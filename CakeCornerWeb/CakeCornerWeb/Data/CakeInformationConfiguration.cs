using CakeCornerWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CakeCornerWeb.Data;

public static class CakeInformationConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        var info = model.Entity<CakeProductInformation>();
        info.ToTable("CakeProductInformation", table =>
        {
            foreach (var property in new[] { "ProteinGrams", "FatGrams", "SaturatedFatGrams", "CarbohydrateGrams", "SugarGrams", "SaltGrams", "FibreGrams" })
                table.HasCheckConstraint("CK_CakeInfo_" + property, $"[{property}] IS NULL OR [{property}] BETWEEN 0 AND 100");
            table.HasCheckConstraint("CK_CakeInfo_EnergyKcal", "[EnergyKcal] IS NULL OR [EnergyKcal] BETWEEN 0 AND 950");
            table.HasCheckConstraint("CK_CakeInfo_EnergyKj", "[EnergyKj] IS NULL OR [EnergyKj] BETWEEN 0 AND 4200");
            table.HasCheckConstraint("CK_CakeInfo_Saturated", "[SaturatedFatGrams] IS NULL OR [FatGrams] IS NULL OR [SaturatedFatGrams] <= [FatGrams]");
            table.HasCheckConstraint("CK_CakeInfo_Sugar", "[SugarGrams] IS NULL OR [CarbohydrateGrams] IS NULL OR [SugarGrams] <= [CarbohydrateGrams]");
            table.HasCheckConstraint("CK_CakeInfo_NutritionStatus", "[NutritionStatus] IN ('Unknown','Estimated','Verified')");
            table.HasCheckConstraint("CK_CakeInfo_AllergenStatus", "[AllergenStatus] IN ('Unknown','Estimated','Verified')");
        });
        info.HasKey(i => i.Cake_ID);
        info.HasOne(i => i.Cake).WithOne(c => c.ProductInformation).HasForeignKey<CakeProductInformation>(i => i.Cake_ID).OnDelete(DeleteBehavior.Cascade);
        foreach (var property in new[] { "ProteinGrams", "FatGrams", "SaturatedFatGrams", "CarbohydrateGrams", "SugarGrams", "SaltGrams", "FibreGrams" })
            info.Property<decimal?>(property).HasPrecision(5, 2);
        info.Property(i => i.EnergyKcal).HasPrecision(7, 2);
        info.Property(i => i.EnergyKj).HasPrecision(8, 2);
        info.Property(i => i.NutritionStatus).HasConversion<string>().HasMaxLength(12).IsUnicode(false);
        info.Property(i => i.AllergenStatus).HasConversion<string>().HasMaxLength(12).IsUnicode(false);
        info.Property(i => i.NutritionSource).HasMaxLength(500);
        info.Property(i => i.AllergenSource).HasMaxLength(500);
        info.Property(i => i.AllergenNotes).HasMaxLength(500);

        var allergen = model.Entity<Allergen>();
        allergen.ToTable("Allergen");
        allergen.HasKey(a => a.Allergen_ID);
        allergen.HasIndex(a => a.Code).IsUnique().HasDatabaseName("UQ_Allergen_Code");
        allergen.Property(a => a.Code).HasMaxLength(40).IsUnicode(false);
        allergen.Property(a => a.Name).HasMaxLength(100);
        allergen.Property(a => a.Description).HasMaxLength(500);

        var link = model.Entity<CakeAllergen>();
        link.ToTable("CakeAllergen", t => t.HasCheckConstraint("CK_CakeAllergen_Presence", "[Presence] IN ('Contains','MayContain')"));
        link.HasKey(a => new { a.Cake_ID, a.Allergen_ID });
        link.Property(a => a.Presence).HasConversion<string>().HasMaxLength(12).IsUnicode(false);
        link.HasOne(a => a.Cake).WithMany(c => c.Allergens).HasForeignKey(a => a.Cake_ID).OnDelete(DeleteBehavior.Cascade);
        link.HasOne(a => a.Allergen).WithMany().HasForeignKey(a => a.Allergen_ID).OnDelete(DeleteBehavior.Restrict);
    }
}
