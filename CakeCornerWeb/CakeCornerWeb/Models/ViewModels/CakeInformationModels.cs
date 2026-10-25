using System.ComponentModel.DataAnnotations;
using CakeCornerWeb.Models.Entities;

namespace CakeCornerWeb.Models;

public sealed class CakeAllergenInput
{
    [Range(1, int.MaxValue)] public int AllergenId { get; set; }
    [EnumDataType(typeof(AllergenPresence))] public AllergenPresence Presence { get; set; }
}

public sealed class CakeInformationInput : IValidatableObject
{
    [Range(typeof(decimal), "0", "100"), Display(Name = "Білки, г")] public decimal? ProteinGrams { get; set; }
    [Range(typeof(decimal), "0", "100"), Display(Name = "Жири, г")] public decimal? FatGrams { get; set; }
    [Range(typeof(decimal), "0", "100"), Display(Name = "З них насичені жири, г")] public decimal? SaturatedFatGrams { get; set; }
    [Range(typeof(decimal), "0", "100"), Display(Name = "Вуглеводи, г")] public decimal? CarbohydrateGrams { get; set; }
    [Range(typeof(decimal), "0", "100"), Display(Name = "З них цукри, г")] public decimal? SugarGrams { get; set; }
    [Range(typeof(decimal), "0", "100"), Display(Name = "Сіль, г")] public decimal? SaltGrams { get; set; }
    [Range(typeof(decimal), "0", "100"), Display(Name = "Клітковина, г (необов’язково)")] public decimal? FibreGrams { get; set; }
    [Range(typeof(decimal), "0", "950"), Display(Name = "Енергія, ккал")] public decimal? EnergyKcal { get; set; }
    [Range(typeof(decimal), "0", "4200"), Display(Name = "Енергія, кДж")] public decimal? EnergyKj { get; set; }
    [EnumDataType(typeof(ProductDataStatus)), Display(Name = "Статус поживності")] public ProductDataStatus NutritionStatus { get; set; }
    [EnumDataType(typeof(ProductDataStatus)), Display(Name = "Статус інформації про алергени")] public ProductDataStatus AllergenStatus { get; set; }
    [StringLength(500), Display(Name = "Джерело показників (необов’язково, лише для адміністратора)")] public string? NutritionSource { get; set; }
    [StringLength(500), Display(Name = "Джерело складу (необов’язково, лише для адміністратора)")] public string? AllergenSource { get; set; }
    [StringLength(500), Display(Name = "Примітка про алергени для покупця")] public string? AllergenNotes { get; set; }
    [MaxLength(100)] public List<CakeAllergenInput> Allergens { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var required = new Dictionary<string, decimal?>
        {
            [nameof(ProteinGrams)] = ProteinGrams,
            [nameof(FatGrams)] = FatGrams,
            [nameof(SaturatedFatGrams)] = SaturatedFatGrams,
            [nameof(CarbohydrateGrams)] = CarbohydrateGrams,
            [nameof(SugarGrams)] = SugarGrams,
            [nameof(SaltGrams)] = SaltGrams,
            [nameof(EnergyKcal)] = EnergyKcal,
            [nameof(EnergyKj)] = EnergyKj
        };
        foreach (var (field, value) in required.Append(new(nameof(FibreGrams), FibreGrams)))
            if (value.HasValue && decimal.Round(value.Value, 2) != value.Value)
                yield return new("Вкажіть значення з точністю не більше двох знаків після крапки.", [field]);
        if (SaturatedFatGrams.HasValue && FatGrams.HasValue && SaturatedFatGrams > FatGrams)
            yield return new("Насичені жири не можуть перевищувати загальну кількість жирів.", [nameof(SaturatedFatGrams)]);
        if (SugarGrams.HasValue && CarbohydrateGrams.HasValue && SugarGrams > CarbohydrateGrams)
            yield return new("Цукри не можуть перевищувати загальну кількість вуглеводів.", [nameof(SugarGrams)]);
        if (ProteinGrams.HasValue && FatGrams.HasValue && CarbohydrateGrams.HasValue && ProteinGrams + FatGrams + CarbohydrateGrams > 100.5m)
            yield return new("Сума білків, жирів і вуглеводів перевищує 100 г продукту.", [nameof(ProteinGrams), nameof(FatGrams), nameof(CarbohydrateGrams)]);
        if (EnergyKcal.HasValue && EnergyKj.HasValue && Math.Abs(EnergyKj.Value - EnergyKcal.Value * 4.184m) > Math.Max(5m, EnergyKcal.Value * 4.184m * .02m))
            yield return new("Перевірте одиниці енергії: 1 ккал ≈ 4,184 кДж.", [nameof(EnergyKcal), nameof(EnergyKj)]);
        if (NutritionStatus == ProductDataStatus.Unknown && (required.Values.Any(v => v.HasValue) || FibreGrams.HasValue))
            yield return new("Для введених показників оберіть «Орієнтовні» або «Підтверджені».", [nameof(NutritionStatus)]);
        if (NutritionStatus != ProductDataStatus.Unknown && !required.Values.Any(v => v.HasValue) && !FibreGrams.HasValue)
            yield return new("Додайте показники або оберіть «Не вказано».", [nameof(NutritionStatus)]);
        if (NutritionStatus == ProductDataStatus.Verified && required.Values.Any(v => !v.HasValue))
            yield return new("Для підтвердженої поживності заповніть усі основні показники й обидві одиниці енергії.", [nameof(NutritionStatus)]);
        if (Allergens == null || Allergens.Count > 100)
            yield return new("Перевірте список алергенів.", [nameof(Allergens)]);
        else if (Allergens.Any(a => a is null))
            yield return new("Перевірте список алергенів.", [nameof(Allergens)]);
        else if (Allergens.Select(a => a.AllergenId).Distinct().Count() != Allergens.Count)
            yield return new("Алерген не може повторюватися у списку.", [nameof(Allergens)]);
    }
}

