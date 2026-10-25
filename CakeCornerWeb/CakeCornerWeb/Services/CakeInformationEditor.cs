using System.ComponentModel.DataAnnotations;
using CakeCornerWeb.Data;
using CakeCornerWeb.Models;
using CakeCornerWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CakeCornerWeb.Services;

public static class CakeInformationEditor
{
    public static async Task ValidateAsync(ApplicationDbContext context, CakeInformationInput? input, CancellationToken ct)
    {
        if (input == null) throw new CatalogRuleException("Перевірте додаткову інформацію про торт.");
        Validator.ValidateObject(input, new ValidationContext(input), true);
        foreach (var allergen in input.Allergens) Validator.ValidateObject(allergen, new ValidationContext(allergen), true);
        var ids = input.Allergens.Select(a => a.AllergenId).ToArray();
        if (await context.Allergens.CountAsync(a => ids.Contains(a.Allergen_ID), ct) != ids.Length)
            throw new CatalogRuleException("Список алергенів змінився. Оновіть сторінку та повторіть дію.");
    }

    public static void Apply(Cake cake, CakeInformationInput input)
    {
        var info = cake.ProductInformation ??= new CakeProductInformation { Cake = cake };
        info.ProteinGrams = input.ProteinGrams;
        info.FatGrams = input.FatGrams;
        info.SaturatedFatGrams = input.SaturatedFatGrams;
        info.CarbohydrateGrams = input.CarbohydrateGrams;
        info.SugarGrams = input.SugarGrams;
        info.SaltGrams = input.SaltGrams;
        info.FibreGrams = input.FibreGrams;
        info.EnergyKcal = input.EnergyKcal;
        info.EnergyKj = input.EnergyKj;
        info.NutritionStatus = input.NutritionStatus;
        info.AllergenStatus = input.AllergenStatus;
        info.NutritionSource = Clean(input.NutritionSource);
        info.AllergenSource = Clean(input.AllergenSource);
        info.AllergenNotes = Clean(input.AllergenNotes);
        var selected = input.Allergens.Where(a => a.Presence != AllergenPresence.None).ToDictionary(a => a.AllergenId);
        // Update composite-key rows in place; removing/re-adding the same key breaks EF tracking.
        foreach (var existing in cake.Allergens.ToArray())
        {
            if (selected.Remove(existing.Allergen_ID, out var value)) existing.Presence = value.Presence;
            else cake.Allergens.Remove(existing);
        }
        foreach (var value in selected.Values)
            cake.Allergens.Add(new CakeAllergen { Cake = cake, Allergen_ID = value.AllergenId, Presence = value.Presence });
    }

    public static CakeInformationInput FromCake(Cake cake)
    {
        var input = CakeAdditionalInformationViewModel.FromCake(cake).Nutrition;
        input.NutritionSource = cake.ProductInformation?.NutritionSource;
        input.AllergenSource = cake.ProductInformation?.AllergenSource;
        input.Allergens = cake.Allergens.Select(a => new CakeAllergenInput { AllergenId = a.Allergen_ID, Presence = a.Presence }).ToList();
        return input;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
