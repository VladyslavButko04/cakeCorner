using CakeCornerWeb.Models.Entities;

namespace CakeCornerWeb.Interfaces
{
    public interface ICakeRepository
    {
        Task<List<Cake>> GetAllCakesWithMinPriceAsync();
        Task<List<Cake>> GetFilteredCakeAsync(int? categoryId, int? flavorId);
        Task<List<Category>> GetAllCategoriesAsync();
        Task<List<Flavor>> GetAllFlavorsAsync();
        Task<Cake?> GetCakeByIdAsync(int id);
    }
}
