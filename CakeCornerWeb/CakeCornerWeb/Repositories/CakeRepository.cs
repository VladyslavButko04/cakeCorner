using Azure.Core.Pipeline;
using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;
using CakeCornerWeb.Data;

public class CakeRepository : ICakeRepository
{
    private readonly ApplicationDbContext _context;

    public CakeRepository(ApplicationDbContext context)
    {
        _context = context;
    }


    public async Task<List<Cake>> GetAllCakesWithMinPriceAsync()
    {
        return await _context.Cakes
    .Include(c => c.Category)
    .Include(c => c.CakeWeights)
        .ThenInclude(w => w.WeightOption)
    .ToListAsync();
    }


    public async Task<List<Cake>> GetFilteredCakeAsync(int? categoryId, int? flavorId)
    {
        var query = _context.Cakes
            .Include(c => c.Category)
            .Include(c => c.Flavor)
            .Include(c => c.CakeWeights)
                .ThenInclude(w => w.WeightOption)
            .AsQueryable();

        if (categoryId.HasValue)
            query = query.Where(c => c.Category_ID == categoryId);

        if (flavorId.HasValue)
            query = query.Where(c => c.Flavor_ID == flavorId);

        return await query.ToListAsync();
    }

    public async Task<List<Category>> GetAllCategoriesAsync()
    {
        return await _context.Categories.ToListAsync();
    }


    public async Task<List<Flavor>> GetAllFlavorsAsync()
    {
        return await _context.Flavors.ToListAsync();
    }

    public async Task<Cake?> GetCakeByIdAsync(int id)
    {
        return await _context.Cakes
            .Include(c => c.CakeWeights)
                .ThenInclude(cw => cw.WeightOption)
            .FirstOrDefaultAsync(c => c.Cake_ID == id);
    }
}