using CakeCornerWeb.Data;
using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Models;
using CakeCornerWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CakeCornerWeb.Services;

public class CartService(ApplicationDbContext context) : ICartService
{
    public Task<Cart> GetOrCreateCartAsync(int personId) => WithCartLockAsync(personId, async () =>
    {
        var cart = await LoadCartAsync(personId) ?? CreateCart(personId);
        RefreshPrices(cart);
        cart.CustomItems = await new CustomCakeService(context).ActiveAsync(personId);
        await context.SaveChangesAsync();
        return cart;
    });

    public Task<CartOperationResult> AddToCartAsync(int personId, int cakeWeightId, int quantity = 1)
    {
        if (!ValidQuantity(quantity))
            return Task.FromResult(CartOperationResult.InvalidQuantity);

        return WithCartLockAsync(personId, async () =>
        {
            var variant = await context.CakeWeights.Include(v => v.Cake).SingleOrDefaultAsync(v => v.CakeWeight_ID == cakeWeightId);
            if (variant == null)
                return CartOperationResult.NotFound;
            if (!variant.IsActive || !variant.Cake.IsActive || variant.Price <= 0)
                return CartOperationResult.Unavailable;

            var cart = await LoadCartAsync(personId) ?? CreateCart(personId);
            var item = cart.CartItems.FirstOrDefault(i => i.CakeWeight_ID == cakeWeightId);
            // Subtraction avoids integer overflow for manipulated quantities.
            if (item != null && item.Quantity > CartLimits.MaxQuantity - quantity)
                return CartOperationResult.InvalidQuantity;

            RefreshPrices(cart);
            if (item == null)
            {
                cart.CartItems.Add(new CartItem
                {
                    Cart = cart,
                    CakeWeight_ID = cakeWeightId,
                    Quantity = quantity,
                    Price = variant.Price,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                item.Quantity += quantity;
            }

            cart.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return CartOperationResult.Success;
        });
    }

    public Task<CartOperationResult> UpdateQuantityAsync(int personId, int cartItemId, int quantity)
    {
        if (!ValidQuantity(quantity))
            return Task.FromResult(CartOperationResult.InvalidQuantity);

        return WithCartLockAsync(personId, async () =>
        {
            var cart = await LoadCartAsync(personId);
            var item = cart?.CartItems.FirstOrDefault(i => i.CartItem_ID == cartItemId);
            if (item == null)
                return CartOperationResult.NotFound;

            if (!item.CakeWeight.IsActive || !item.CakeWeight.Cake.IsActive)
                return CartOperationResult.Unavailable;
            RefreshPrices(cart!);
            item.Quantity = quantity;
            cart!.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return CartOperationResult.Success;
        });
    }

    public Task<CartOperationResult> RemoveFromCartAsync(int personId, int cartItemId) =>
        WithCartLockAsync(personId, async () =>
        {
            var cart = await LoadCartAsync(personId);
            var item = cart?.CartItems.FirstOrDefault(i => i.CartItem_ID == cartItemId);
            if (item == null)
                return CartOperationResult.NotFound;

            context.CartItems.Remove(item);
            await new CakeCornerWeb.Recommendations.Persistence.RecommendationStore(context).RecordCommerceAsync(personId,
                "remove_from_cart", CakeCornerWeb.Recommendations.Runtime.CommerceEventKey.For($"remove:ready:{cartItemId}"), cakeWeightId: item.CakeWeight_ID);
            cart!.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return CartOperationResult.Success;
        });

    public async Task ClearCartAsync(int personId)
    {
        await WithCartLockAsync(personId, async () =>
        {
            var cart = await LoadCartAsync(personId);
            var customItems = await new CustomCakeService(context).ActiveAsync(personId);
            context.CustomCartItems.RemoveRange(customItems);
            var telemetry = new CakeCornerWeb.Recommendations.Persistence.RecommendationStore(context);
            foreach (var item in customItems)
                await telemetry.RecordCommerceAsync(personId, "remove_from_cart", CakeCornerWeb.Recommendations.Runtime.CommerceEventKey.For($"remove:custom:{item.CustomCakeCart_ID}"), customCakeId: item.CustomCake_ID);
            foreach (var item in cart?.CartItems ?? [])
                await telemetry.RecordCommerceAsync(personId, "remove_from_cart", CakeCornerWeb.Recommendations.Runtime.CommerceEventKey.For($"remove:ready:{item.CartItem_ID}"), cakeWeightId: item.CakeWeight_ID);
            if (cart != null && cart.CartItems.Count > 0)
            {
                context.CartItems.RemoveRange(cart.CartItems);
                cart.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
            else if (customItems.Count > 0)
                await context.SaveChangesAsync();
            return true;
        });
    }

    private Task<Cart?> LoadCartAsync(int personId) => context.Carts
        .Include(c => c.CartItems).ThenInclude(i => i.CakeWeight).ThenInclude(w => w.Cake)
        .Include(c => c.CartItems).ThenInclude(i => i.CakeWeight).ThenInclude(w => w.WeightOption)
        .SingleOrDefaultAsync(c => c.Person_ID == personId);

    private Cart CreateCart(int personId)
    {
        var cart = Cart.Create(personId);
        context.Carts.Add(cart);
        return cart;
    }

    private static bool ValidQuantity(int quantity) => quantity is >= 1 and <= CartLimits.MaxQuantity;

    private static void RefreshPrices(Cart cart)
    {
        foreach (var item in cart.CartItems)
        {
            if (item.Price != item.CakeWeight.Price)
            {
                item.Price = item.CakeWeight.Price;
                cart.UpdatedAt = DateTime.UtcNow;
            }
        }
    }

    private async Task<T> WithCartLockAsync<T>(int personId, Func<Task<T>> action)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(personId);
        await using var transaction = context.Database.CurrentTransaction == null
            ? await context.Database.BeginTransactionAsync() : null;
        await SqlServerLocks.AcquireAsync(context, CatalogAvailability.LockResource, shared: true);
        // SQL Server transaction lock serializes this user's cart across requests/processes,
        // including the first addition when no Cart row exists yet.
        await SqlServerLocks.AcquireAsync(context, $"CakeCorner:Cart:{personId}");
        var result = await action();
        if (transaction != null) await transaction.CommitAsync();
        return result;
    }
}
