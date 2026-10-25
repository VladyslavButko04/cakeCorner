using System.Globalization;
using System.Text.RegularExpressions;

namespace CakeCornerWeb.Recommendations.Core;

/// <summary>Pure content scoring, knowledge gates and bounded four-component search.</summary>
public sealed class RecommendationEngine
{
    private static readonly string[] RequiredSlots = ["Biscuit", "Cream", "Filling", "Mousse"];
    private static readonly string[] Shapes = ["Round", "Square", "Heart"];
    private const decimal MaxMoney = 99999999.99m;
    private readonly RecommendationMethod method;
    private readonly SugenoInference fuzzy;

    public RecommendationEngine(RecommendationMethod method)
    {
        ValidateMethod(method);
        this.method = method;
        fuzzy = new SugenoInference(OrderedConsequents(method));
    }

    public static void ValidateMethod(RecommendationMethod method)
    {
        if (method == null || string.IsNullOrWhiteSpace(method.Version) || method.Version.Length > 80)
            throw new RecommendationValidationException("Потрібна коректна версія методу.");
        if (method.Slots == null || !method.Slots.SequenceEqual(RequiredSlots))
            throw new RecommendationValidationException("Метод має містити чотири впорядковані типи компонентів.");
        if (method.FlavorTags == null || method.FlavorTags.Length is < 1 or > 100 ||
            method.FlavorTags.Distinct(StringComparer.Ordinal).Count() != method.FlavorTags.Length ||
            method.FlavorTags.Any(t => t == null || !Regex.IsMatch(t, "^[a-z][a-z0-9_]{0,39}$")))
            throw new RecommendationValidationException("Некоректний словник смаків.");
        if (method.AllergenCodes == null || method.AllergenCodes.Length is < 1 or > 100 ||
            method.AllergenCodes.Distinct(StringComparer.Ordinal).Count() != method.AllergenCodes.Length ||
            method.AllergenCodes.Any(t => t == null || !Regex.IsMatch(t, "^[a-z][a-z0-9_]{0,39}$")))
            throw new RecommendationValidationException("Некоректний словник алергенів.");
        if (method.AggregationWeights == null || !method.AggregationWeights.Keys.Order().SequenceEqual(RequiredSlots.Order()) ||
            method.AggregationWeights.Values.Any(w => !double.IsFinite(w) || w <= 0 || w > 1) ||
            Math.Abs(method.AggregationWeights.Values.Sum() - 1) > 1e-9)
            throw new RecommendationValidationException("Чотири додатні ваги агрегації мають давати суму 1.");
        if (method.PerSlotCandidates is < 1 or > 100 || method.BeamWidth is < 1 or > 2000 ||
            method.MaxCatalogIngredients is < 4 or > 50000 || method.AvailabilityTtlHours is < 1 or > 8760)
            throw new RecommendationValidationException("Некоректні межі пошуку або строк актуальності наявності.");
        if (method.Rules == null || method.Rules.Count != 9 || method.Rules.Any(r => r == null ||
                string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 30) ||
            method.Rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != 9 ||
            method.Rules.Any(r => !SugenoInference.Levels.Contains(r.Taste) || !SugenoInference.Levels.Contains(r.Sweetness)) ||
            method.Rules.Select(r => (r.Taste, r.Sweetness)).Distinct().Count() != 9)
            throw new RecommendationValidationException("Потрібні дев'ять унікальних правил для двох входів.");
        try { _ = new SugenoInference(OrderedConsequents(method)); }
        catch (ArgumentException ex) { throw new RecommendationValidationException(ex.Message); }
    }

    private static IEnumerable<double> OrderedConsequents(RecommendationMethod method) =>
        from taste in SugenoInference.Levels
        from sweetness in SugenoInference.Levels
        select method.Rules.Single(r => r.Taste == taste && r.Sweetness == sweetness).Consequent;

