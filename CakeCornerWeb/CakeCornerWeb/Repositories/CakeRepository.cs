using CakeCornerWeb.Data;
using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Models.Entities;
using CakeCornerWeb.Services;
using Microsoft.EntityFrameworkCore;

public class CakeRepository(ApplicationDbContext context) : ICakeRepository
{
    private IQueryable<Cake> ActiveCakes() => context.Cakes.AsNoTracking().ForSale()
        .Include(c => c.Category).Include(c => c.Flavor)
        .Include(c => c.CakeWeights.Where(v => v.IsActive && v.Price > 0 && v.WeightOption.Weight > 0))
        .ThenInclude(v => v.WeightOption);

    public Task<List<Cake>> GetAllCakesWithMinPriceAsync() => ActiveCakes().ToListAsync();

    public Task<List<Cake>> GetFilteredCakeAsync(int? categoryId, int? flavorId, string? search = null)
    {
        var query = ActiveCakes();
        if (categoryId.HasValue) query = query.Where(c => c.Category_ID == categoryId);
        if (flavorId.HasValue) query = query.Where(c => c.Flavor_ID == flavorId);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(c => c.CakeName.Contains(search) ||
            c.DescriptionShort.Contains(search) || c.DescriptionLong.Contains(search));
        return query.ToListAsync();
    }

    public Task<List<Category>> GetAllCategoriesAsync() => context.Categories.AsNoTracking().OrderBy(c => c.CategoryName).ToListAsync();
    public Task<List<Flavor>> GetAllFlavorsAsync() => context.Flavors.AsNoTracking().OrderBy(f => f.FlavorName).ToListAsync();
    public Task<Cake?> GetCakeByIdAsync(int id) => ActiveCakes().SingleOrDefaultAsync(c => c.Cake_ID == id);
}
