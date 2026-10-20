using CakeCornerWeb.Models.Entities;

namespace CakeCornerWeb.Services;

public static class CatalogAvailability
{
    public const string LockResource = "CakeCorner:Catalog";

    public static bool IsAvailable(CustomCake cake) => cake.Biscuit.IsActive &&
        cake.Cream.IsActive && cake.Filling.IsActive && cake.Mousse.IsActive;
}

public sealed class CatalogRuleException(string message) : Exception(message);
