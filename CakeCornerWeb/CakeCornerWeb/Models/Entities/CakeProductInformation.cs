namespace CakeCornerWeb.Models.Entities;

public enum ProductDataStatus { Unknown, Estimated, Verified }
public enum AllergenPresence { None, Contains, MayContain }

/// <summary>All quantities describe 100 g of the finished cake, independently of sales weight.</summary>
public sealed class CakeProductInformation
{
    public int Cake_ID { get; set; }
    public Cake Cake { get; set; } = null!;
    public decimal? ProteinGrams { get; set; }
    public decimal? FatGrams { get; set; }
    public decimal? SaturatedFatGrams { get; set; }
    public decimal? CarbohydrateGrams { get; set; }
    public decimal? SugarGrams { get; set; }
    public decimal? SaltGrams { get; set; }
    public decimal? FibreGrams { get; set; }
    public decimal? EnergyKcal { get; set; }
    public decimal? EnergyKj { get; set; }
    public ProductDataStatus NutritionStatus { get; set; }
    public ProductDataStatus AllergenStatus { get; set; }
    public string? NutritionSource { get; set; }
    public string? AllergenSource { get; set; }
    public string? AllergenNotes { get; set; }
}

/// <summary>Shares dbo.Allergen with the versioned recommendation annotation schema.</summary>
public sealed class Allergen
{
    public int Allergen_ID { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

public sealed class CakeAllergen
{
    public int Cake_ID { get; set; }
    public Cake Cake { get; set; } = null!;
    public int Allergen_ID { get; set; }
    public Allergen Allergen { get; set; } = null!;
    public AllergenPresence Presence { get; set; }
}
