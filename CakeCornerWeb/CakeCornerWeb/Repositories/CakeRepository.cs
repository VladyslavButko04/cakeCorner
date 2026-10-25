using CakeCornerWeb.Data;
using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Models;
using CakeCornerWeb.Models.Entities;
using CakeCornerWeb.Services;
using Microsoft.EntityFrameworkCore;

public class CakeRepository(ApplicationDbContext context) : ICakeRepository
{
    private static IQueryable<Cake> WithCatalogData(IQueryable<Cake> query) => query
        .Include(c => c.Category).Include(c => c.CakeFlavors).ThenInclude(cf => cf.Flavor)
        .Include(c => c.CakeWeights.Where(v => v.IsActive && v.Price > 0 && v.WeightOption.Weight > 0))
        .ThenInclude(v => v.WeightOption).AsSplitQuery();

    private IQueryable<Cake> ActiveCakes() => WithCatalogData(context.Cakes.AsNoTracking().ForSale());

    public Task<List<Cake>> GetAllCakesWithMinPriceAsync() => ActiveCakes().ToListAsync();

    public Task<List<Cake>> GetFilteredCakeAsync(int? categoryId, int? flavorId, string? search = null)
        => WithCatalogData(FilteredCakes(categoryId, flavorId, search)).ToListAsync();

    private IQueryable<Cake> FilteredCakes(int? categoryId, int? flavorId, string? search)
    {
        var query = context.Cakes.AsNoTracking().ForSale();
        if (categoryId.HasValue) query = query.Where(c => c.Category_ID == categoryId);
        if (flavorId.HasValue) query = query.Where(c => c.CakeFlavors.Any(cf => cf.Flavor_ID == flavorId));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(c => c.CakeName.ToLower().Contains(term) ||
                c.DescriptionShort.ToLower().Contains(term) || c.DescriptionLong.ToLower().Contains(term));
        }
        return query;
    }

    public async Task<CakeCatalogPage> GetCatalogPageAsync(int? categoryId, int? flavorId, string? search = null, int page = 1, CancellationToken ct = default)
    {
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var query = FilteredCakes(categoryId, flavorId, search);
        var totalCount = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalCount / (double)CakeCatalogPage.DefaultPageSize);
        page = Math.Clamp(page, 1, Math.Max(1, totalPages));

        var items = await WithCatalogData(query.OrderBy(c => c.Cake_ID)
            .Skip((page - 1) * CakeCatalogPage.DefaultPageSize).Take(CakeCatalogPage.DefaultPageSize)).ToListAsync(ct);
        return new CakeCatalogPage
        {
            Items = items, Page = page, TotalCount = totalCount,
            Filters = new CakeFilterViewModel { CategoryId = categoryId, FlavorId = flavorId, Search = search }
        };
    }

    public Task<List<Category>> GetAllCategoriesAsync() => context.Categories.AsNoTracking().OrderBy(c => c.CategoryName).ToListAsync();
    public Task<List<Flavor>> GetAllFlavorsAsync() => context.Flavors.AsNoTracking().OrderBy(f => f.FlavorName).ToListAsync();
    public Task<Cake?> GetCakeByIdAsync(int id) => ActiveCakes()
        .Include(c => c.ProductInformation).Include(c => c.Allergens).ThenInclude(a => a.Allergen)
        .AsSplitQuery().SingleOrDefaultAsync(c => c.Cake_ID == id);
}
