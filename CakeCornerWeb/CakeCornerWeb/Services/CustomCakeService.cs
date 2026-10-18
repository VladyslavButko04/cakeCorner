using CakeCornerWeb.Data;
using CakeCornerWeb.Models;
using CakeCornerWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace CakeCornerWeb.Services;

public class CustomCakeService(ApplicationDbContext context)
{
    public IQueryable<CustomCakeCart> Query() => context.CustomCartItems
        .Include(i => i.CustomCake).ThenInclude(c => c.Biscuit)
        .Include(i => i.CustomCake).ThenInclude(c => c.Filling)
        .Include(i => i.CustomCake).ThenInclude(c => c.Cream)
        .Include(i => i.CustomCake).ThenInclude(c => c.Soaking)
        .Include(i => i.CustomCake).ThenInclude(c => c.Mousse);

    public Task<List<CustomCakeCart>> ActiveAsync(int personId) => Query()
        .Where(i => i.Order_ID == null && i.CustomCake.Person_ID == personId).ToListAsync();

    public async Task AddAsync(int personId, CustomCakeRequest request)
    {
        Validator.ValidateObject(request, new ValidationContext(request), true);
        if (request.Weight != decimal.Round(request.Weight, 1))
            throw new OrderRuleException("Вкажіть вагу з точністю до 0,1 кг.");
        await using var transaction = await context.Database.BeginTransactionAsync();
        await SqlServerLocks.AcquireAsync(context, CatalogAvailability.LockResource, shared: true);
        await SqlServerLocks.AcquireAsync(context, $"CakeCorner:Cart:{personId}");
        var choices = new[] { (request.BiscuitId, "Biscuit"), (request.FillingId, "Filling"),
            (request.CreamId, "Cream"), (request.SoakingId, "Soaking"), (request.MousseId, "Mousse") };
        var ids = choices.Select(c => c.Item1).ToArray();
        var ingredients = await context.Ingredients.Include(i => i.Type).Where(i => i.IsActive && ids.Contains(i.Ingredients_ID)).ToListAsync();
        foreach (var (id, type) in choices)
            if (!ingredients.Any(i => i.Ingredients_ID == id && i.Type.IngredientsTypesName == type && i.Price >= 0 && !string.IsNullOrWhiteSpace(i.IngredientName)))
                throw new OrderRuleException("Один із компонентів недоступний або має неправильний тип.");
        // Initial pricing policy: sum of selected component rates per kg * cake weight.
        var price = decimal.Round(choices.Sum(c => ingredients.Single(i => i.Ingredients_ID == c.Item1).Price) * request.Weight, 2, MidpointRounding.AwayFromZero);
        if (price <= 0 || price > 99999999.99m)
            throw new OrderRuleException("Не вдалося визначити ціну цього торта.");
        var cake = new CustomCake
        {
            Person_ID = personId,
            Biscuit_ID = request.BiscuitId,
            Filling_ID = request.FillingId,
            Cream_ID = request.CreamId,
            Soaking_ID = request.SoakingId,
            MousseCheeseCake_ID = request.MousseId,
            Weight = request.Weight,
            Price = price,
            Inscription = request.Inscription?.Trim(),
            CreatedAt = DateTime.UtcNow,
            Shape = request.Shape,
            GuestCount = request.GuestCount,
            DesignNotes = request.DesignNotes?.Trim()
        };
        context.CustomCartItems.Add(new CustomCakeCart { CustomCake = cake, Quantity = request.Quantity });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<bool> ChangeAsync(int personId, int itemId, int? quantity)
    {
        if (quantity != null && (quantity < 1 || quantity > CartLimits.MaxQuantity))
            throw new OrderRuleException($"Кількість має бути від 1 до {CartLimits.MaxQuantity}.");
        await using var transaction = await context.Database.BeginTransactionAsync();
        await SqlServerLocks.AcquireAsync(context, CatalogAvailability.LockResource, shared: true);
        await SqlServerLocks.AcquireAsync(context, $"CakeCorner:Cart:{personId}");
        var item = await Query()
            .SingleOrDefaultAsync(i => i.CustomCakeCart_ID == itemId && i.Order_ID == null && i.CustomCake.Person_ID == personId);
        if (item == null) return false;
        if (quantity != null && !CatalogAvailability.IsAvailable(item.CustomCake))
            throw new OrderRuleException("Один з інгредієнтів більше недоступний. Видаліть конфігурацію та зберіть нову.");
        if (quantity == null) context.CustomCartItems.Remove(item);
        else item.Quantity = quantity.Value;
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }
}