public sealed class CakeAdditionalInformationViewModel
{
    public CakeInformationInput Nutrition { get; init; } = new();
    public List<string> Contains { get; init; } = [];
    public List<string> MayContain { get; init; } = [];

    public static CakeAdditionalInformationViewModel FromCake(Cake cake) => new()
    {
        // Admin-only source references deliberately do not reach the public view model.
        Nutrition = new CakeInformationInput
        {
            ProteinGrams = cake.ProductInformation?.ProteinGrams,
            FatGrams = cake.ProductInformation?.FatGrams,
            SaturatedFatGrams = cake.ProductInformation?.SaturatedFatGrams,
            CarbohydrateGrams = cake.ProductInformation?.CarbohydrateGrams,
            SugarGrams = cake.ProductInformation?.SugarGrams,
            SaltGrams = cake.ProductInformation?.SaltGrams,
            FibreGrams = cake.ProductInformation?.FibreGrams,
            EnergyKcal = cake.ProductInformation?.EnergyKcal,
            EnergyKj = cake.ProductInformation?.EnergyKj,
            NutritionStatus = cake.ProductInformation?.NutritionStatus ?? ProductDataStatus.Unknown,
            AllergenStatus = cake.ProductInformation?.AllergenStatus ?? ProductDataStatus.Unknown,
            AllergenNotes = cake.ProductInformation?.AllergenNotes
        },
        Contains = cake.Allergens.Where(a => a.Presence == AllergenPresence.Contains).OrderBy(a => a.Allergen.Name).Select(a => a.Allergen.Name).ToList(),
        MayContain = cake.Allergens.Where(a => a.Presence == AllergenPresence.MayContain).OrderBy(a => a.Allergen.Name).Select(a => a.Allergen.Name).ToList()
    };
}
