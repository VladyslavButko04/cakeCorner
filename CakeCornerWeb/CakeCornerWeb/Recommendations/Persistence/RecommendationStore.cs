using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using CakeCornerWeb.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CakeCornerWeb.Recommendations.Persistence;

public sealed record AnnotationVersionInfo(string VersionCode, string Status, int IngredientCount, int ReadyCount, int PairCount);
public sealed record RecommendationStorageStatus(bool Installed, List<AnnotationVersionInfo> AnnotationVersions);
public sealed record RecommendationImportResult(string AnnotationVersion, string MethodVersion, int Ingredients, int Cakes, int CompatibilityPairs, int ReadyVariants);
public sealed record LoggedRecommendation(RecommendationQuery Query, RecommendationItemDto Item, string AnnotationVersion, string MethodVersion);

/// <summary>Parameterized persistence over the existing recommendation schema. It never creates the database.</summary>
public sealed partial class RecommendationStore(ApplicationDbContext context)
{
    private ApplicationDbContext Context => context;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string SchemaCheck = """
        SELECT CASE WHEN
          OBJECT_ID(N'dbo.AnnotationVersion',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.ProductRecommendationProfile',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.ProductTasteScore',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.IngredientCompatibility',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.AvailabilityObservation',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.RecommendationMethodVersion',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.RecommendationRequest',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.RecommendationItem',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.UserInteraction',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.UserRecommendationProfile',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.UserTastePreference',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.UserAllergenExclusion',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.ProductProfileAllergen',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.Allergen',N'U') IS NOT NULL AND
          OBJECT_ID(N'dbo.TasteTag',N'U') IS NOT NULL AND
          COL_LENGTH(N'dbo.UserInteraction',N'Ingredients_ID') IS NOT NULL AND
          COL_LENGTH(N'dbo.UserInteraction',N'CandidateKey') IS NOT NULL AND
          COL_LENGTH(N'dbo.UserInteraction',N'DisplayPosition') IS NOT NULL
        THEN 1 ELSE 0 END;
        """;

