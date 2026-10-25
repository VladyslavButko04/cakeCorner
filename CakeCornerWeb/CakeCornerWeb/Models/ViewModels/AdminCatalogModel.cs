using System.ComponentModel.DataAnnotations;
using CakeCornerWeb.Models.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CakeCornerWeb.Models;

public sealed class CakeVariantInput
{
    public bool Enabled { get; set; }
    public int WeightOptionId { get; set; }
    [Range(typeof(decimal), "0", "99999999.99", ErrorMessage = "Перевірте ціну.")]
    public decimal? Price { get; set; }
}

public sealed class CakeEditorRequest
{
    [Required, StringLength(100), Display(Name = "Назва")]
    public string Name { get; set; } = "";
    [Range(1, int.MaxValue), Display(Name = "Категорія")]
    public int CategoryId { get; set; }
    [Required, MinLength(1, ErrorMessage = "Оберіть щонайменше один смак."), MaxLength(100), Display(Name = "Смаки")]
    public List<int> FlavorIds { get; set; } = [];
    [Required, StringLength(300), Display(Name = "Короткий опис")]
    public string ShortDescription { get; set; } = "";
    [Required, StringLength(4000), Display(Name = "Повний опис")]
    public string Description { get; set; } = "";
    public IFormFile? Image { get; set; }
    [MaxLength(100)]
    public List<CakeVariantInput> Variants { get; set; } = [];
    public string? RowVersion { get; set; }
    public CakeInformationInput AdditionalInformation { get; set; } = new();
}

public sealed class CakeEditorPage
{
    public int? Id { get; init; }
    public CakeEditorRequest Request { get; init; } = new();
    public string? ImagePath { get; init; }
    public List<SelectListItem> Categories { get; init; } = [];
    public List<SelectListItem> Flavors { get; init; } = [];
    public List<WeightOption> Weights { get; init; } = [];
    public List<Allergen> Allergens { get; init; } = [];
}

public sealed class IngredientEditorRequest
{
    [Required, StringLength(100), Display(Name = "Назва")]
    public string Name { get; set; } = "";
    [Range(1, int.MaxValue), Display(Name = "Тип інгредієнта")]
    public int TypeId { get; set; }
    [StringLength(255), Display(Name = "Опис")]
    public string? Description { get; set; }
    [Range(typeof(decimal), "0", "99999999.99"), Display(Name = "Тариф, грн/кг")]
    public decimal Price { get; set; }
    public IFormFile? Image { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class IngredientEditorPage
{
    public int? Id { get; init; }
    public IngredientEditorRequest Request { get; init; } = new();
    public string? ImagePath { get; init; }
    public List<SelectListItem> Types { get; init; } = [];
}

public sealed class CatalogListPage<T>
{
    public List<T> Items { get; init; } = [];
    public string? Query { get; init; }
    public bool Archived { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
}

public sealed class CatalogVisibilityRequest
{
    [Required] public string RowVersion { get; set; } = "";
}

public sealed class CatalogDeletePage
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Controller { get; init; } = "";
    public string RowVersion { get; init; } = "";
}

public sealed class CatalogDashboard
{
    public int Cakes { get; init; }
    public int Ingredients { get; init; }
}

public sealed class CatalogLookupRequest
{
    [Required, RegularExpression("^(Category|Flavor|Weight)$")]
    public string Kind { get; set; } = "";
    [StringLength(100)] public string? Name { get; set; }
    [Range(typeof(decimal), "0", "99.9")] public decimal Weight { get; set; }
}

public sealed class CatalogLookupPage
{
    public CatalogLookupRequest Request { get; init; } = new();
    public List<Category> Categories { get; init; } = [];
    public List<Flavor> Flavors { get; init; } = [];
    public List<WeightOption> Weights { get; init; } = [];
}
