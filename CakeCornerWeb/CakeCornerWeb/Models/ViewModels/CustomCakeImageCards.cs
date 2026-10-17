using CakeCornerWeb.Models.Entities;
using CakeCornerWeb.Services;

namespace CakeCornerWeb.Models;

public sealed record IngredientImageCard(string Slot, string Label, string ImagePath);

public sealed class CustomCakeImageCards
{
    public bool Compact { get; init; }
    public bool Interactive { get; init; }
    public IReadOnlyList<IngredientImageCard> Cards { get; init; } = [];

    public static CustomCakeImageCards ForItem(CustomCakeCart item, IngredientImageService images, bool compact = false) => new()
    {
        Compact = compact,
        Cards =
        [
            new("Biscuit", "Бісквіт", images.ForIngredient(item.CustomCake.Biscuit, "Biscuit")),
            new("Cream", "Крем", images.ForIngredient(item.CustomCake.Cream, "Cream")),
            new("Filling", "Начинка", images.ForIngredient(item.CustomCake.Filling, "Filling"))
        ]
    };
}