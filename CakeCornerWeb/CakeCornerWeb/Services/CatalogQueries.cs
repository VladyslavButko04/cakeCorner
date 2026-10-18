using CakeCornerWeb.Models.Entities;

namespace CakeCornerWeb.Services;

public static class CatalogQueries
{
    public static IQueryable<Cake> ForSale(this IQueryable<Cake> query) => query.Where(c => c.IsActive &&
        c.CakeWeights.Any(v => v.IsActive && v.Price > 0 && v.WeightOption.Weight > 0));
}
