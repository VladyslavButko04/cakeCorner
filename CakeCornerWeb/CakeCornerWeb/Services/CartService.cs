using CakeCornerWeb;
using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;

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

        public async Task AddToCartAsync(int personId, int cakeWeightId, int quantity = 1)
        {
            var cart = await GetOrCreateCartAsync(personId);

            var cakeWeight = await _context.Set<CakeWeight>()
            .Include(cw => cw.Cake)
            .FirstOrDefaultAsync(cw => cw.CakeWeight_ID == cakeWeightId);

            if (cakeWeight == null) return;

            var existingItem = cart.CartItems
                .FirstOrDefault(ci => ci.CakeWeight_ID == cakeWeightId);

            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
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
        }

        public async Task RemoveFromCartAsync(int personId, int cartItemId)
        {
            var cart = await GetOrCreateCartAsync(personId);
            var item = cart.CartItems.FirstOrDefault(ci => ci.CartItem_ID == cartItemId);

            if (item != null)
            {
                _context.CartItems.Remove(item);
                cart.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
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