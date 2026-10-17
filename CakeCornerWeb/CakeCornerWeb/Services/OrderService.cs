using CakeCornerWeb.Data;
using CakeCornerWeb.Models;
using CakeCornerWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace CakeCornerWeb.Services;

public class OrderService(ApplicationDbContext context)
{
    public static string Fingerprint(Cart cart)
    {
        var contents = new
        {
            Ready = cart.CartItems.OrderBy(i => i.CartItem_ID).Select(i => new
            { i.CartItem_ID, i.CakeWeight_ID, i.Quantity, i.CakeWeight.Price, i.CakeWeight.Cake.CakeName, i.CakeWeight.WeightOption.Weight }),
            Custom = cart.CustomItems.OrderBy(i => i.CustomCakeCart_ID).Select(i => new
            { i.CustomCakeCart_ID, i.Quantity, i.CustomCake.Price, i.CustomCake.Weight, i.CustomCake.ConfigurationDescription, i.CustomCake.Inscription })
        };
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(contents)));
    }
    public static DateTime InKyiv(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv"));
    public static DateTime BusinessToday => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv")).Date;

    public IQueryable<PersonOrder> DetailsQuery() => context.Orders
        .Include(o => o.Status).Include(o => o.Person).Include(o => o.Payment)
        .Include(o => o.PickupLocation).ThenInclude(p => p.City)
        .Include(o => o.Items).ThenInclude(i => i.CakeWeight).ThenInclude(w => w.Cake)
        .Include(o => o.Items).ThenInclude(i => i.CakeWeight).ThenInclude(w => w.WeightOption)
        .Include(o => o.CustomItems).ThenInclude(i => i.CustomCake).ThenInclude(c => c.Biscuit)
        .Include(o => o.CustomItems).ThenInclude(i => i.CustomCake).ThenInclude(c => c.Filling)
        .Include(o => o.CustomItems).ThenInclude(i => i.CustomCake).ThenInclude(c => c.Cream)
        .Include(o => o.CustomItems).ThenInclude(i => i.CustomCake).ThenInclude(c => c.Soaking)
        .Include(o => o.CustomItems).ThenInclude(i => i.CustomCake).ThenInclude(c => c.Mousse)
        .Include(o => o.History).AsSplitQuery();

    public async Task<CheckoutPage> CheckoutPageAsync(int personId, CheckoutRequest? request = null)
    {
        var cart = await new CartService(context).GetOrCreateCartAsync(personId);
        var phone = await context.Persons.Where(p => p.Person_ID == personId).Select(p => p.Phone).SingleOrDefaultAsync();
        var page = new CheckoutPage
        {
            Cart = cart,
            Locations = await context.PickupLocations.Include(p => p.City).OrderBy(p => p.City.CityName).ThenBy(p => p.Address).ToListAsync(),
            Request = request ?? new CheckoutRequest
            {
                CheckoutToken = Guid.NewGuid(),
                PhoneNumber = phone ?? "",
                DeliveryDateTime = BusinessToday.AddDays(2).AddHours(12)
            }
        };
        // A changed price requires a fresh review instead of trusting a posted total.
        page.Request.ExpectedTotal = page.Total;
        page.Request.CartFingerprint = Fingerprint(cart);
        if (page.Request.CheckoutToken == Guid.Empty) page.Request.CheckoutToken = Guid.NewGuid();
        return page;
    }

    public async Task<int> CheckoutAsync(int personId, CheckoutRequest request)
    {
        Validator.ValidateObject(request, new ValidationContext(request), true);
        if (request.CheckoutToken == Guid.Empty) throw new OrderRuleException("Оновіть сторінку оформлення.");
        await using var transaction = await context.Database.BeginTransactionAsync();
        await SqlServerLocks.AcquireAsync(context, $"CakeCorner:Cart:{personId}");
        var previous = await context.Orders.SingleOrDefaultAsync(o => o.Person_ID == personId && o.CheckoutToken == request.CheckoutToken);
        if (previous != null) return previous.Order_ID;

        if (request.DeliveryDateTime.Date < BusinessToday.AddDays(1) || request.DeliveryDateTime.Date > BusinessToday.AddDays(90))
            throw new OrderRuleException("Оберіть бажану дату від завтра до 90 днів наперед.");
        if (!await context.PickupLocations.AnyAsync(p => p.PickupLocation_ID == request.PickupLocationId))
            throw new OrderRuleException("Пункт видачі недоступний.");
        if (!await context.Persons.AnyAsync(p => p.Person_ID == personId))
            throw new OrderRuleException("Обліковий запис недоступний.");

        // Read fresh database state even if this DbContext was previously used to show a cart.
        var ready = await context.CartItems.AsNoTracking()
            .Include(i => i.CakeWeight).ThenInclude(w => w.Cake)
            .Include(i => i.CakeWeight).ThenInclude(w => w.WeightOption)
            .Where(i => i.Cart.Person_ID == personId).ToListAsync();
        var custom = await new CustomCakeService(context).Query().AsNoTracking()
            .Where(i => i.Order_ID == null && i.CustomCake.Person_ID == personId).ToListAsync();
        if (ready.Count == 0 && custom.Count == 0) throw new OrderRuleException("Кошик порожній.");
        if (request.CartFingerprint != Fingerprint(new Cart { CartItems = ready, CustomItems = custom }))
            throw new OrderRuleException("Склад або ціни кошика змінилися. Перевірте замовлення та підтвердьте його повторно.");
        if (ready.Any(i => i.Quantity < 1 || i.Quantity > CartLimits.MaxQuantity || i.CakeWeight.Price <= 0)
            || custom.Any(i => i.Quantity < 1 || i.Quantity > CartLimits.MaxQuantity || i.CustomCake.Price <= 0))
            throw new OrderRuleException("Перевірте кількість і ціни товарів у кошику.");
        var total = ready.Sum(i => i.Quantity * i.CakeWeight.Price) + custom.Sum(i => i.Quantity * i.CustomCake.Price);
        if (total > 99999999.99m) throw new OrderRuleException("Сума замовлення перевищує допустиму.");
        if (total != request.ExpectedTotal) throw new OrderRuleException("Ціни або склад кошика змінилися. Перевірте нову суму та підтвердьте замовлення повторно.");
        var status = await context.OrderStatuses.SingleOrDefaultAsync(s => s.Code == OrderWorkflow.Pending)
            ?? throw new OrderRuleException("Оформлення недоступне: зверніться до кондитерської.");
        var order = new PersonOrder
        {
            Person_ID = personId,
            Status = status,
            PickupLocation_ID = request.PickupLocationId,
            CreatedAt = DateTime.UtcNow,
            DeliveryDateTime = request.DeliveryDateTime,
            Comment = request.Comment?.Trim(),
            PhoneNumber = request.PhoneNumber.Trim(),
            RequiredCallback = request.RequiredCallback,
            CheckoutToken = request.CheckoutToken,
            TotalPrice = total,
            Payment = new Payment { OnlineAmount = 0, IsOnlinePaid = false, IsFinalPaid = false, RemaininAmount = total, PaidAmount = 0 }
        };
        foreach (var i in ready)
            order.Items.Add(new OrderItem
            {
                CakeWeight_ID = i.CakeWeight_ID,
                Quantity = i.Quantity,
                ItemPrice = i.CakeWeight.Price,
                CakeNameSnapshot = i.CakeWeight.Cake.CakeName,
                WeightSnapshot = i.CakeWeight.WeightOption.Weight
            });
        order.History.Add(new OrderStatusHistory
        {
            ToCode = OrderWorkflow.Pending,
            ChangedAt = DateTime.UtcNow,
            ChangedByPerson_ID = personId,
            Note = "Замовлення оформлено. Оплата при отриманні."
        });
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        foreach (var i in custom)
        {
            var description = i.CustomCake.ConfigurationDescription + (string.IsNullOrWhiteSpace(i.CustomCake.Inscription) ? "" : $"; напис: {i.CustomCake.Inscription}");
            var updated = await context.CustomCartItems.Where(x => x.CustomCakeCart_ID == i.CustomCakeCart_ID && x.Order_ID == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Order_ID, order.Order_ID)
                    .SetProperty(x => x.UnitPriceSnapshot, i.CustomCake.Price)
                    .SetProperty(x => x.WeightSnapshot, i.CustomCake.Weight)
                    .SetProperty(x => x.DescriptionSnapshot, description));
            if (updated != 1) throw new OrderRuleException("Кошик змінився. Повторіть оформлення.");
        }
        var readyIds = ready.Select(i => i.CartItem_ID).ToArray();
        if (await context.CartItems.Where(i => readyIds.Contains(i.CartItem_ID) && i.Cart.Person_ID == personId).ExecuteDeleteAsync() != ready.Count)
            throw new OrderRuleException("Кошик змінився. Повторіть оформлення.");
        await context.Carts.Where(c => c.Person_ID == personId).ExecuteUpdateAsync(s => s.SetProperty(c => c.UpdatedAt, DateTime.UtcNow));
        await transaction.CommitAsync();
        return order.Order_ID;
    }

    public async Task<OrderListPage> ListAsync(int? personId, bool history, string? status, int page, bool includeAll = false)
    {
        var query = context.Orders.AsNoTracking().Include(o => o.Status).Include(o => o.Person)
            .Include(o => o.CustomItems).ThenInclude(i => i.CustomCake).ThenInclude(c => c.Biscuit)
            .Include(o => o.CustomItems).ThenInclude(i => i.CustomCake).ThenInclude(c => c.Cream)
            .Include(o => o.CustomItems).ThenInclude(i => i.CustomCake).ThenInclude(c => c.Filling)
            .AsSplitQuery();
        if (personId != null) query = query.Where(o => o.Person_ID == personId);
        if (history) query = query.Where(o => OrderWorkflow.Terminal.Contains(o.Status.Code!));
        else if (personId != null && !includeAll) query = query.Where(o => !OrderWorkflow.Terminal.Contains(o.Status.Code!));
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(o => o.Status.Code == status);
        var pages = (int)Math.Ceiling(await query.CountAsync() / 20m);
        page = Math.Clamp(page, 1, Math.Max(1, pages));
        return new OrderListPage
        {
            Orders = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Order_ID).Skip((page - 1) * 20).Take(20).ToListAsync(),
            Page = page,
            TotalPages = pages,
            History = history,
            Status = status,
            IsAdmin = personId == null,
            Scope = includeAll ? "all" : history ? "completed" : "active"
        };
    }

    public async Task<bool> ChangeStatusAsync(int orderId, int actorId, bool isAdmin, OrderChangeRequest request)
    {
        if (isAdmin) await RequireAdminAsync(actorId);
        Validator.ValidateObject(request, new ValidationContext(request), true);
        await using var transaction = await context.Database.BeginTransactionAsync();
        await SqlServerLocks.AcquireAsync(context, $"CakeCorner:Order:{orderId}");
        var order = await context.Orders.Include(o => o.Status).Include(o => o.Payment)
            .SingleOrDefaultAsync(o => o.Order_ID == orderId && (isAdmin || o.Person_ID == actorId));
        if (order == null) return false;
        CheckVersion(order, request.RowVersion);
        if (!isAdmin && (request.StatusCode != OrderWorkflow.Cancelled || !OrderWorkflow.CanCancel(order.Status.Code)))
            throw new OrderRuleException("Скасування на цьому етапі недоступне.");
        if (!OrderWorkflow.Next(order.Status.Code).Contains(request.StatusCode))
            throw new OrderRuleException("Такий перехід статусу недоступний.");
        if (request.StatusCode is not (OrderWorkflow.Cancelled or OrderWorkflow.Rejected) &&
            !await HasPositionsAsync(orderId))
            throw new OrderRuleException("Замовлення не має позицій. Його потрібно скасувати або уточнити.");
        if (request.StatusCode is OrderWorkflow.Cancelled or OrderWorkflow.Rejected && string.IsNullOrWhiteSpace(request.Reason))
            throw new OrderRuleException("Вкажіть причину скасування або відхилення.");
        if (request.StatusCode == OrderWorkflow.Completed && order.Payment?.IsFinalPaid != true)
            throw new OrderRuleException("Спочатку підтвердьте отримання оплати.");
        if (request.StatusCode is OrderWorkflow.Cancelled or OrderWorkflow.Rejected && order.Payment?.PaidAmount > 0)
            throw new OrderRuleException("Для оплаченого замовлення спочатку потрібне повернення коштів. Зверніться до відповідального за оплату.");
        var next = await context.OrderStatuses.SingleOrDefaultAsync(s => s.Code == request.StatusCode)
            ?? throw new OrderRuleException("Статус не налаштований.");
        order.History.Add(new OrderStatusHistory
        {
            FromCode = order.Status.Code,
            ToCode = next.Code!,
            ChangedAt = DateTime.UtcNow,
            ChangedByPerson_ID = actorId,
            Note = request.Reason?.Trim()
        });
        order.Status = next;
        order.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task<bool> MarkPaidAsync(int orderId, int actorId, string rowVersion)
    {
        await RequireAdminAsync(actorId);
        await using var transaction = await context.Database.BeginTransactionAsync();
        await SqlServerLocks.AcquireAsync(context, $"CakeCorner:Order:{orderId}");
        var order = await context.Orders.Include(o => o.Status).Include(o => o.Payment).SingleOrDefaultAsync(o => o.Order_ID == orderId);
        if (order == null) return false;
        if (order.Payment?.IsFinalPaid == true) return true;
        CheckVersion(order, rowVersion);
        if (order.Status.Code != OrderWorkflow.Ready) throw new OrderRuleException("Оплату при отриманні можна підтвердити для готового до видачі замовлення.");
        if (!await HasPositionsAsync(orderId)) throw new OrderRuleException("Замовлення не має позицій.");
        order.Payment ??= new Payment();
        var received = Math.Max(0, order.TotalPrice - order.Payment.PaidAmount);
        order.Payment.PaidAmount = order.TotalPrice;
        order.Payment.RemaininAmount = 0;
        order.Payment.IsFinalPaid = true;
        order.Payment.FinalPaidAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;
        order.History.Add(new OrderStatusHistory
        {
            FromCode = order.Status.Code,
            ToCode = order.Status.Code!,
            ChangedAt = DateTime.UtcNow,
            ChangedByPerson_ID = actorId,
            Note = $"Адміністратор підтвердив отримання {received:0.00} грн при видачі."
        });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    private static void CheckVersion(PersonOrder order, string version)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(version); }
        catch (FormatException) { throw new OrderRuleException("Оновіть сторінку замовлення."); }
        if (!order.RowVersion.SequenceEqual(bytes)) throw new OrderRuleException("Замовлення вже змінено. Оновіть сторінку та повторіть дію.");
    }

    private async Task RequireAdminAsync(int actorId)
    {
        if (!await context.Persons.AnyAsync(p => p.Person_ID == actorId && p.Role.RoleName == "Admin"))
            throw new OrderRuleException("Обробка замовлень доступна лише адміністратору.");
    }

    private async Task<bool> HasPositionsAsync(int orderId) =>
        await context.OrderItems.AnyAsync(i => i.Order_ID == orderId && i.Quantity > 0) ||
        await context.CustomCartItems.AnyAsync(i => i.Order_ID == orderId && i.Quantity > 0);
}