    private async Task<DbCommand> CommandAsync(string text, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        if (context.Database.GetDbConnection().State != ConnectionState.Open) await context.Database.OpenConnectionAsync(ct);
        var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = text;
        command.CommandTimeout = 30;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        foreach (var (name, value) in parameters)
        {
            var p = command.CreateParameter(); p.ParameterName = name; p.Value = value ?? DBNull.Value;
            // Inferred SQL datetime rounds to ~3.33 ms and can even move a timestamp forwards.
            if (value is DateTime) { p.DbType = DbType.DateTime2; p.Scale = 7; }
            command.Parameters.Add(p);
        }
        return command;
    }
    private async Task<object?> Scalar(string text, CancellationToken ct, params (string Name, object? Value)[] parameters)
    { await using var c = await CommandAsync(text, ct, parameters); return await c.ExecuteScalarAsync(ct); }
    private async Task Execute(string text, CancellationToken ct, params (string Name, object? Value)[] parameters)
    { await using var c = await CommandAsync(text, ct, parameters); await c.ExecuteNonQueryAsync(ct); }
    private async Task<bool> Installed(CancellationToken ct) => Convert.ToInt32(await Scalar(SchemaCheck, ct), CultureInfo.InvariantCulture) == 1;
    private async Task RequireInstalled(CancellationToken ct)
    { if (!await Installed(ct)) throw new RecommendationUnavailableException("Рекомендації ще не налаштовано. Адміністратор має виконати SQL 004 та 011 і імпортувати розмітку."); }
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, Json) ?? throw new RecommendationUnavailableException("Пошкоджений знімок рекомендації.");
    private static DateTime? Utc(DbDataReader r, int i) => r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc);
    private static string? String(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static string Canonical(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            object? Value(JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.Object)
                {
                    var properties = e.EnumerateObject().ToList();
                    if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Count)
                        throw new JsonException("Duplicate configuration key.");
                    return properties.OrderBy(p => p.Name, StringComparer.Ordinal).ToDictionary(p => p.Name, p => Value(p.Value));
                }
                return e.ValueKind switch
                {
                    JsonValueKind.Array => e.EnumerateArray().Select(Value).ToArray(),
                    JsonValueKind.String => e.GetString(),
                    JsonValueKind.Number => e.TryGetDecimal(out var number) ? number : e.Clone(),
                    JsonValueKind.True => true, JsonValueKind.False => false, _ => null
                };
            }
            return JsonSerializer.Serialize(Value(doc.RootElement));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or ArgumentException)
        { throw new RecommendationUnavailableException("Некоректна збережена конфігурація методу. Перевірте JSON та зареєструйте узгоджену версію."); }
    }

    public async Task<RecommendationStorageStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (!await Installed(ct)) return new(false, []);
        var versions = new List<AnnotationVersionInfo>();
        await using var c = await CommandAsync("""
            SELECT a.VersionCode,a.Status,
              (SELECT COUNT(*) FROM dbo.ProductRecommendationProfile p WHERE p.AnnotationVersion_ID=a.AnnotationVersion_ID AND p.Ingredients_ID IS NOT NULL),
              (SELECT COUNT(*) FROM dbo.ProductRecommendationProfile p WHERE p.AnnotationVersion_ID=a.AnnotationVersion_ID AND p.Cake_ID IS NOT NULL),
              (SELECT COUNT(*) FROM dbo.IngredientCompatibility p WHERE p.AnnotationVersion_ID=a.AnnotationVersion_ID)
            FROM dbo.AnnotationVersion a ORDER BY a.CreatedAtUtc DESC,a.AnnotationVersion_ID DESC;
            """, ct);
        await using var r = await c.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) versions.Add(new(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4)));
        return new(true, versions);
    }

    public async Task<RecommendationSnapshot> LoadSnapshotAsync(string annotationCode, RecommendationMethod definition, string dataMode, CancellationToken ct = default)
    {
        await RequireInstalled(ct);
        var snapshot = new RecommendationSnapshot { AnnotationVersion = annotationCode, MethodVersion = definition.Version, CapturedAtUtc = DateTime.UtcNow };
        await using (var c = await CommandAsync("""
            SELECT a.AnnotationVersion_ID,a.Status,m.MethodVersion_ID,m.Status,m.ConfigJson,
                CONVERT(datetime2(3),SYSUTCDATETIME()) AS CapturedAtUtc
            FROM dbo.AnnotationVersion a CROSS JOIN dbo.RecommendationMethodVersion m
            WHERE a.VersionCode=@a AND m.VersionCode=@m;
            """, ct, ("@a", annotationCode), ("@m", definition.Version)))
        await using (var r = await c.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) throw new RecommendationUnavailableException("Обрана версія розмітки або методу ще не імпортована.");
            if (r.GetString(1) == "Archived" || r.GetString(3) == "Archived") throw new RecommendationUnavailableException("Цю версію заархівовано. Оберіть актуальну розмітку.");
            snapshot.AnnotationVersionId = r.GetInt32(0); snapshot.AnnotationPublished = r.GetString(1) == "Published";
            snapshot.MethodVersionId = r.GetInt32(2); snapshot.MethodPublished = r.GetString(3) == "Published";
            // Use one clock for both ends of the request; web/SQL hosts need not have identical clocks.
            snapshot.CapturedAtUtc = DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc);
            try { if (Canonical(r.GetString(4)) != Canonical(Serialize(definition))) throw new RecommendationUnavailableException("Збережена конфігурація відрізняється від поточної. Зареєструйте нову версію методу."); }
            catch (JsonException) { throw new RecommendationUnavailableException("Некоректний JSON конфігурації методу."); }
        }
        if (dataMode == "Verified" && (!snapshot.AnnotationPublished || !snapshot.MethodPublished))
            throw new RecommendationUnavailableException("Перевірений режим потребує опублікованих версій методу й розмітки.");
        var profileById = new Dictionary<int, RecommendationFeatures>();
        var ingredientFeatures = new Dictionary<int, RecommendationFeatures>(); var cakeFeatures = new Dictionary<int, RecommendationFeatures>();
        await using (var c = await CommandAsync("""
            SELECT Profile_ID,Ingredients_ID,Cake_ID,Sweetness,SensoryVerificationStatus,AllergenVerificationStatus
            FROM dbo.ProductRecommendationProfile WHERE AnnotationVersion_ID=@a;
            SELECT s.Profile_ID,t.Code,s.Intensity FROM dbo.ProductTasteScore s JOIN dbo.TasteTag t ON t.TasteTag_ID=s.TasteTag_ID
            JOIN dbo.ProductRecommendationProfile p ON p.Profile_ID=s.Profile_ID WHERE p.AnnotationVersion_ID=@a;
            SELECT p.Profile_ID,a.Code,s.Presence FROM dbo.ProductProfileAllergen s JOIN dbo.Allergen a ON a.Allergen_ID=s.Allergen_ID
            JOIN dbo.ProductRecommendationProfile p ON p.Profile_ID=s.Profile_ID WHERE p.AnnotationVersion_ID=@a;
            SELECT IngredientA_ID,IngredientB_ID,Decision,VerificationStatus,Reason FROM dbo.IngredientCompatibility WHERE AnnotationVersion_ID=@a;
            """, ct, ("@a", snapshot.AnnotationVersionId)))
        await using (var r = await c.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var f = new RecommendationFeatures { Sweetness = r.IsDBNull(3) ? null : Convert.ToDouble(r.GetDecimal(3)), SensoryVerified = r.GetString(4) == "Verified", AllergenStatus = r.GetString(5) };
                profileById[r.GetInt32(0)] = f;
                if (!r.IsDBNull(1)) ingredientFeatures[r.GetInt32(1)] = f;
                else if (!r.IsDBNull(2)) cakeFeatures[r.GetInt32(2)] = f;
            }
            await r.NextResultAsync(ct);
            while (await r.ReadAsync(ct)) if (profileById.TryGetValue(r.GetInt32(0), out var f)) f.Flavors[r.GetString(1)] = Convert.ToDouble(r.GetDecimal(2));
            await r.NextResultAsync(ct);
            while (await r.ReadAsync(ct)) if (profileById.TryGetValue(r.GetInt32(0), out var f)) (r.GetString(2) == "contains" ? f.Contains : f.MayContain).Add(r.GetString(1));
            await r.NextResultAsync(ct);
            while (await r.ReadAsync(ct)) snapshot.Compatibility.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3) == "Verified", String(r, 4)));
        }
        var ingredientAvailability = new Dictionary<int, RecommendationFeatures>(); var variantAvailability = new Dictionary<int, RecommendationFeatures>();
        await using (var c = await CommandAsync("""
            ;WITH Latest AS
            (
                SELECT a.*,ROW_NUMBER() OVER(PARTITION BY Ingredients_ID,CakeWeight_ID ORDER BY
                    COALESCE(ObservedAtUtc,RecordedAtUtc) DESC,RecordedAtUtc DESC,AvailabilityObservation_ID DESC) AS rn
                FROM dbo.AvailabilityObservation a WHERE AnnotationVersion_ID=@a OR AnnotationVersion_ID IS NULL
                    OR (VerificationStatus='Verified' AND ObservedAtUtc IS NOT NULL)
            )
            SELECT Ingredients_ID,CakeWeight_ID,Status,VerificationStatus,ObservedAtUtc,ValidUntilUtc FROM Latest WHERE rn=1;
            """, ct, ("@a", snapshot.AnnotationVersionId)))
        await using (var r = await c.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                var f = new RecommendationFeatures { Availability = r.GetString(2), AvailabilityVerified = r.GetString(3) == "Verified", ObservedAtUtc = Utc(r, 4), ValidUntilUtc = Utc(r, 5) };
                if (!r.IsDBNull(0)) ingredientAvailability[r.GetInt32(0)] = f; else variantAvailability[r.GetInt32(1)] = f;
            }
        RecommendationFeatures? Merge(RecommendationFeatures? profile, RecommendationFeatures? availability)
        {
            if (profile == null && availability == null) return null;
            // Ready variants share sensory profiles but must never overwrite each other's availability.
            var f = profile == null ? new RecommendationFeatures() : Deserialize<RecommendationFeatures>(Serialize(profile));
            if (availability != null) { f.Availability = availability.Availability; f.AvailabilityVerified = availability.AvailabilityVerified; f.ObservedAtUtc = availability.ObservedAtUtc; f.ValidUntilUtc = availability.ValidUntilUtc; }
            return f;
        }
        var ingredients = await context.Ingredients.AsNoTracking().Include(i => i.Type).OrderBy(i => i.Ingredients_ID).ToListAsync(ct);
        snapshot.Ingredients = ingredients.Select(i => new IngredientOption { Id = i.Ingredients_ID, Type = i.Type.IngredientsTypesName, Name = i.IngredientName ?? "", ImagePath = i.IngredientImagePath, Rate = i.Price, IsActive = i.IsActive,
            Features = Merge(ingredientFeatures.GetValueOrDefault(i.Ingredients_ID), ingredientAvailability.GetValueOrDefault(i.Ingredients_ID)) }).ToList();
        var variants = await context.CakeWeights.AsNoTracking().Include(w => w.Cake).ThenInclude(c => c.ProductInformation)
            .Include(w => w.Cake).ThenInclude(c => c.Allergens).ThenInclude(a => a.Allergen)
            .Include(w => w.WeightOption).AsSplitQuery().OrderBy(w => w.CakeWeight_ID).ToListAsync(ct);
        snapshot.Ready = variants.Select(w => new ReadyOption { CakeId = w.Cake_ID, CakeWeightId = w.CakeWeight_ID, Name = w.Cake.CakeName, ImagePath = w.Cake.ImagePath, WeightKg = w.WeightOption.Weight, UnitPrice = w.Price, IsActive = w.IsActive && w.Cake.IsActive,
            Features = Runtime.CatalogAllergenGuard.Apply(Merge(cakeFeatures.GetValueOrDefault(w.Cake_ID), variantAvailability.GetValueOrDefault(w.CakeWeight_ID)), w.Cake) }).ToList();
        return snapshot;
    }

    public async Task<RecommendationPreferences> LoadPreferencesAsync(int personId, CancellationToken ct = default)
    {
        if (!await Installed(ct)) return new();
        var result = new RecommendationPreferences();
        await using var c = await CommandAsync("""
            SELECT DesiredSweetness FROM dbo.UserRecommendationProfile WHERE Person_ID=@person;
            SELECT t.Code,p.PreferenceWeight FROM dbo.UserTastePreference p JOIN dbo.TasteTag t ON t.TasteTag_ID=p.TasteTag_ID WHERE p.Person_ID=@person;
            SELECT a.Code FROM dbo.UserAllergenExclusion p JOIN dbo.Allergen a ON a.Allergen_ID=p.Allergen_ID WHERE p.Person_ID=@person;
            """, ct, ("@person", personId));
        await using var r = await c.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct)) result.DesiredSweetness = r.IsDBNull(0) ? null : Convert.ToDouble(r.GetDecimal(0));
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct)) result.PreferredFlavors[r.GetString(0)] = Convert.ToDouble(r.GetDecimal(1));
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct)) result.ExcludedAllergens.Add(r.GetString(0));
        return result;
    }

    public async Task SavePreferencesAsync(int personId, RecommendationPreferences preferences, CancellationToken ct = default)
    {
        await RequireInstalled(ct);
        if (preferences.PreferredFlavors == null || preferences.ExcludedAllergens == null || preferences.PreferredFlavors.Count > 100 || preferences.ExcludedAllergens.Count > 100
            || preferences.PreferredFlavors.Any(p => !double.IsFinite(p.Value) || p.Value is < 0 or > 5 || p.Key.Length > 40)
            || (preferences.DesiredSweetness is double sweetness && (!double.IsFinite(sweetness) || sweetness is < 0 or > 5)))
            throw new RecommendationValidationException("Некоректні значення вподобань.");
        await using var owned = context.Database.CurrentTransaction == null ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        await Execute("""
            IF NOT EXISTS(SELECT 1 FROM dbo.Person WHERE Person_ID=@person) THROW 52101,'Account no longer exists.',1;
            UPDATE dbo.UserRecommendationProfile WITH(UPDLOCK,HOLDLOCK) SET DesiredSweetness=@sweetness,UpdatedAtUtc=SYSUTCDATETIME() WHERE Person_ID=@person;
            IF @@ROWCOUNT=0 INSERT dbo.UserRecommendationProfile(Person_ID,DesiredSweetness) VALUES(@person,@sweetness);
            DELETE dbo.UserTastePreference WHERE Person_ID=@person;
            DELETE dbo.UserAllergenExclusion WHERE Person_ID=@person;
            """, ct, ("@person", personId), ("@sweetness", preferences.DesiredSweetness));
        foreach (var (tag, weight) in preferences.PreferredFlavors)
            await Execute("""
                IF NOT EXISTS(SELECT 1 FROM dbo.TasteTag WHERE Code COLLATE Latin1_General_100_BIN2=@tag) THROW 52102,'Unknown taste code.',1;
                INSERT dbo.UserTastePreference(Person_ID,TasteTag_ID,PreferenceWeight) SELECT @person,TasteTag_ID,@weight FROM dbo.TasteTag WHERE Code COLLATE Latin1_General_100_BIN2=@tag;
                """, ct, ("@person", personId), ("@tag", tag), ("@weight", weight));
        foreach (var allergen in preferences.ExcludedAllergens.Distinct(StringComparer.Ordinal))
            await Execute("""
                IF NOT EXISTS(SELECT 1 FROM dbo.Allergen WHERE Code COLLATE Latin1_General_100_BIN2=@code) THROW 52103,'Unknown allergen code.',1;
                INSERT dbo.UserAllergenExclusion(Person_ID,Allergen_ID) SELECT @person,Allergen_ID FROM dbo.Allergen WHERE Code COLLATE Latin1_General_100_BIN2=@code;
                """, ct, ("@person", personId), ("@code", allergen));
        if (owned != null) await owned.CommitAsync(ct);
    }

    public async Task LogResultAsync(int? personId, Guid? sessionId, RecommendationQuery query, RecommendationSnapshot snapshot, RecommendationResult result, CancellationToken ct = default)
    {
        await RequireInstalled(ct);
        if (personId == null && (sessionId == null || sessionId == Guid.Empty)) throw new RecommendationValidationException("Потрібна серверна сесія рекомендацій.");
        await using var owned = context.Database.CurrentTransaction == null ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var status = result.Status switch { "ok" => "Completed", "no_eligible_candidates" => "NoCandidates", "insufficient_metadata" => "InsufficientMetadata", _ => "Failed" };
        var requestId = Convert.ToInt64(await Scalar("""
            DECLARE @ids table(ID bigint);
            INSERT dbo.RecommendationRequest(Person_ID,AnonymousSession_ID,MethodVersion_ID,AnnotationVersion_ID,DataMode,ProductMode,Status,
                CreatedAtUtc,CompletedAtUtc,ContextJson,ProfileSnapshotJson,CandidatePoolSnapshotJson,ResultSummaryJson)
            OUTPUT INSERTED.Request_ID INTO @ids(ID)
            VALUES(@person,@session,@method,@annotation,@mode,@product,@status,@captured,SYSUTCDATETIME(),@context,@preferences,@pool,N'{}');
            SELECT ID FROM @ids;
            """, ct, ("@person", personId), ("@session", personId == null ? sessionId : null), ("@method", snapshot.MethodVersionId), ("@annotation", snapshot.AnnotationVersionId),
            ("@mode", query.DataMode), ("@product", query.Mode == "Ready" ? "Ready" : "Custom"), ("@status", status), ("@captured", snapshot.CapturedAtUtc),
            ("@context", Serialize(query)), ("@preferences", Serialize(new RecommendationPreferences { PreferredFlavors = query.PreferredFlavors, DesiredSweetness = query.DesiredSweetness, ExcludedAllergens = query.ExcludedAllergens })),
            ("@pool", Serialize(snapshot))), CultureInfo.InvariantCulture);
        result.RequestId = requestId;
        for (var rank = 0; rank < result.Items.Count; rank++)
        {
            var item = result.Items[rank];
            if (item.Kind is not ("Ready" or "Custom")) continue; // Ingredient suggestions are fully preserved in ResultSummaryJson.
            item.LogItemId = Convert.ToInt64(await Scalar("""
                DECLARE @ids table(ID bigint);
                INSERT dbo.RecommendationItem(Request_ID,ProductKind,CakeWeight_ID,Biscuit_ID,Cream_ID,Filling_ID,MousseCheeseCake_ID,Shape,WeightKg,Quantity,
                    UnitPriceSnapshot,ContentScore,FuzzyScore,TotalScore,Rank,FeatureSnapshotJson)
                OUTPUT INSERTED.RecommendationItem_ID INTO @ids(ID)
                VALUES(@request,@kind,@variant,@biscuit,@cream,@filling,@mousse,@shape,@weight,@quantity,@price,@content,@fuzzy,@total,@rank,@features);
                SELECT ID FROM @ids;
                """, ct, ("@request", requestId), ("@kind", item.Kind), ("@variant", item.CakeWeightId),
                ("@biscuit", item.Kind == "Custom" ? item.Ingredients.GetValueOrDefault("Biscuit") : null),
                ("@cream", item.Kind == "Custom" ? item.Ingredients.GetValueOrDefault("Cream") : null),
                ("@filling", item.Kind == "Custom" ? item.Ingredients.GetValueOrDefault("Filling") : null),
                ("@mousse", item.Kind == "Custom" ? item.Ingredients.GetValueOrDefault("Mousse") : null),
                ("@shape", item.Kind == "Custom" ? item.Shape : null), ("@weight", item.WeightKg), ("@quantity", item.Quantity), ("@price", item.UnitPrice),
                ("@content", item.TasteFit), ("@fuzzy", item.Strategy == "hybrid-fuzzy" ? item.Score : null), ("@total", item.Score), ("@rank", rank + 1), ("@features", Serialize(item))), CultureInfo.InvariantCulture);
        }
        await Execute("UPDATE dbo.RecommendationRequest SET ResultSummaryJson=@result WHERE Request_ID=@request;", ct, ("@request", requestId), ("@result", Serialize(result)));
        if (owned != null) await owned.CommitAsync(ct);
    }

    private async Task<(RecommendationQuery Query, RecommendationResult Result, string Annotation, string Method)?> FindLoggedResult(int? personId, Guid? sessionId, long requestId, CancellationToken ct)
    {
        await RequireInstalled(ct);
        await using var c = await CommandAsync("""
            SELECT r.ContextJson,r.ResultSummaryJson,a.VersionCode,m.VersionCode
            FROM dbo.RecommendationRequest r JOIN dbo.AnnotationVersion a ON a.AnnotationVersion_ID=r.AnnotationVersion_ID
            JOIN dbo.RecommendationMethodVersion m ON m.MethodVersion_ID=r.MethodVersion_ID
            WHERE r.Request_ID=@request AND ((@person IS NOT NULL AND r.Person_ID=@person)
                OR (@person IS NULL AND r.Person_ID IS NULL AND r.AnonymousSession_ID=@session));
            """, ct, ("@request", requestId), ("@person", personId), ("@session", sessionId));
        await using var reader = await c.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(1)) return null;
        return (Deserialize<RecommendationQuery>(reader.GetString(0)), Deserialize<RecommendationResult>(reader.GetString(1)), reader.GetString(2), reader.GetString(3));
    }

    public async Task<LoggedRecommendation?> FindLoggedItemAsync(int? personId, Guid? sessionId, long requestId, string candidateKey, CancellationToken ct = default)
    {
        if (requestId <= 0 || string.IsNullOrEmpty(candidateKey) || candidateKey.Length > 200) return null;
        var logged = await FindLoggedResult(personId, sessionId, requestId, ct);
        var item = logged?.Result.Items.SingleOrDefault(i => i.Key == candidateKey);
        return item == null || logged == null ? null : new(logged.Value.Query, item, logged.Value.Annotation, logged.Value.Method);
    }

    public async Task<bool> HasEventAsync(int? personId, Guid? sessionId, Guid eventKey, string eventType = "add_to_cart", CancellationToken ct = default)
    {
        if (eventKey == Guid.Empty) throw new RecommendationValidationException("Потрібен ідентифікатор події.");
        await RequireInstalled(ct);
        await using var c = await CommandAsync("SELECT Person_ID,AnonymousSession_ID,EventType FROM dbo.UserInteraction WITH(UPDLOCK,HOLDLOCK) WHERE EventKey=@key;", ct, ("@key", eventKey));
        await using var reader = await c.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return false;
        var sameOwner = personId != null ? !reader.IsDBNull(0) && reader.GetInt32(0) == personId : reader.IsDBNull(0) && !reader.IsDBNull(1) && reader.GetGuid(1) == sessionId;
        if (!sameOwner || reader.GetString(2) != eventType) throw new RecommendationValidationException("Ідентифікатор події вже використано іншим запитом.");
        return true;
    }

    public async Task<bool> RecordEventAsync(int? personId, Guid? sessionId, RecommendationEventRequest request, CancellationToken ct = default)
    {
        await RequireInstalled(ct);
        if (request.EventKey == Guid.Empty || request.EventType is not ("impression" or "detail_view" or "add_to_cart" or "remove_from_cart" or "explicit_rating")
            || (request.EventType == "explicit_rating" ? request.Rating is null or < 0 or > 3 : request.Rating != null))
            throw new RecommendationValidationException("Некоректна подія рекомендації.");
        await using var owned = context.Database.CurrentTransaction == null ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var logged = await FindLoggedResult(personId, sessionId, request.RequestId, ct);
        var index = logged?.Result.Items.FindIndex(i => i.Key == request.CandidateKey) ?? -1;
        if (index < 0 || logged == null) throw new RecommendationValidationException("Рекомендація не належить поточному користувачу або сесії.");
        var item = logged.Value.Result.Items[index];
        if (request.EventType == "impression" ? request.Position != index + 1 : request.Position != null)
            throw new RecommendationValidationException("Некоректна позиція показу.");
        if (await HasEventAsync(personId, sessionId, request.EventKey, request.EventType, ct))
        {
            var matching = Convert.ToInt32(await Scalar("""
                SELECT COUNT(*) FROM dbo.UserInteraction WHERE EventKey=@key AND Request_ID=@request AND CandidateKey COLLATE Latin1_General_100_BIN2=@candidate
                AND (Rating=@rating OR(Rating IS NULL AND @rating IS NULL)) AND(DisplayPosition=@position OR(DisplayPosition IS NULL AND @position IS NULL));
                """, ct, ("@key", request.EventKey), ("@request", request.RequestId), ("@candidate", request.CandidateKey), ("@rating", request.Rating), ("@position", request.Position)), CultureInfo.InvariantCulture);
            if (matching != 1) throw new RecommendationValidationException("Повторний ключ події містить інші дані.");
            if (owned != null) await owned.CommitAsync(ct);
            return false;
        }
        if (request.EventType == "impression" && item.LogItemId != null)
            await Execute("""
                IF EXISTS(SELECT 1 FROM dbo.RecommendationItem WHERE RecommendationItem_ID=@item AND Request_ID=@request AND DisplayPosition IS NOT NULL AND DisplayPosition<>@position)
                    THROW 52104,'Stored display position differs from this request.',1;
                UPDATE dbo.RecommendationItem SET DisplayPosition=COALESCE(DisplayPosition,@position) WHERE RecommendationItem_ID=@item AND Request_ID=@request;
                """, ct,
                ("@item", item.LogItemId), ("@request", request.RequestId), ("@position", request.Position));
        await Execute("""
            INSERT dbo.UserInteraction(EventKey,Person_ID,AnonymousSession_ID,Request_ID,RecommendationItem_ID,Cake_ID,CakeWeight_ID,Ingredients_ID,
                CandidateKey,DisplayPosition,EventType,Rating,OccurredAtUtc)
            VALUES(@key,@person,@session,@request,@item,@cake,@variant,@ingredient,@candidate,@position,@type,@rating,SYSUTCDATETIME());
            """, ct, ("@key", request.EventKey), ("@person", personId), ("@session", personId == null ? sessionId : null), ("@request", request.RequestId), ("@item", item.LogItemId),
            ("@cake", item.CakeId), ("@variant", item.CakeWeightId), ("@ingredient", item.IngredientId), ("@candidate", request.CandidateKey), ("@position", request.Position), ("@type", request.EventType), ("@rating", request.Rating));
        if (owned != null) await owned.CommitAsync(ct);
        return true;
    }

    public async Task<bool> RecordCommerceAsync(int personId, string eventType, Guid eventKey, int? cakeWeightId = null, int? customCakeId = null, int? orderId = null, CancellationToken ct = default)
    {
        if (!await Installed(ct)) return false; // Telemetry is optional until the administrator installs it.
        if (eventType is not ("add_to_cart" or "remove_from_cart" or "order_completed") || eventKey == Guid.Empty || (cakeWeightId == null && customCakeId == null && orderId == null) || (eventType == "order_completed" && orderId == null))
            throw new RecommendationValidationException("Некоректна подія покупки.");
        await using var owned = context.Database.CurrentTransaction == null ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        if (customCakeId != null && !await context.CustomCakes.AnyAsync(c => c.CustomCake_ID == customCakeId && c.Person_ID == personId, ct))
            throw new RecommendationValidationException("Конфігурація не належить користувачу.");
        if (orderId != null)
        {
            var order = await context.Orders.AsNoTracking().Include(o => o.Status).SingleOrDefaultAsync(o => o.Order_ID == orderId && o.Person_ID == personId, ct);
            if (order == null || (eventType == "order_completed" && order.Status.Code != "Completed")) throw new RecommendationValidationException("Замовлення ще не завершено або належить іншому користувачу.");
        }
        if (await HasEventAsync(personId, null, eventKey, eventType, ct))
        {
            var same = Convert.ToInt32(await Scalar("""
                SELECT COUNT(*) FROM dbo.UserInteraction WHERE EventKey=@key AND(CakeWeight_ID=@variant OR(CakeWeight_ID IS NULL AND @variant IS NULL))
                AND(CustomCake_ID=@custom OR(CustomCake_ID IS NULL AND @custom IS NULL)) AND(Order_ID=@order OR(Order_ID IS NULL AND @order IS NULL));
                """, ct, ("@key", eventKey), ("@variant", cakeWeightId), ("@custom", customCakeId), ("@order", orderId)), CultureInfo.InvariantCulture);
            if (same != 1) throw new RecommendationValidationException("Повторний ключ покупки містить інші дані.");
            if (owned != null) await owned.CommitAsync(ct);
            return false;
        }
        await Execute("""
            INSERT dbo.UserInteraction(EventKey,Person_ID,CakeWeight_ID,CustomCake_ID,Order_ID,EventType,OccurredAtUtc)
            VALUES(@key,@person,@variant,@custom,@order,@type,SYSUTCDATETIME());
            """, ct, ("@key", eventKey), ("@person", personId), ("@variant", cakeWeightId), ("@custom", customCakeId), ("@order", orderId), ("@type", eventType));
        if (owned != null) await owned.CommitAsync(ct);
        return true;
    }
}
