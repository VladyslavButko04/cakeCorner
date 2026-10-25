using System.Data;
using CakeCornerWeb.Data;
using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Models;
using CakeCornerWeb.Models.Entities;
using CakeCornerWeb.Recommendations.Core;
using CakeCornerWeb.Recommendations.Persistence;
using CakeCornerWeb.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CakeCornerWeb.Recommendations.Runtime;

public sealed class RecommendationService(ApplicationDbContext context, RecommendationStore store,
    RecommendationMethod method, RecommendationEngine engine, IOptions<RecommendationRuntimeOptions> settings,
    ICartService carts, CustomCakeService custom, IngredientImageService images)
{
    public async Task<RecommendationResult> QueryAsync(int? personId, Guid? sessionId, RecommendationQuery query, CancellationToken ct = default)
    {
        if (!settings.Value.Enabled) throw new RecommendationUnavailableException("Підбір тимчасово вимкнено.");
        engine.ValidateRequest(query);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await SqlServerLocks.AcquireAsync(context, CatalogAvailability.LockResource, shared: true);
        var snapshot = await store.LoadSnapshotAsync(settings.Value.AnnotationVersion, method, query.DataMode, ct);
        var result = engine.Recommend(query, snapshot);
        ResolveImages(result, snapshot);
        await store.LogResultAsync(personId, sessionId, query, snapshot, result, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task AddAsync(int personId, RecommendationAddRequest request, CancellationToken ct = default)
    {
        if (!settings.Value.Enabled) throw new RecommendationUnavailableException("Підбір тимчасово вимкнено.");
        if (request.EventKey == Guid.Empty || request.RequestId <= 0 || string.IsNullOrWhiteSpace(request.CandidateKey))
            throw new RecommendationValidationException("Оновіть список рекомендацій та повторіть вибір.");
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await SqlServerLocks.AcquireAsync(context, CatalogAvailability.LockResource, shared: true);
        await SqlServerLocks.AcquireAsync(context, $"CakeCorner:Cart:{personId}");
        if (await store.HasEventAsync(personId, null, request.EventKey, "add_to_cart", ct))
        {
            await store.RecordEventAsync(personId, null, new RecommendationEventRequest
            { EventKey = request.EventKey, RequestId = request.RequestId, CandidateKey = request.CandidateKey, EventType = "add_to_cart" }, ct);
            await transaction.CommitAsync(ct); return;
        }
        var logged = await store.FindLoggedItemAsync(personId, null, request.RequestId, request.CandidateKey, ct)
            ?? throw new RecommendationValidationException("Ця рекомендація недоступна для вашого облікового запису.");
        if (logged.MethodVersion != method.Version || logged.AnnotationVersion != settings.Value.AnnotationVersion)
            throw new RecommendationValidationException("Дані підбору оновилися. Отримайте нові рекомендації.");
        var query = logged.Query;
        var item = logged.Item;
        if (item.Kind is not ("Ready" or "Custom"))
            throw new RecommendationValidationException("Спочатку завершіть склад торта в конструкторі.");
        // Re-evaluate exactly this candidate using current metadata; beam pruning cannot discard it.
        if (item.Kind == "Custom")
        {
            query.Mode = "Custom";
            query.LockedIngredients = new(item.Ingredients);
        }
        else query.Mode = "Ready";
        engine.ValidateRequest(query);
        var snapshot = await store.LoadSnapshotAsync(logged.AnnotationVersion, method, query.DataMode, ct);
        if (item.Kind == "Ready") snapshot.Ready = snapshot.Ready.Where(r => r.CakeWeightId == item.CakeWeightId).ToList();
        var current = engine.Recommend(query, snapshot).Items.SingleOrDefault(i => i.Key == item.Key)
            ?? throw new RecommendationValidationException("Склад, наявність або обмеження змінилися. Оновіть рекомендації.");
        if (current.UnitPrice != item.UnitPrice)
            throw new RecommendationValidationException("Ціна змінилася. Перегляньте нову ціну та підтвердьте вибір.");
        if (current.WeightKg != item.WeightKg || current.CakeId != item.CakeId ||
            !string.Equals(current.Name, item.Name, StringComparison.Ordinal) ||
            !current.IngredientNames.OrderBy(p => p.Key, StringComparer.Ordinal)
                .SequenceEqual(item.IngredientNames.OrderBy(p => p.Key, StringComparer.Ordinal)))
            throw new RecommendationValidationException("Вага або склад товару змінилися. Оновіть рекомендації та підтвердьте вибір.");
        if (item.Kind == "Ready")
        {
            var result = await carts.AddToCartAsync(personId, current.CakeWeightId!.Value, query.Quantity);
            if (result != CartOperationResult.Success)
                throw new RecommendationValidationException("Не вдалося додати торт. Перевірте його доступність та кількість.");
        }
        else
        {
            await custom.AddAsync(personId, new CustomCakeRequest
            {
                BiscuitId = current.Ingredients["Biscuit"], CreamId = current.Ingredients["Cream"],
                FillingId = current.Ingredients["Filling"], MousseId = current.Ingredients["Mousse"],
                Weight = query.WeightKg, Quantity = query.Quantity, Shape = query.Shape,
                Inscription = request.Inscription, DesignNotes = request.DesignNotes, GuestCount = request.GuestCount
            });
        }
        await store.RecordEventAsync(personId, null, new RecommendationEventRequest
        { EventKey = request.EventKey, RequestId = request.RequestId, CandidateKey = request.CandidateKey, EventType = "add_to_cart" }, ct);
        await transaction.CommitAsync(ct);
    }

    private void ResolveImages(RecommendationResult result, RecommendationSnapshot snapshot)
    {
        foreach (var item in result.Items)
        {
            if (item.Kind == "Ready") continue; // Cake image paths were already validated when uploaded.
            var id = item.IngredientId ?? item.Ingredients.GetValueOrDefault("Biscuit");
            var option = snapshot.Ingredients.FirstOrDefault(i => i.Id == id);
            item.ImagePath = images.Resolve(option?.ImagePath, option?.Type);
        }
    }
}
