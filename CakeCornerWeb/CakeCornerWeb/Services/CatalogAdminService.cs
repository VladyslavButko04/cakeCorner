using System.ComponentModel.DataAnnotations;
using CakeCornerWeb.Data;
using CakeCornerWeb.Models;
using CakeCornerWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CakeCornerWeb.Services;

public sealed class CatalogAdminService(ApplicationDbContext context, CatalogImageStore images)
{
    public static readonly string[] IngredientTypes = ["Biscuit", "Cream", "Filling", "Soaking", "Mousse"];

    public async Task<int> SaveCakeAsync(int? id, CakeEditorRequest request, CancellationToken ct = default)
    {
        Validator.ValidateObject(request, new ValidationContext(request), true);
        if (request.Variants == null || request.Variants.Count > 100)
            throw new CatalogRuleException("Перевірте список вагових варіантів.");
        var selected = request.Variants.Where(v => v.Enabled).ToList();
        if (selected.Count == 0 || selected.Select(v => v.WeightOptionId).Distinct().Count() != selected.Count)
            throw new CatalogRuleException("Оберіть хоча б один ваговий варіант без повторів.");
        if (selected.Any(v => !v.Price.HasValue || v.Price.Value <= 0 || v.Price.Value > 99999999.99m || decimal.Round(v.Price.Value, 2) != v.Price.Value))
            throw new CatalogRuleException("Ціна кожної обраної ваги має бути додатною, з точністю до копійок.");
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await SqlServerLocks.AcquireAsync(context, CatalogAvailability.LockResource);
        var cake = id == null ? new Cake() : await context.Cakes.Include(c => c.CakeWeights)
            .SingleOrDefaultAsync(c => c.Cake_ID == id, ct)
            ?? throw new CatalogRuleException("Торт більше не існує. Оновіть список.");
        if (id != null) CheckVersion(cake.RowVersion, request.RowVersion);
        if (id != null && !cake.IsActive)
            throw new CatalogRuleException("Спочатку відновіть торт з архіву.");
        if (!await context.Categories.AnyAsync(c => c.Category_ID == request.CategoryId, ct) ||
            !await context.Flavors.AnyAsync(f => f.Flavor_ID == request.FlavorId, ct))
            throw new CatalogRuleException("Оберіть наявну категорію та смак.");
        var weightIds = selected.Select(v => v.WeightOptionId).ToArray();
        if (await context.WeightOptions.CountAsync(w => weightIds.Contains(w.WeightOption_ID) && w.Weight > 0, ct) != selected.Count)
            throw new CatalogRuleException("Один з вагових варіантів більше не існує.");
        if (id == null && request.Image == null)
            throw new CatalogRuleException("Додайте фотографію торта.");
        string? uploaded = null;
        var commitStarted = false;
        try
        {
            if (request.Image != null) uploaded = await images.SaveAsync(request.Image, null, ct);
            cake.CakeName = request.Name.Trim();
            cake.Category_ID = request.CategoryId;
            cake.Flavor_ID = request.FlavorId;
            cake.DescriptionShort = request.ShortDescription.Trim();
            cake.DescriptionLong = request.Description.Trim();
            if (uploaded != null) cake.ImagePath = uploaded;
            if (id == null)
            {
                cake.CreatedAt = DateTime.UtcNow;
                context.Cakes.Add(cake);
            }
            else
            {
                // A variant-only edit must also advance the parent's concurrency token.
                context.Entry(cake).Property(c => c.CakeName).IsModified = true;
            }
            foreach (var variant in cake.CakeWeights) variant.IsActive = false;
            foreach (var input in selected)
            {
                // Keep existing variant IDs used by carts and historical OrderItem rows.
                var variant = cake.CakeWeights.Where(v => v.WeightOption_ID == input.WeightOptionId)
                    .OrderBy(v => v.CakeWeight_ID).FirstOrDefault();
                if (variant == null)
                {
                    variant = new CakeWeight { Cake = cake, WeightOption_ID = input.WeightOptionId };
                    cake.CakeWeights.Add(variant);
                }
                variant.IsActive = true;
                variant.Price = input.Price.Value;
            }
            await context.SaveChangesAsync(ct);
            commitStarted = true;
            await transaction.CommitAsync(ct);
            return cake.Cake_ID;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (uploaded != null && !commitStarted) images.DeleteFailedUpload(uploaded);
            throw Changed();
        }
        catch
        {
            if (uploaded != null && !commitStarted) images.DeleteFailedUpload(uploaded);
            throw;
        }
    }

