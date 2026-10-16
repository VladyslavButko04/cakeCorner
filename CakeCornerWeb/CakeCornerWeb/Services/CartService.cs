using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Models;
using CakeCornerWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;
using CakeCornerWeb.Data;

namespace CakeCornerWeb.Services
{
    public class CartService : ICartService
    {
        private readonly ApplicationDbContext _context;

        public CartService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Cart> GetOrCreateCartAsync(int personId)
        {
            var cart = await _context.Carts
                .Include(c => c.CartItems)
                    .ThenInclude(ci => ci.CakeWeight)
                        .ThenInclude(cw => cw.Cake)
                .FirstOrDefaultAsync(c => c.Person_ID == personId);

            if (cart == null)
            {
                cart = Cart.Create(personId);
                _context.Carts.Add(cart);
                await _context.SaveChangesAsync();
            }

            return cart;
        }

        public async Task<CartOperationResult> AddToCartAsync(int personId, int cakeWeightId, int quantity = 1)
        {
            if (quantity <= 0 || quantity > CartLimits.MaxQuantity)
            {
                return CartOperationResult.InvalidQuantity;
            }

            var cart = await GetOrCreateCartAsync(personId);

            var cakeWeight = await _context.Set<CakeWeight>()
                .Include(cw => cw.Cake)
                .FirstOrDefaultAsync(cw => cw.CakeWeight_ID == cakeWeightId);

            if (cakeWeight == null)
            {
                return CartOperationResult.NotFound;
            }

            var existingItem = cart.CartItems
                .FirstOrDefault(ci => ci.CakeWeight_ID == cakeWeightId);

            if (existingItem != null)
            {
                int newQuantity = existingItem.Quantity + quantity;
                if (newQuantity > CartLimits.MaxQuantity)
                {
                    return CartOperationResult.InvalidQuantity;
                }
                existingItem.Quantity = newQuantity;
            }
            else
            {
                var newItem = new CartItem
                {
                    Cart_ID = cart.Cart_ID,
                    CakeWeight_ID = cakeWeightId,
                    Quantity = quantity,
                    Price = cakeWeight.Price,
                    CreatedAt = DateTime.UtcNow
                };
                cart.CartItems.Add(newItem);
            }

            cart.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return CartOperationResult.Success;
        }

        public async Task<CartOperationResult> UpdateQuantityAsync(int personId, int cartItemId, int quantity)
        {
            if (quantity > CartLimits.MaxQuantity)
            {
                return CartOperationResult.InvalidQuantity;
            }

            var cart = await GetOrCreateCartAsync(personId);
            var item = cart.CartItems.FirstOrDefault(ci => ci.CartItem_ID == cartItemId);

            if (item == null)
            {
                return CartOperationResult.NotFound;
            }

            if (quantity <= 0)
            {
                _context.CartItems.Remove(item);
            }
            else
            {
                item.Quantity = quantity;
            }

            cart.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return CartOperationResult.Success;
        }

        public async Task<CartOperationResult> RemoveFromCartAsync(int personId, int cartItemId)
        {
            var cart = await GetOrCreateCartAsync(personId);
            var item = cart.CartItems.FirstOrDefault(ci => ci.CartItem_ID == cartItemId);

            if (item == null)
            {
                return CartOperationResult.NotFound;
            }

            _context.CartItems.Remove(item);
            cart.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return CartOperationResult.Success;
        }

        public async Task ClearCartAsync(int personId)
        {
            var cart = await GetOrCreateCartAsync(personId);
            _context.CartItems.RemoveRange(cart.CartItems);
            cart.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }
}