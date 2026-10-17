using CakeCornerWeb.Models;
using CakeCornerWeb.Models.Entities;

namespace CakeCornerWeb.Interfaces
{
    public interface ICartService
    {
        Task<Cart> GetOrCreateCartAsync(int personId);
        Task<CartOperationResult> AddToCartAsync(int personId, int cakeWeightId, int quantity = 1);
        Task<CartOperationResult> UpdateQuantityAsync(int personId, int cartItemId, int quantity);
        Task<CartOperationResult> RemoveFromCartAsync(int personId, int cartItemId);
        Task ClearCartAsync(int personId);
    }
}