    public async Task<int> SaveIngredientAsync(int? id, IngredientEditorRequest request, CancellationToken ct = default)
    {
        Validator.ValidateObject(request, new ValidationContext(request), true);
        if (decimal.Round(request.Price, 2) != request.Price)
            throw new CatalogRuleException("Вкажіть тариф з точністю до копійок.");
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await SqlServerLocks.AcquireAsync(context, CatalogAvailability.LockResource);
        var ingredient = id == null ? new Ingredient() : await context.Ingredients
            .SingleOrDefaultAsync(i => i.Ingredients_ID == id, ct)
            ?? throw new CatalogRuleException("Інгредієнт більше не існує. Оновіть список.");
        if (id != null) CheckVersion(ingredient.RowVersion, request.RowVersion);
        if (id != null && !ingredient.IsActive)
            throw new CatalogRuleException("Спочатку відновіть інгредієнт з архіву.");
        var type = await context.IngredientTypes.SingleOrDefaultAsync(t => t.IngredientsTypes_ID == request.TypeId, ct);
        if (type == null || !IngredientTypes.Contains(type.IngredientsTypesName))
            throw new CatalogRuleException("Оберіть тип, який підтримує конструктор.");
        if (id != null && ingredient.IngredientTypes_ID != request.TypeId &&
            await context.CustomCakes.AnyAsync(c => c.Biscuit_ID == id || c.Cream_ID == id ||
                c.Filling_ID == id || c.Soaking_ID == id || c.MousseCheeseCake_ID == id, ct))
            throw new CatalogRuleException("Тип використаного інгредієнта змінювати не можна. Створіть новий інгредієнт.");
        if (id == null && request.Image == null)
            throw new CatalogRuleException("Додайте фотографію інгредієнта.");
        string? uploaded = null;
        var commitStarted = false;
        try
        {
            if (request.Image != null) uploaded = await images.SaveAsync(request.Image, type.IngredientsTypesName, ct);
            ingredient.IngredientName = request.Name.Trim();
            ingredient.IngredientDescription = request.Description?.Trim();
            ingredient.Price = request.Price;
            ingredient.IngredientTypes_ID = type.IngredientsTypes_ID;
            if (uploaded != null) ingredient.IngredientImagePath = uploaded;
            if (id == null) context.Ingredients.Add(ingredient);
            await context.SaveChangesAsync(ct);
            commitStarted = true;
            await transaction.CommitAsync(ct);
            return ingredient.Ingredients_ID;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (uploaded != null && !commitStarted) images.DeleteFailedUpload(uploaded);
            throw Changed();
        }
        catch
        {
            if (uploaded != null && !commitStarted) images.DeleteFailedUpload(uploaded);
            throw;
        }
    }

    public async Task SetCakeActiveAsync(int id, string rowVersion, bool active, CancellationToken ct = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await SqlServerLocks.AcquireAsync(context, CatalogAvailability.LockResource);
        var cake = await context.Cakes.SingleOrDefaultAsync(c => c.Cake_ID == id, ct)
            ?? throw new CatalogRuleException("Торт більше не існує.");
        CheckVersion(cake.RowVersion, rowVersion);
        if (active && !await context.CakeWeights.AnyAsync(w => w.Cake_ID == id && w.IsActive && w.Price > 0, ct))
            throw new CatalogRuleException("Для відновлення потрібен наявний ваговий варіант з додатною ціною.");
        cake.IsActive = active;
        await SaveVisibilityAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task SetIngredientActiveAsync(int id, string rowVersion, bool active, CancellationToken ct = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await SqlServerLocks.AcquireAsync(context, CatalogAvailability.LockResource);
        var ingredient = await context.Ingredients.SingleOrDefaultAsync(i => i.Ingredients_ID == id, ct)
            ?? throw new CatalogRuleException("Інгредієнт більше не існує.");
        CheckVersion(ingredient.RowVersion, rowVersion);
        ingredient.IsActive = active;
        await SaveVisibilityAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task AddLookupAsync(CatalogLookupRequest request, CancellationToken ct = default)
    {
        Validator.ValidateObject(request, new ValidationContext(request), true);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await SqlServerLocks.AcquireAsync(context, CatalogAvailability.LockResource);
        var name = request.Name?.Trim();
        if (request.Kind != "Weight" && string.IsNullOrWhiteSpace(name))
            throw new CatalogRuleException("Вкажіть назву.");
        switch (request.Kind)
        {
            case "Category":
                if (await context.Categories.AnyAsync(c => c.CategoryName == name, ct))
                    throw new CatalogRuleException("Така категорія вже існує.");
                context.Categories.Add(new Category { CategoryName = name! });
                break;
            case "Flavor":
                if (await context.Flavors.AnyAsync(f => f.FlavorName == name, ct))
                    throw new CatalogRuleException("Такий смак вже існує.");
                context.Flavors.Add(new Flavor { FlavorName = name! });
                break;
            case "Weight":
                if (request.Weight <= 0 || decimal.Round(request.Weight, 1) != request.Weight)
                    throw new CatalogRuleException("Вкажіть вагу від 0,1 до 99,9 кг з кроком 0,1.");
                if (await context.WeightOptions.AnyAsync(w => w.Weight == request.Weight, ct))
                    throw new CatalogRuleException("Така вага вже існує.");
                context.WeightOptions.Add(new WeightOption { Weight = request.Weight });
                break;
        }
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task SaveVisibilityAsync(CancellationToken ct)
    {
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Changed(); }
    }

    private static void CheckVersion(byte[] current, string? posted)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(posted ?? ""); }
        catch (FormatException) { throw Changed(); }
        if (bytes.Length != 8 || !bytes.SequenceEqual(current)) throw Changed();
    }

    private static CatalogRuleException Changed() =>
        new("Дані вже змінено іншим адміністратором. Оновіть сторінку й повторіть дію.");
}
