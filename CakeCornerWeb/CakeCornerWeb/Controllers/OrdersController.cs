using System.ComponentModel.DataAnnotations;
using CakeCornerWeb.Extensions;
using CakeCornerWeb.Models;
using CakeCornerWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CakeCornerWeb.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
public class OrdersController(OrderService orders) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(bool history = false, int page = 1, string scope = "all")
    {
        if (User.PersonId() is not int id) return Forbid();
        if (history) scope = "completed";
        if (scope is not ("all" or "active" or "completed")) scope = "all";
        return View(await orders.ListAsync(id, scope == "completed", null, page, scope == "all"));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (User.PersonId() is not int personId) return Forbid();
        var order = await orders.DetailsQuery().AsNoTracking().SingleOrDefaultAsync(o => o.Order_ID == id && o.Person_ID == personId);
        return order == null ? NotFound() : View(new OrderDetailsPage { Order = order });
    }

    [HttpGet]
    public async Task<IActionResult> Checkout()
    {
        if (User.PersonId() is not int id) return Forbid();
        var page = await orders.CheckoutPageAsync(id);
        if (!page.Cart.CartItems.Any() && !page.Cart.CustomItems.Any()) return RedirectToAction("Index", "Cart");
        return View(page);
    }

    [HttpPost]
    public async Task<IActionResult> Checkout(CheckoutRequest request)
    {
        if (User.PersonId() is not int id) return Forbid();
        if (ModelState.IsValid)
        {
            try
            {
                var orderId = await orders.CheckoutAsync(id, request);
                TempData["OrderSuccess"] = $"Замовлення #{orderId} успішно оформлено. Воно вже доступне в історії замовлень.";
                return RedirectToAction(nameof(Details), new { id = orderId });
            }
            catch (Exception ex) when (ex is OrderRuleException or ValidationException)
            {
                ModelState.AddModelError("", ex.Message);
            }
        }
        ModelState.Remove("Request.ExpectedTotal");
        ModelState.Remove("ExpectedTotal");
        ModelState.Remove("Request.CartFingerprint");
        ModelState.Remove("CartFingerprint");
        if (request.CheckoutToken == Guid.Empty)
        {
            ModelState.Remove("Request.CheckoutToken");
            ModelState.Remove("CheckoutToken");
        }
        return View(nameof(Checkout), await orders.CheckoutPageAsync(id, request));
    }

    [HttpPost]
    public Task<IActionResult> SubmitOrder(CheckoutRequest request) => Checkout(request);

    [HttpPost]
    public async Task<IActionResult> Cancel(int id, OrderChangeRequest request)
    {
        if (User.PersonId() is not int personId) return Forbid();
        // Never accept a requested status from the customer.
        request.StatusCode = OrderWorkflow.Cancelled;
        ModelState.Remove("StatusCode");
        if (!ModelState.IsValid) TempData["OrderError"] = "Перевірте дані форми.";
        else
        {
            try
            {
                if (!await orders.ChangeStatusAsync(id, personId, false, request)) return NotFound();
            }
            catch (Exception ex) when (ex is OrderRuleException or ValidationException)
            { TempData["OrderError"] = ex.Message; }
        }
        return RedirectToAction(nameof(Details), new { id });
    }
}