    public void ValidateRequest(RecommendationQuery query)
    {
        if (query == null || query.Mode is not ("Ready" or "Custom" or "Ingredients") || query.DataMode is not ("Demo" or "Verified"))
            throw new RecommendationValidationException("Оберіть коректний режим рекомендацій.");
        if (query.PreferredFlavors == null || query.LockedIngredients == null || query.ExcludedIngredientIds == null || query.ExcludedAllergens == null)
            throw new RecommendationValidationException("Списки побажань не можуть бути null.");
        if (query.PreferredFlavors.Count > method.FlavorTags.Length || query.PreferredFlavors.Any(p =>
                !method.FlavorTags.Contains(p.Key, StringComparer.Ordinal) || !double.IsFinite(p.Value) || p.Value is < 0 or > 5))
            throw new RecommendationValidationException("Оберіть відомі смаки та ваги від 0 до 5.");
        if (query.DesiredSweetness is double sweetness && (!double.IsFinite(sweetness) || sweetness is < 0 or > 5))
            throw new RecommendationValidationException("Бажана солодкість має бути від 0 до 5.");
        if (query.LockedIngredients.Count > 4 || query.LockedIngredients.Any(p => !RequiredSlots.Contains(p.Key) || p.Value <= 0) ||
            query.LockedIngredients.Values.Distinct().Count() != query.LockedIngredients.Count)
            throw new RecommendationValidationException("Закріпіть не більше одного інгредієнта кожного типу.");
        if (query.ExcludedIngredientIds.Count > method.MaxCatalogIngredients || query.ExcludedIngredientIds.Any(id => id <= 0) ||
            query.LockedIngredients.Values.Intersect(query.ExcludedIngredientIds).Any())
            throw new RecommendationValidationException("Закріплені та виключені інгредієнти мають бути узгоджені.");
        if (query.ExcludedAllergens.Count > method.AllergenCodes.Length || query.ExcludedAllergens.Any(a => !method.AllergenCodes.Contains(a, StringComparer.Ordinal)))
            throw new RecommendationValidationException("Невідоме обмеження щодо алергенів потребує уточнення.");
        if (query.TopK is < 1 or > 10 || query.Quantity is < 1 or > 99 || query.WeightKg is < .5m or > 10m ||
            decimal.Round(query.WeightKg, 1) != query.WeightKg || !Shapes.Contains(query.Shape))
            throw new RecommendationValidationException("Перевірте вагу, кількість, форму та кількість рекомендацій.");
        if (query.ReadyMinWeightKg is decimal min && (min <= 0 || min > 100) ||
            query.ReadyMaxWeightKg is decimal max && (max <= 0 || max > 100) ||
            query.ReadyMinWeightKg is decimal lo && query.ReadyMaxWeightKg is decimal hi && lo > hi)
            throw new RecommendationValidationException("Перевірте межі ваги готових тортів.");
        if (query.Mode == "Ingredients" && (query.TargetType == null || !RequiredSlots.Contains(query.TargetType)))
            throw new RecommendationValidationException("Оберіть тип інгредієнта для підказки.");
    }

    public RecommendationResult Recommend(RecommendationQuery query, RecommendationSnapshot snapshot) => RecommendCore(query, snapshot, false);

    /// <summary>Small-catalog research reference. Not exposed by MVC. Same gates, no beam pruning.</summary>
    public RecommendationResult RecommendExhaustive(RecommendationQuery query, RecommendationSnapshot snapshot)
    {
        if (query.Mode != "Custom") throw new RecommendationValidationException("Повний перебір призначений для дослідження кастомних конфігурацій.");
        return RecommendCore(query, snapshot, true);
    }

