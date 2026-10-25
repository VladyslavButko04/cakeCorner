using CakeCornerWeb.Models;
using CakeCornerWeb.Models.Entities;

namespace CakeCornerWeb.Interfaces
{
    public interface ICakeRepository
    {
        Task<List<Cake>> GetAllCakesWithMinPriceAsync();
        Task<List<Cake>> GetFilteredCakeAsync(int? categoryId, int? flavorId, string? search = null);
        Task<CakeCatalogPage> GetCatalogPageAsync(int? categoryId, int? flavorId, string? search = null, int page = 1, CancellationToken ct = default);
        Task<List<Category>> GetAllCategoriesAsync();
        Task<List<Flavor>> GetAllFlavorsAsync();
        Task<Cake?> GetCakeByIdAsync(int id);
    }
}
