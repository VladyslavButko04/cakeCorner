using System.ComponentModel.DataAnnotations;
using CakeCornerWeb.Data;
using CakeCornerWeb.Extensions;
using CakeCornerWeb.Models;
using CakeCornerWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CakeCornerWeb.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
public class ConstructorController(ApplicationDbContext context, CustomCakeService custom) : Controller
{
    private async Task<ConstructorPage> PageAsync(CustomCakeRequest? request = null) => new()
    {
        Request = request ?? new() { GuestCount = 10, Weight = 2m },
        Ingredients = await context.Ingredients.AsNoTracking().Include(i => i.Type)
            .Where(i => i.IngredientName != null && i.Price >= 0).OrderBy(i => i.IngredientName).ToListAsync()
    };

    [HttpGet]
    public async Task<IActionResult> Index() => View(await PageAsync());

    [HttpPost]
    public async Task<IActionResult> Add(CustomCakeRequest request)
    {
        if (User.PersonId() is not int personId) return Forbid();
        if (ModelState.IsValid)
        {
            try
            {
                await custom.AddAsync(personId, request);
                TempData["CartSuccess"] = "Кастомний торт додано до кошика.";
                return RedirectToAction("Index", "Cart");
            }
            catch (Exception ex) when (ex is OrderRuleException or ValidationException)
            { ModelState.AddModelError("", ex.Message); }
        }
        return View("Index", await PageAsync(request));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateQuantity(UpdateCartItemRequest request)
    {
        if (User.PersonId() is not int personId) return Forbid();
        if (!ModelState.IsValid) return BadRequest();
        try
        {
            if (!await custom.ChangeAsync(personId, request.CartItemId, request.Quantity)) return NotFound();
        }
        catch (OrderRuleException ex) { TempData["CartError"] = ex.Message; }
        return RedirectToAction("Index", "Cart");
    }

    [HttpPost]
    public async Task<IActionResult> Remove(int cartItemId)
    {
        if (User.PersonId() is not int personId) return Forbid();
        if (!await custom.ChangeAsync(personId, cartItemId, null)) return NotFound();
        return RedirectToAction("Index", "Cart");
    }
}
