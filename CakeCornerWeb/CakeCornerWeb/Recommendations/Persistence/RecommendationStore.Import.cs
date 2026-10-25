using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CakeCornerWeb.Recommendations.Persistence;

public sealed partial class RecommendationStore
{
    /// <summary>
    /// Imports all four CSVs as a single unpublished demonstration version. Import never upgrades
    /// verification flags or reuses an already populated/used annotation identity.
    /// </summary>
    public async Task<RecommendationImportResult> ImportDemoAsync(int actorId, string annotationCode,
        IReadOnlyDictionary<string, Stream> files, RecommendationMethod method, CancellationToken ct = default)
    {
        var package = AnnotationCsv.Parse(files, annotationCode, method);
        if (package.Ingredients.Count > method.MaxCatalogIngredients)
            throw new RecommendationValidationException("Набір перевищує обмеження кількості інгредієнтів методу.");
        foreach (var profile in package.Ingredients.Concat(package.Cakes))
        {
            if (profile.Sweetness is double sweetness) ExactDecimal(sweetness, 2);
            foreach (var intensity in profile.Flavors.Values) ExactDecimal(intensity, 3);
            if (profile.Contains.Intersect(profile.MayContain, StringComparer.Ordinal).Any())
                throw new RecommendationValidationException("Один алерген не може одночасно бути у contains і may_contain.");
        }
        var typeSizes = package.Ingredients.GroupBy(i => i.Type).Select(g => g.Count()).ToArray();
        long expectedPairs = 0;
        for (var i = 0; i < typeSizes.Length; i++)
            for (var j = i + 1; j < typeSizes.Length; j++) expectedPairs += (long)typeSizes[i] * typeSizes[j];
        if (package.Compatibility.Count != expectedPairs)
            throw new RecommendationValidationException("CSV сумісності має містити кожну міжтипову пару імпортованих інгредієнтів рівно один раз; відсутні рішення запишіть як unknown.");

        await RequireInstalled(ct);
        await using var transaction = Context.Database.CurrentTransaction == null
            ? await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            await Execute("""
                DECLARE @result int;
                EXEC @result=sys.sp_getapplock @Resource=N'CakeCorner:Catalog',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
                IF @result<0 THROW 52100,'Catalog is busy; retry import.',1;
                EXEC @result=sys.sp_getapplock @Resource=N'CakeCorner:RecommendationSchema',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
                IF @result<0 THROW 52100,'Recommendation schema is busy; retry import.',1;
                """, ct);
            var admin = Convert.ToInt32(await Scalar("""
                SELECT COUNT(*) FROM dbo.Person p JOIN dbo.[Role] r ON r.Role_ID=p.Role_ID
                WHERE p.Person_ID=@actor AND r.RoleName='Admin';
                """, ct, ("@actor", actorId)), CultureInfo.InvariantCulture);
            if (admin != 1) throw new RecommendationValidationException("Імпорт доступний лише чинному адміністратору.");

            // Resolve IDs against the live commerce catalogue under the same transaction/lock.
            var ingredientIds = package.Ingredients.Select(x => x.Id).ToArray();
            var actualIngredients = await Context.Ingredients.AsNoTracking().Where(i => ingredientIds.Contains(i.Ingredients_ID))
                .Select(i => new { Id = i.Ingredients_ID, Type = i.Type.IngredientsTypesName }).ToDictionaryAsync(i => i.Id, ct);
            if (actualIngredients.Count != ingredientIds.Length || package.Ingredients.Any(i => actualIngredients[i.Id].Type != i.Type))
                throw new RecommendationValidationException("Інгредієнт CSV відсутній у поточній БД або має інший тип. Звірте ID зі своїм каталогом.");
            var cakeIds = package.Cakes.Select(x => x.Id).ToArray();
            if (await Context.Cakes.CountAsync(c => cakeIds.Contains(c.Cake_ID), ct) != cakeIds.Length)
                throw new RecommendationValidationException("Один із готових тортів CSV відсутній у поточній БД.");
            var variantIds = package.Availability.Select(x => x.Id).ToArray();
            var actualVariants = await Context.CakeWeights.AsNoTracking().Where(v => variantIds.Contains(v.CakeWeight_ID))
                .Select(v => new { Id = v.CakeWeight_ID, CakeId = v.Cake_ID }).ToListAsync(ct);
            if (actualVariants.Count != variantIds.Length || actualVariants.Any(v => !cakeIds.Contains(v.CakeId)))
                throw new RecommendationValidationException("Ваговий варіант відсутній у БД або не належить готовому торту з імпортованої таблиці.");

            var tags = await ReadCodes("SELECT Code,TasteTag_ID FROM dbo.TasteTag;", ct);
            var allergens = await ReadCodes("SELECT Code,Allergen_ID FROM dbo.Allergen;", ct);
            if (method.FlavorTags.Any(t => !tags.ContainsKey(t)) || method.AllergenCodes.Any(t => !allergens.ContainsKey(t)))
                throw new RecommendationValidationException("Словник БД не відповідає конфігурації. Спочатку встановіть узгоджену схему 004.");

            var featureSchema = Serialize(new
            {
                schemaVersion = "cakecorner-features-v2-no-soaking", ingredientTypes = method.Slots,
                sweetness = new { min = 0, max = 5 }, tasteIntensity = new { min = 0, max = 1 },
                tasteTags = method.FlavorTags, allergens = method.AllergenCodes
            });
            var currentAnnotation = await Scalar("SELECT AnnotationVersion_ID FROM dbo.AnnotationVersion WITH(UPDLOCK,HOLDLOCK) WHERE VersionCode=@version;", ct, ("@version", annotationCode));
            int annotationId;
            if (currentAnnotation is null or DBNull)
            {
                annotationId = Convert.ToInt32(await Scalar("""
                    INSERT dbo.AnnotationVersion(VersionCode,FeatureSchemaJson,SourceDescription)
                    VALUES(@version,@schema,@source); SELECT CONVERT(int,SCOPE_IDENTITY());
                    """, ct, ("@version", annotationCode), ("@schema", featureSchema),
                    ("@source", "Four-file demonstration import. Sensory and compatibility values remain unverified hypotheses; availability is an assumption. No versions are automatically published.")), CultureInfo.InvariantCulture);
            }
            else
            {
                annotationId = Convert.ToInt32(currentAnnotation, CultureInfo.InvariantCulture);
                var eligible = Convert.ToInt32(await Scalar("""
                    SELECT COUNT(*) FROM dbo.AnnotationVersion a WHERE a.AnnotationVersion_ID=@a AND a.Status='Draft'
                    AND NOT EXISTS(SELECT 1 FROM dbo.RecommendationRequest r WHERE r.AnnotationVersion_ID=a.AnnotationVersion_ID)
                    AND NOT EXISTS(SELECT 1 FROM dbo.ProductRecommendationProfile p WHERE p.AnnotationVersion_ID=a.AnnotationVersion_ID)
                    AND NOT EXISTS(SELECT 1 FROM dbo.IngredientCompatibility p WHERE p.AnnotationVersion_ID=a.AnnotationVersion_ID)
                    AND NOT EXISTS(SELECT 1 FROM dbo.AvailabilityObservation p WHERE p.AnnotationVersion_ID=a.AnnotationVersion_ID);
                    """, ct, ("@a", annotationId)), CultureInfo.InvariantCulture);
                if (eligible != 1) throw new RecommendationValidationException("Цю версію вже заповнено, використано або опубліковано. Створіть новий код версії; старі дані не перезаписуються.");
                var existingSchema = Convert.ToString(await Scalar("SELECT FeatureSchemaJson FROM dbo.AnnotationVersion WHERE AnnotationVersion_ID=@a;", ct, ("@a", annotationId)), CultureInfo.InvariantCulture);
                if (!CompatibleFeatureSchema(existingSchema, method))
                    throw new RecommendationValidationException("Наявна порожня версія має іншу схему ознак. Вкажіть новий код версії.");
            }

            var configJson = Serialize(method);
            var existingMethod = await Scalar("SELECT ConfigJson FROM dbo.RecommendationMethodVersion WITH(UPDLOCK,HOLDLOCK) WHERE VersionCode=@version;", ct, ("@version", method.Version));
            if (existingMethod is string config)
            {
                if (Canonical(config) != Canonical(configJson))
                    throw new RecommendationValidationException("Цей код методу вже відповідає іншій конфігурації. Змініть версію методу, а не опубліковані параметри.");
                var archived = Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM dbo.RecommendationMethodVersion WHERE VersionCode=@version AND Status='Archived';", ct, ("@version", method.Version)), CultureInfo.InvariantCulture);
                if (archived != 0) throw new RecommendationValidationException("Версію методу заархівовано; потрібен новий код.");
            }
            else
                await Execute("""
                    INSERT dbo.RecommendationMethodVersion(VersionCode,Algorithm,ConfigJson,CodeVersion)
                    VALUES(@version,N'content+knowledge+Sugeno',@config,N'CakeCorner / two-input Sugeno runtime');
                    """, ct, ("@version", method.Version), ("@config", configJson));

            foreach (var profile in package.Ingredients)
            {
                await InsertProfile(profile, true, annotationId, tags, allergens, ct);
                await InsertAvailability(profile.Id, true, profile.Availability!, annotationId, profile.Reviewer, profile.Source, ct);
            }
            foreach (var profile in package.Cakes) await InsertProfile(profile, false, annotationId, tags, allergens, ct);
            foreach (var pair in package.Compatibility)
                await Execute("""
                    INSERT dbo.IngredientCompatibility(IngredientA_ID,IngredientB_ID,AnnotationVersion_ID,Decision,VerificationStatus,Reason,ReviewerRef,SourceRef,ReviewedAtUtc)
                    VALUES(@a,@b,@version,@decision,'Draft',@reason,@reviewer,@source,@reviewed);
                    """, ct, ("@a", pair.A), ("@b", pair.B), ("@version", annotationId), ("@decision", pair.Decision),
                    ("@reason", pair.Reason), ("@reviewer", pair.Reviewer), ("@source", pair.Source), ("@reviewed", pair.ReviewedAt));
            foreach (var item in package.Availability)
                await InsertAvailability(item.Id, false, item.Status, annotationId, item.Reviewer, item.Source, ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return new(annotationCode, method.Version, package.Ingredients.Count, package.Cakes.Count, package.Compatibility.Count, package.Availability.Count);
        }
        catch
        {
            if (transaction != null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<Dictionary<string, int>> ReadCodes(string sql, CancellationToken ct)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var command = await CommandAsync(sql, ct);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(reader.GetString(0), reader.GetInt32(1));
        return result;
    }
    private async Task InsertProfile(CsvProfile profile, bool ingredient, int annotationId,
        Dictionary<string, int> tags, Dictionary<string, int> allergens, CancellationToken ct)
    {
        var id = Convert.ToInt32(await Scalar("""
            INSERT dbo.ProductRecommendationProfile(Cake_ID,Ingredients_ID,AnnotationVersion_ID,Sweetness,SensoryVerificationStatus,AllergenVerificationStatus,SourceType,ReviewerRef,SourceRef,ReviewedAtUtc)
            VALUES(@cake,@ingredient,@version,@sweetness,'Draft','Unknown','AI',@reviewer,@source,@reviewed); SELECT CONVERT(int,SCOPE_IDENTITY());
            """, ct, ("@cake", ingredient ? null : profile.Id), ("@ingredient", ingredient ? profile.Id : null),
            ("@version", annotationId), ("@sweetness", profile.Sweetness is double s ? ExactDecimal(s, 2) : null),
            ("@reviewer", profile.Reviewer), ("@source", profile.Source), ("@reviewed", profile.ReviewedAt)), CultureInfo.InvariantCulture);
        foreach (var flavor in profile.Flavors)
            await Execute("INSERT dbo.ProductTasteScore(Profile_ID,TasteTag_ID,Intensity) VALUES(@profile,@tag,@value);", ct,
                ("@profile", id), ("@tag", tags[flavor.Key]), ("@value", ExactDecimal(flavor.Value, 3)));
        // Known assertions may be retained while overall completeness remains explicitly Unknown.
        foreach (var (presence, values) in new[] { ("contains", profile.Contains), ("may_contain", profile.MayContain) })
            foreach (var value in values)
                await Execute("INSERT dbo.ProductProfileAllergen(Profile_ID,Allergen_ID,Presence) VALUES(@profile,@allergen,@presence);", ct,
                    ("@profile", id), ("@allergen", allergens[value]), ("@presence", presence));
    }
    private async Task InsertAvailability(int id, bool ingredient, string status, int annotationId, string reviewer, string source, CancellationToken ct)
    {
        // A date supplied in a demonstration CSV is not evidence of an observed stock level.
        await Execute("""
            INSERT dbo.AvailabilityObservation(Ingredients_ID,CakeWeight_ID,AnnotationVersion_ID,Status,VerificationStatus,ReviewerRef,SourceRef)
            VALUES(@ingredient,@variant,@version,@status,'Draft',@reviewer,@source);
            """, ct, ("@ingredient", ingredient ? id : null), ("@variant", ingredient ? null : id),
            ("@version", annotationId), ("@status", status), ("@reviewer", reviewer), ("@source", source));
    }
    private static decimal ExactDecimal(double value, int scale)
    {
        var result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        if (decimal.Round(result, scale) != result)
            throw new RecommendationValidationException($"Оцінка має містити не більше {scale} знаків після крапки, відповідно до схеми БД.");
        return result;
    }
    private static bool CompatibleFeatureSchema(string? json, RecommendationMethod method)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.TryGetProperty("tasteTags", out var tags) && tags.ValueKind == JsonValueKind.Array
                && tags.EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal).SetEquals(method.FlavorTags)
                && root.TryGetProperty("sweetness", out var sweetness)
                && sweetness.TryGetProperty("min", out var sweetMin) && sweetMin.GetDecimal() == 0
                && sweetness.TryGetProperty("max", out var sweetMax) && sweetMax.GetDecimal() == 5
                && root.TryGetProperty("tasteIntensity", out var intensity)
                && intensity.TryGetProperty("min", out var intensityMin) && intensityMin.GetDecimal() == 0
                && intensity.TryGetProperty("max", out var intensityMax) && intensityMax.GetDecimal() == 1
                && root.TryGetProperty("ingredientTypes", out var slots) && slots.ValueKind == JsonValueKind.Array
                && slots.EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal).SetEquals(method.Slots);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return false; }
    }
}
