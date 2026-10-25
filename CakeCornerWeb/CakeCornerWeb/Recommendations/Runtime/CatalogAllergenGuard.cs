using CakeCornerWeb.Models.Entities;

namespace CakeCornerWeb.Recommendations.Runtime;

/// <summary>Current catalogue data can veto old annotations, but cannot certify or publish them.</summary>
public static class CatalogAllergenGuard
{
    private static readonly HashSet<string> Nuts = ["almond", "hazelnut", "walnut", "pistachio", "cashew", "pecan", "brazil_nut", "macadamia"];

    public static RecommendationFeatures? Apply(RecommendationFeatures? features, Cake cake)
    {
        if (features == null) return null;
        if (cake.ProductInformation != null && cake.ProductInformation.AllergenStatus != ProductDataStatus.Verified)
            features.AllergenStatus = "Unknown";
        foreach (var allergen in cake.Allergens)
        {
            var target = allergen.Presence == AllergenPresence.Contains ? features.Contains : features.MayContain;
            target.Add(allergen.Allergen.Code);
            if (Nuts.Contains(allergen.Allergen.Code)) target.Add("tree_nut");
        }
        return features;
    }
}
