using System.ComponentModel.DataAnnotations;

namespace CakeCornerWeb.Recommendations;

public sealed class RecommendationQuery
{
    public string Mode { get; set; } = "Ready"; // Ready, Custom, Ingredients
    public string DataMode { get; set; } = "Demo";
    public Dictionary<string, double> PreferredFlavors { get; set; } = [];
    public double? DesiredSweetness { get; set; }
    public Dictionary<string, int> LockedIngredients { get; set; } = [];
    public List<int> ExcludedIngredientIds { get; set; } = [];
    public List<string> ExcludedAllergens { get; set; } = [];
    public string? TargetType { get; set; }
    public decimal WeightKg { get; set; } = 2m;
    public int Quantity { get; set; } = 1;
    public string Shape { get; set; } = "Round";
    public decimal? ReadyMinWeightKg { get; set; }
    public decimal? ReadyMaxWeightKg { get; set; }
    public int TopK { get; set; } = 5;
}

public sealed class RecommendationMethod
{
    public string Version { get; set; } = "cakecorner-sugeno-2input-v1";
    public string[] FlavorTags { get; set; } = ["vanilla", "chocolate", "caramel", "coffee", "nut", "berry", "fruit", "citrus", "honey", "coconut", "cheesecake", "spice"];
    public string[] AllergenCodes { get; set; } = ["gluten", "milk", "egg", "peanut", "tree_nut", "soy", "sesame", "sulphites"];
    public string[] Slots { get; set; } = ["Biscuit", "Cream", "Filling", "Mousse"];
    public Dictionary<string, double> AggregationWeights { get; set; } = new() { ["Biscuit"] = .25, ["Cream"] = .25, ["Filling"] = .25, ["Mousse"] = .25 };
    public List<SugenoRule> Rules { get; set; } = [];
    public int PerSlotCandidates { get; set; } = 8;
    public int BeamWidth { get; set; } = 80;
    public int MaxCatalogIngredients { get; set; } = 2000;
    public int AvailabilityTtlHours { get; set; } = 24;
}
public sealed record SugenoRule(string Id, string Taste, string Sweetness, double Consequent);
public sealed record RuleActivation(string RuleId, double Weight, double Consequent);
public sealed class RecommendationFeatures
{
    public double? Sweetness { get; set; }
    public Dictionary<string, double?> Flavors { get; set; } = [];
    public bool SensoryVerified { get; set; }
    public string AllergenStatus { get; set; } = "Unknown";
    public HashSet<string> Contains { get; set; } = [];
    public HashSet<string> MayContain { get; set; } = [];
    public string Availability { get; set; } = "unknown";
    public bool AvailabilityVerified { get; set; }
    public DateTime? ObservedAtUtc { get; set; }
    public DateTime? ValidUntilUtc { get; set; }
}
public sealed class IngredientOption
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ImagePath { get; set; }
    public decimal Rate { get; set; }
    public bool IsActive { get; set; }
    public RecommendationFeatures? Features { get; set; }
}
public sealed class ReadyOption
{
    public int CakeId { get; set; }
    public int CakeWeightId { get; set; }
    public string Name { get; set; } = "";
    public string? ImagePath { get; set; }
    public decimal WeightKg { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsActive { get; set; }
    public RecommendationFeatures? Features { get; set; }
}
public sealed record CompatibilityPair(int IngredientAId, int IngredientBId, string Decision, bool Verified, string? Reason);
public sealed class RecommendationSnapshot
{
    public string AnnotationVersion { get; set; } = "";
    public string MethodVersion { get; set; } = "";
    public int AnnotationVersionId { get; set; }
    public int MethodVersionId { get; set; }
    public bool AnnotationPublished { get; set; }
    public bool MethodPublished { get; set; }
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public List<IngredientOption> Ingredients { get; set; } = [];
    public List<ReadyOption> Ready { get; set; } = [];
    public List<CompatibilityPair> Compatibility { get; set; } = [];
}
public sealed class RecommendationItemDto
{
    public long? LogItemId { get; set; }
    public string Key { get; set; } = "";
    public string Kind { get; set; } = ""; // Ready, Custom, Ingredient
    public int? CakeId { get; set; }
    public int? CakeWeightId { get; set; }
    public int? IngredientId { get; set; }
    public string? IngredientType { get; set; }
    public string Name { get; set; } = "";
    public string? ImagePath { get; set; }
    public Dictionary<string, int> Ingredients { get; set; } = [];
    public Dictionary<string, string> IngredientNames { get; set; } = [];
    public decimal? UnitPrice { get; set; }
    public decimal WeightKg { get; set; }
    public int Quantity { get; set; }
    public string Shape { get; set; } = "Round";
    public double? TasteFit { get; set; }
    public double? SweetnessFit { get; set; }
    public double? Score { get; set; }
    public string Strategy { get; set; } = "";
    public List<RuleActivation> Activations { get; set; } = [];
    public List<string> Reasons { get; set; } = [];
    public RecommendationFeatures? Features { get; set; }
}
public sealed class RecommendationResult
{
    public long? RequestId { get; set; }
    public string Status { get; set; } = "ok";
    public string DataMode { get; set; } = "Demo";
    public string MethodVersion { get; set; } = "";
    public string AnnotationVersion { get; set; } = "";
    public List<string> Warnings { get; set; } = [];
    public Dictionary<string, int> Rejections { get; set; } = [];
    public List<RecommendationItemDto> Items { get; set; } = [];
    public int ExaminedCandidates { get; set; }
}
public sealed class RecommendationPreferences
{
    public Dictionary<string, double> PreferredFlavors { get; set; } = [];
    public double? DesiredSweetness { get; set; }
    public List<string> ExcludedAllergens { get; set; } = [];
}
public sealed class RecommendationEventRequest
{
    public Guid EventKey { get; set; }
    public long RequestId { get; set; }
    public string CandidateKey { get; set; } = "";
    public string EventType { get; set; } = "impression";
    public int? Position { get; set; }
    public int? Rating { get; set; }
}
public sealed class RecommendationAddRequest
{
    public long RequestId { get; set; }
    public string CandidateKey { get; set; } = "";
    public Guid EventKey { get; set; }
    public string? Inscription { get; set; }
    public string? DesignNotes { get; set; }
    public int? GuestCount { get; set; }
}
public sealed class RecommendationValidationException(string message) : Exception(message);
public sealed class RecommendationUnavailableException(string message) : Exception(message);
