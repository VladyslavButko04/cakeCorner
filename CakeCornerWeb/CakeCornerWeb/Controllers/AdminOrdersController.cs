using System.ComponentModel.DataAnnotations;
using CakeCornerWeb.Extensions;
using CakeCornerWeb.Models;
using CakeCornerWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CakeCornerWeb.Controllers;

[Authorize(Policy = "AdminOnly")]
[AutoValidateAntiforgeryToken]
public class AdminOrdersController(OrderService orders) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? status, int page = 1) =>
        View("~/Views/Orders/Index.cshtml", await orders.ListAsync(null, false, status, page));

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var order = await orders.DetailsQuery().AsNoTracking().SingleOrDefaultAsync(o => o.Order_ID == id);
        return order == null ? NotFound() : View("~/Views/Orders/Details.cshtml", new OrderDetailsPage { Order = order, IsAdmin = true });
    }

    [HttpPost]
    public async Task<IActionResult> ChangeStatus(int id, OrderChangeRequest request)
    {
        if (User.PersonId() is not int actorId) return Forbid();
        if (!ModelState.IsValid) TempData["OrderError"] = "Перевірте дані форми.";
        else
        {
            try
            {
                if (!await orders.ChangeStatusAsync(id, actorId, true, request)) return NotFound();
            }
            catch (Exception ex) when (ex is OrderRuleException or ValidationException)
            { TempData["OrderError"] = ex.Message; }
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> MarkPaid(int id, string rowVersion)
    {
        if (User.PersonId() is not int actorId) return Forbid();
        if (string.IsNullOrWhiteSpace(rowVersion)) return BadRequest();
        try
        {
            if (!await orders.MarkPaidAsync(id, actorId, rowVersion)) return NotFound();
        }
        catch (OrderRuleException ex) { TempData["OrderError"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { id });
    }
}