    private RecommendationResult RecommendCore(RecommendationQuery query, RecommendationSnapshot snapshot, bool exhaustive)
    {
        ValidateRequest(query);
        if (snapshot == null || snapshot.Ingredients == null || snapshot.Ready == null || snapshot.Compatibility == null ||
            snapshot.MethodVersion != method.Version || string.IsNullOrWhiteSpace(snapshot.AnnotationVersion))
            throw new RecommendationUnavailableException("Версії каталогу та методу не узгоджені.");
        if (snapshot.Ingredients.Count > method.MaxCatalogIngredients)
            throw new RecommendationUnavailableException("Каталог перевищує налаштовану межу пошуку.");
        var result = new RecommendationResult
        {
            DataMode = query.DataMode, MethodVersion = snapshot.MethodVersion, AnnotationVersion = snapshot.AnnotationVersion
        };
        if (query.DataMode == "Demo") result.Warnings.Add("Демонстраційні оцінки: смак і сумісність ще не підтверджені експертами.");
        if (query.DataMode == "Verified" && (!snapshot.AnnotationPublished || !snapshot.MethodPublished))
        {
            Reject(result, "unpublished_versions");
            result.Status = "insufficient_metadata";
            result.Warnings.Add("Для перевіреного режиму потрібні опубліковані версії методу та розмітки.");
            return result;
        }
        var ingredients = snapshot.Ingredients.GroupBy(i => i.Id).Where(g => g.Count() == 1).Select(g => g.Single()).ToDictionary(i => i.Id);
        foreach (var (type, id) in query.LockedIngredients)
            if (!ingredients.TryGetValue(id, out var ingredient) || ingredient.Type != type)
                throw new RecommendationValidationException("Один із закріплених інгредієнтів не існує або має інший тип.");
        var pairs = snapshot.Compatibility.GroupBy(p => PairKey(p.IngredientAId, p.IngredientBId))
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.Single() : null);

