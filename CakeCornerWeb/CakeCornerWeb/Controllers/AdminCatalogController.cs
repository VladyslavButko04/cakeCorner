using System.ComponentModel.DataAnnotations;
using CakeCornerWeb.Data;
using CakeCornerWeb.Models;
using CakeCornerWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CakeCornerWeb.Controllers;

[Authorize(Policy = "AdminOnly")]
[AutoValidateAntiforgeryToken]
public sealed class AdminCatalogController(ApplicationDbContext context, CatalogAdminService catalog) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(new CatalogDashboard
    {
        Cakes = await context.Cakes.CountAsync(c => c.IsActive),
        Ingredients = await context.Ingredients.CountAsync(i => i.IsActive)
    });

    [HttpGet]
    public async Task<IActionResult> Options() => View(await OptionsPageAsync(new()));

    [HttpPost]
    public async Task<IActionResult> Options(CatalogLookupRequest request, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await catalog.AddLookupAsync(request, ct);
                TempData["CatalogSuccess"] = "Довідник оновлено.";
                return RedirectToAction(nameof(Options));
            }
            catch (Exception ex) when (ex is CatalogRuleException or ValidationException)
            { ModelState.AddModelError("", ex.Message); }
        }
        return View(await OptionsPageAsync(request));
    }

    private async Task<CatalogLookupPage> OptionsPageAsync(CatalogLookupRequest request) => new()
    {
        Request = request,
        Categories = await context.Categories.AsNoTracking().OrderBy(c => c.CategoryName).ToListAsync(),
        Flavors = await context.Flavors.AsNoTracking().OrderBy(f => f.FlavorName).ToListAsync(),
        Weights = await context.WeightOptions.AsNoTracking().OrderBy(w => w.Weight).ToListAsync()
    };
}