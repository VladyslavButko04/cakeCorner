using CakeCornerWeb.Models.Entities;

namespace CakeCornerWeb.Interfaces
{
    public interface ICartService
    {
        Task<Cart> GetOrCreateCartAsync(int personId);
        Task AddToCartAsync(int personId, int cakeWeightId, int quantity = 1);
        Task RemoveFromCartAsync(int personId, int cartItemId);
        Task ClearCartAsync(int personId);
    }
}