        List<RecommendationItemDto> candidates = query.Mode switch
        {
            "Ready" => ReadyCandidates(query, snapshot, result),
            "Ingredients" => IngredientCandidates(query, snapshot, ingredients, pairs, result),
            _ => CustomCandidates(query, snapshot, ingredients, pairs, result, exhaustive)
        };
        var sorted = Rank(candidates);
        if (query.Mode == "Ready") sorted = Rank(sorted.GroupBy(i => i.CakeId).Select(g => g.First()));
        result.Items = sorted.Take(query.TopK).ToList();
        if (result.Items.Count == 0)
        {
            result.Status = result.Rejections.Keys.Any(k => k.Contains("unknown") || k.Contains("missing") || k.Contains("unverified") ||
                k.Contains("expired") || k.Contains("metadata")) ? "insufficient_metadata" : "no_eligible_candidates";
            result.Warnings.Add(result.Status == "insufficient_metadata"
                ? "Даних для надійного підбору недостатньо. Обмеження не послаблювались."
                : "За поточними умовами немає доступних варіантів. Змініть побажання або вибір.");
        }
        return result;
    }

    private List<RecommendationItemDto> ReadyCandidates(RecommendationQuery query, RecommendationSnapshot snapshot, RecommendationResult result)
    {
        List<RecommendationItemDto> candidates = [];
        if (query.LockedIngredients.Count > 0 || query.ExcludedIngredientIds.Count > 0)
        {
            Reject(result, "ready_composition_unknown");
            return candidates;
        }
        foreach (var ready in snapshot.Ready.GroupBy(i => i.CakeWeightId).Where(g => g.Count() == 1).Select(g => g.Single()))
        {
            result.ExaminedCandidates++;
            if (!ready.IsActive) { Reject(result, "inactive"); continue; }
            if (ready.CakeId <= 0 || ready.CakeWeightId <= 0 || ready.WeightKg <= 0 || string.IsNullOrWhiteSpace(ready.Name))
            { Reject(result, "invalid_catalog"); continue; }
            if (ready.UnitPrice <= 0 || ready.UnitPrice > MaxMoney || ready.UnitPrice * query.Quantity > MaxMoney)
            { Reject(result, "invalid_price"); continue; }
            if ((query.ReadyMinWeightKg is decimal min && ready.WeightKg < min) ||
                (query.ReadyMaxWeightKg is decimal max && ready.WeightKg > max))
            { Reject(result, "ready_weight"); continue; }
            var reason = FeaturesRejection(query, ready.Features, snapshot.CapturedAtUtc);
            if (reason != null) { Reject(result, reason); continue; }
            var item = new RecommendationItemDto
            {
                Key = $"ready:{ready.CakeWeightId}", Kind = "Ready", CakeId = ready.CakeId, CakeWeightId = ready.CakeWeightId,
                Name = ready.Name, ImagePath = ready.ImagePath, UnitPrice = ready.UnitPrice, WeightKg = ready.WeightKg,
                Quantity = query.Quantity, Shape = query.Shape, Features = ready.Features
            };
            Score(item, query, ready.Features!);
            candidates.Add(item);
        }
        return candidates;
    }

    private List<RecommendationItemDto> IngredientCandidates(RecommendationQuery query, RecommendationSnapshot snapshot,
        Dictionary<int, IngredientOption> ingredients, Dictionary<(int, int), CompatibilityPair?> pairs, RecommendationResult result)
    {
        List<RecommendationItemDto> candidates = [];
        var locked = query.LockedIngredients.Values.Select(id => ingredients[id]).ToList();
        foreach (var i in locked)
        {
            var reason = IngredientRejection(query, i, snapshot.CapturedAtUtc);
            if (reason != null) { Reject(result, reason); return candidates; }
        }
        if (!AllPairsAllowed(locked, query, pairs, result)) return candidates;
        foreach (var option in ingredients.Values.Where(i => i.Type == query.TargetType))
        {
            result.ExaminedCandidates++;
            if (query.LockedIngredients.TryGetValue(option.Type, out var lockedId) && option.Id != lockedId) continue;
            var reason = IngredientRejection(query, option, snapshot.CapturedAtUtc);
            if (reason != null) { Reject(result, reason); continue; }
            var selected = locked.Where(i => i.Type != option.Type).Append(option).ToList();
            if (!AllPairsAllowed(selected, query, pairs, result)) continue;
            var features = Aggregate(selected);
            var item = CreateCustomItem(selected, query, features);
            item.Key = $"ingredient:{option.Type}:{option.Id}";
            item.Kind = "Ingredient"; item.IngredientId = option.Id; item.IngredientType = option.Type;
            item.Name = option.Name; item.ImagePath = option.ImagePath; item.UnitPrice = null;
            item.Reasons.Add($"Тариф компонента: {option.Rate.ToString("0.00", CultureInfo.InvariantCulture)} грн/кг.");
            item.Reasons.Add("Оцінка для вибраної частини складу; після завершення торта вона може змінитися.");
            Score(item, query, features);
            candidates.Add(item);
        }
        return candidates;
    }

    private List<RecommendationItemDto> CustomCandidates(RecommendationQuery query, RecommendationSnapshot snapshot,
        Dictionary<int, IngredientOption> ingredients, Dictionary<(int, int), CompatibilityPair?> pairs,
        RecommendationResult result, bool exhaustive)
    {
        var locked = query.LockedIngredients.Values.Select(id => ingredients[id]).ToList();
        foreach (var option in locked)
        {
            var reason = IngredientRejection(query, option, snapshot.CapturedAtUtc);
            if (reason != null) { Reject(result, reason); return []; }
        }
        if (!AllPairsAllowed(locked, query, pairs, result)) return [];
        var pools = new Dictionary<string, List<IngredientOption>>();
        foreach (var slot in method.Slots)
        {
            List<IngredientOption> pool = [];
            foreach (var option in ingredients.Values.Where(i => i.Type == slot))
            {
                if (query.LockedIngredients.TryGetValue(slot, out var id) && option.Id != id) continue;
                var reason = IngredientRejection(query, option, snapshot.CapturedAtUtc);
                if (reason != null) { Reject(result, reason); continue; }
                if (!AllPairsAllowed(locked.Where(i => i.Type != slot).Append(option).ToList(), query, pairs, result)) continue;
                pool.Add(option);
            }
            pools[slot] = pool.OrderByDescending(i => Fit(i.Features!, query).Score ?? 0)
                .ThenBy(i => i.Id.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal)
                .Take(exhaustive ? int.MaxValue : method.PerSlotCandidates).ToList();
            if (pool.Count == 0) { Reject(result, "no_available_" + slot); return []; }
        }
        if (exhaustive && pools.Values.Aggregate(1L, (n, p) => Math.Min(100001, n * p.Count)) > 100000)
            throw new RecommendationValidationException("Повний перебір обмежений 100000 малими дослідницькими комбінаціями.");
        List<List<IngredientOption>> beam = [[]];
        foreach (var slot in method.Slots)
        {
            List<(List<IngredientOption> Selected, double Priority, string Key)> extensions = [];
            foreach (var partial in beam)
            foreach (var option in pools[slot])
            {
                result.ExaminedCandidates++;
                var selected = partial.Append(option).ToList();
                if (!AllPairsAllowed(selected, query, pairs, result)) continue;
                extensions.Add((selected, Fit(Aggregate(selected), query).Score ?? 0, CompositionKey(selected)));
            }
            beam = extensions.OrderByDescending(e => e.Priority).ThenBy(e => e.Key, StringComparer.Ordinal)
                .Take(exhaustive ? int.MaxValue : method.BeamWidth).Select(e => e.Selected).ToList();
            if (beam.Count == 0) return [];
        }
        List<RecommendationItemDto> candidates = [];
        foreach (var selected in beam)
        {
            var price = decimal.Round(selected.Sum(i => i.Rate) * query.WeightKg, 2, MidpointRounding.AwayFromZero);
            if (price <= 0 || price > MaxMoney || price * query.Quantity > MaxMoney)
            { Reject(result, "invalid_price"); continue; }
            var features = Aggregate(selected);
            var item = CreateCustomItem(selected, query, features);
            item.UnitPrice = price;
            item.Key = $"custom:{CompositionKey(selected)}:{query.WeightKg.ToString("0.0", CultureInfo.InvariantCulture)}:{query.Shape}";
            Score(item, query, features);
            candidates.Add(item);
        }
        return candidates;
    }

    private RecommendationItemDto CreateCustomItem(List<IngredientOption> selected, RecommendationQuery query, RecommendationFeatures features) => new()
    {
        Kind = "Custom", Name = "Поєднання для вашого торта", Quantity = query.Quantity, WeightKg = query.WeightKg,
        Shape = query.Shape, Features = features,
        Ingredients = selected.ToDictionary(i => i.Type, i => i.Id),
        IngredientNames = selected.ToDictionary(i => i.Type, i => i.Name)
    };

    private string CompositionKey(IEnumerable<IngredientOption> selected) => string.Join(":", method.Slots
        .Where(t => selected.Any(i => i.Type == t)).Select(t => selected.Single(i => i.Type == t).Id.ToString(CultureInfo.InvariantCulture)));

    private string? IngredientRejection(RecommendationQuery query, IngredientOption option, DateTime now)
    {
        if (!option.IsActive) return "inactive";
        if (option.Id <= 0 || !RequiredSlots.Contains(option.Type) || string.IsNullOrWhiteSpace(option.Name)) return "invalid_catalog";
        if (option.Rate < 0 || option.Rate > MaxMoney) return "invalid_price";
        if (query.ExcludedIngredientIds.Contains(option.Id)) return "ingredient_excluded";
        return FeaturesRejection(query, option.Features, now);
    }

    private string? FeaturesRejection(RecommendationQuery query, RecommendationFeatures? features, DateTime now)
    {
        if (features == null || features.Flavors == null || features.Contains == null || features.MayContain == null) return "missing_metadata";
        if (!string.Equals(features.Availability, "available", StringComparison.OrdinalIgnoreCase))
            return string.Equals(features.Availability, "unavailable", StringComparison.OrdinalIgnoreCase) ? "unavailable" : "availability_unknown";
        // An explicitly expired latest observation is never replaced by an older available one.
        if (features.ObservedAtUtc > now || features.ValidUntilUtc is DateTime until && until <= now)
            return "availability_expired";
        if (query.DataMode == "Verified")
        {
            if (!features.SensoryVerified) return "sensory_unverified";
            if (!features.AvailabilityVerified || features.ObservedAtUtc == null) return "availability_unverified";
            if (features.ObservedAtUtc > now || (features.ValidUntilUtc is DateTime expiry ? expiry <= now :
                    features.ObservedAtUtc.Value.AddHours(method.AvailabilityTtlHours) <= now)) return "availability_expired";
        }
        if (features.Sweetness is double sweetness && (!double.IsFinite(sweetness) || sweetness is < 0 or > 5)) return "invalid_sensory_metadata";
        if (features.Flavors.Values.Any(v => v is double intensity && (!double.IsFinite(intensity) || intensity is < 0 or > 1)))
            return "invalid_sensory_metadata";
        if (query.DesiredSweetness != null && features.Sweetness == null) return "sweetness_unknown";
        foreach (var (tag, weight) in query.PreferredFlavors.Where(p => p.Value > 0))
            if (!features.Flavors.TryGetValue(tag, out var value) || value == null) return "taste_unknown";
            else if (!double.IsFinite(value.Value) || value is < 0 or > 1) return "invalid_sensory_metadata";
        if (query.ExcludedAllergens.Count > 0)
        {
            if (!string.Equals(features.AllergenStatus, "Verified", StringComparison.OrdinalIgnoreCase)) return "allergen_unknown";
            if (query.ExcludedAllergens.Any(a => features.Contains.Contains(a) || features.MayContain.Contains(a))) return "allergen_conflict";
        }
        return null;
    }

    private bool AllPairsAllowed(List<IngredientOption> selected, RecommendationQuery query,
        Dictionary<(int, int), CompatibilityPair?> pairs, RecommendationResult result)
    {
        for (var a = 0; a < selected.Count; a++)
        for (var b = a + 1; b < selected.Count; b++)
        {
            if (!pairs.TryGetValue(PairKey(selected[a].Id, selected[b].Id), out var pair) || pair == null)
            { Reject(result, "compatibility_unknown"); return false; }
            if (string.Equals(pair.Decision, "forbidden", StringComparison.OrdinalIgnoreCase))
            { Reject(result, "compatibility_forbidden"); return false; }
            if (!string.Equals(pair.Decision, "allowed", StringComparison.OrdinalIgnoreCase))
            { Reject(result, "compatibility_unknown"); return false; }
            if (query.DataMode == "Verified" && !pair.Verified)
            { Reject(result, "compatibility_unverified"); return false; }
        }
        return true;
    }

    private RecommendationFeatures Aggregate(List<IngredientOption> selected)
    {
        // Inputs have already passed range checks; clamps only remove floating-point roundoff in derived means.
        var totalWeight = selected.Sum(i => method.AggregationWeights[i.Type]);
        double? sweetness = selected.All(i => i.Features!.Sweetness != null)
            ? Math.Clamp(selected.Sum(i => method.AggregationWeights[i.Type] * i.Features!.Sweetness!.Value) / totalWeight, 0, 5) : null;
        Dictionary<string, double?> flavors = [];
        foreach (var tag in method.FlavorTags)
            flavors[tag] = selected.All(i => i.Features!.Flavors.TryGetValue(tag, out var v) && v != null)
                ? Math.Clamp(selected.Sum(i => method.AggregationWeights[i.Type] * i.Features!.Flavors[tag]!.Value) / totalWeight, 0, 1) : null;
        return new RecommendationFeatures
        {
            Sweetness = sweetness, Flavors = flavors, Availability = "available",
            SensoryVerified = selected.All(i => i.Features!.SensoryVerified),
            AvailabilityVerified = selected.All(i => i.Features!.AvailabilityVerified),
            ObservedAtUtc = selected.All(i => i.Features!.ObservedAtUtc != null) ? selected.Min(i => i.Features!.ObservedAtUtc) : null,
            ValidUntilUtc = selected.All(i => i.Features!.ValidUntilUtc != null) ? selected.Min(i => i.Features!.ValidUntilUtc) : null,
            AllergenStatus = selected.All(i => string.Equals(i.Features!.AllergenStatus, "Verified", StringComparison.OrdinalIgnoreCase)) ? "Verified" : "Unknown",
            Contains = selected.SelectMany(i => i.Features!.Contains).ToHashSet(StringComparer.Ordinal),
            MayContain = selected.SelectMany(i => i.Features!.MayContain).ToHashSet(StringComparer.Ordinal)
        };
    }

    private FitResult Fit(RecommendationFeatures features, RecommendationQuery query)
    {
        var active = query.PreferredFlavors.Where(p => p.Value > 0).ToList();
        double? taste = active.Count == 0 ? null : Math.Clamp(active.Sum(p => p.Value * features.Flavors[p.Key]!.Value) / active.Sum(p => p.Value), 0, 1);
        double? sweetness = query.DesiredSweetness is double target ? Math.Clamp(1 - Math.Abs(features.Sweetness!.Value - target) / 5, 0, 1) : null;
        if (taste != null && sweetness != null)
        {
            var evaluated = fuzzy.Evaluate(taste.Value, sweetness.Value);
            // Rule IDs come from the versioned method, not their default labels.
            var activations = evaluated.Rules.Select(a => new RuleActivation(method.Rules.Single(r =>
                r.Taste == a.TasteLevel && r.Sweetness == a.SweetnessLevel).Id, a.Strength, a.Consequent)).ToList();
            return new(taste, sweetness, evaluated.Score, "hybrid-fuzzy", activations);
        }
        return new(taste, sweetness, taste ?? sweetness, taste != null || sweetness != null ? "partial-profile" : "stable-fallback", []);
    }

    private void Score(RecommendationItemDto item, RecommendationQuery query, RecommendationFeatures features)
    {
        var fit = Fit(features, query);
        item.TasteFit = fit.Taste; item.SweetnessFit = fit.Sweetness; item.Score = fit.Score;
        item.Strategy = fit.Strategy; item.Activations = fit.Rules;
        if (fit.Taste != null)
        {
            var strong = query.PreferredFlavors.Where(p => p.Value > 0 && features.Flavors[p.Key] >= .5)
                .OrderByDescending(p => p.Value * features.Flavors[p.Key]).Take(2).Select(p => FlavorLabel(p.Key)).ToArray();
            item.Reasons.Add(strong.Length > 0 ? "Враховано виражені смаки: " + string.Join(", ", strong) + "." : "Смакові ознаки порівняно з вашими побажаннями.");
        }
        if (fit.Sweetness != null)
            item.Reasons.Add($"Солодкість за розміткою: {features.Sweetness!.Value.ToString("0.##", CultureInfo.InvariantCulture)} з 5; бажана: {query.DesiredSweetness!.Value.ToString("0.##", CultureInfo.InvariantCulture)}.");
        if (fit.Score == null) item.Reasons.Add("Варіант без персонального ранжування. Задайте смак або солодкість.");
        if (query.ExcludedAllergens.Count > 0) item.Reasons.Add("У перевіреній розмітці немає зазначених виключених алергенів.");
    }

    private static string FlavorLabel(string tag) => tag switch
    {
        "vanilla" => "ваніль", "chocolate" => "шоколад", "caramel" => "карамель", "coffee" => "кава", "nut" => "горіхи",
        "berry" => "ягоди", "fruit" => "фрукти", "citrus" => "цитрус", "honey" => "мед", "coconut" => "кокос",
        "cheesecake" => "сирно-вершковий", "spice" => "прянощі", _ => tag
    };

    private static (int, int) PairKey(int a, int b) => a < b ? (a, b) : (b, a);
    private static IOrderedEnumerable<RecommendationItemDto> Rank(IEnumerable<RecommendationItemDto> candidates) => candidates
        .OrderByDescending(i => i.Score ?? 0).ThenBy(i => i.Key, StringComparer.Ordinal);
    private static void Reject(RecommendationResult result, string reason) => result.Rejections[reason] = result.Rejections.GetValueOrDefault(reason) + 1;
    private sealed record FitResult(double? Taste, double? Sweetness, double? Score, string Strategy, List<RuleActivation> Rules);
}
