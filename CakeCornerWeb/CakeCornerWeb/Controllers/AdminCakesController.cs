using System.ComponentModel.DataAnnotations;
using CakeCornerWeb.Data;
using CakeCornerWeb.Models;
using CakeCornerWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace CakeCornerWeb.Controllers;

[Authorize(Policy = "AdminOnly")]
[AutoValidateAntiforgeryToken]
[RequestSizeLimit(8 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 8 * 1024 * 1024)]
public sealed class AdminCakesController(ApplicationDbContext context, CatalogAdminService catalog,
    ILogger<AdminCakesController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? query, bool archived = false, int page = 1)
    {
        query = query?.Trim();
        if (query?.Length > 100) query = query[..100];
        var items = context.Cakes.AsNoTracking().Where(c => c.IsActive != archived)
            .Include(c => c.Category).Include(c => c.Flavor).Include(c => c.CakeWeights).AsQueryable();
        if (!string.IsNullOrWhiteSpace(query)) items = items.Where(c => c.CakeName.Contains(query));
        var pages = (int)Math.Ceiling(await items.CountAsync() / 20m);
        page = Math.Clamp(page, 1, Math.Max(1, pages));
        return View(new CatalogListPage<Models.Entities.Cake>
        {
            Items = await items.OrderByDescending(c => c.Cake_ID).Skip((page - 1) * 20).Take(20).ToListAsync(),
            Query = query,
            Archived = archived,
            Page = page,
            TotalPages = pages
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create() => View("Edit", await PageAsync(null, new()));

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var cake = await context.Cakes.AsNoTracking().Include(c => c.CakeWeights)
            .SingleOrDefaultAsync(c => c.Cake_ID == id && c.IsActive);
        if (cake == null) return NotFound();
        return View(await PageAsync(id, new CakeEditorRequest
        {
            Name = cake.CakeName,
            CategoryId = cake.Category_ID,
            FlavorId = cake.Flavor_ID,
            ShortDescription = cake.DescriptionShort,
            Description = cake.DescriptionLong,
            RowVersion = Convert.ToBase64String(cake.RowVersion),
            Variants = cake.CakeWeights.Where(v => v.IsActive).Select(v => new CakeVariantInput
            { Enabled = true, WeightOptionId = v.WeightOption_ID, Price = v.Price }).ToList()
        }));
    }

    [HttpPost]
    public Task<IActionResult> Create(CakeEditorRequest request, CancellationToken ct) => SaveAsync(null, request, ct);

    [HttpPost]
    public Task<IActionResult> Edit(int id, CakeEditorRequest request, CancellationToken ct) => SaveAsync(id, request, ct);

    private async Task<IActionResult> SaveAsync(int? id, CakeEditorRequest request, CancellationToken ct)
    {
        if (id != null && !await context.Cakes.AsNoTracking().AnyAsync(c => c.Cake_ID == id, ct)) return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                await catalog.SaveCakeAsync(id, request, ct);
                TempData["CatalogSuccess"] = id == null ? "Торт додано до каталогу." : "Торт оновлено.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (ex is CatalogRuleException or ValidationException)
            { ModelState.AddModelError("", ex.Message); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException)
            {
                logger.LogError(ex, "Could not save a catalog cake.");
                ModelState.AddModelError("", "Не вдалося зберегти торт. Перевірте доступ до бази та каталогу зображень.");
            }
        }
        return View("Edit", await PageAsync(id, request));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var cake = await context.Cakes.AsNoTracking().SingleOrDefaultAsync(c => c.Cake_ID == id && c.IsActive);
        return cake == null ? NotFound() : View("~/Views/Shared/CatalogDelete.cshtml", new CatalogDeletePage
        { Id = id, Name = cake.CakeName, Controller = "AdminCakes", RowVersion = Convert.ToBase64String(cake.RowVersion) });
    }

    [HttpPost]
    public Task<IActionResult> Delete(int id, CatalogVisibilityRequest request, CancellationToken ct) => SetActiveAsync(id, request, false, ct);

    [HttpPost]
    public Task<IActionResult> Restore(int id, CatalogVisibilityRequest request, CancellationToken ct) => SetActiveAsync(id, request, true, ct);

    private async Task<IActionResult> SetActiveAsync(int id, CatalogVisibilityRequest request, bool active, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest();
        try
        {
            await catalog.SetCakeActiveAsync(id, request.RowVersion, active, ct);
            TempData["CatalogSuccess"] = active ? "Торт відновлено." : "Торт видалено з каталогу та збережено в архіві.";
        }
        catch (CatalogRuleException ex) { TempData["CatalogError"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { archived = !active });
    }

    private async Task<CakeEditorPage> PageAsync(int? id, CakeEditorRequest request)
    {
        var weights = await context.WeightOptions.AsNoTracking().OrderBy(w => w.Weight).ToListAsync();
        var submitted = (request.Variants ?? []).GroupBy(v => v.WeightOptionId).ToDictionary(g => g.Key, g => g.First());
        // Reindex server-approved options without reusing posted hidden IDs from ModelState.
        var variantKeys = ModelState.Keys.Where(k => k.StartsWith("Request.Variants", StringComparison.OrdinalIgnoreCase)).ToArray();
        var variantErrors = variantKeys.Any(k => ModelState[k]!.Errors.Count > 0);
        foreach (var key in variantKeys) ModelState.Remove(key);
        if (variantErrors) ModelState.AddModelError("", "Перевірте вагові варіанти та введіть коректні ціни.");
        // Render only real options; crafted/removed IDs are never added to the form.
        request.Variants = weights.Select(w => submitted.TryGetValue(w.WeightOption_ID, out var value) ? value :
            new CakeVariantInput { WeightOptionId = w.WeightOption_ID }).ToList();
        return new CakeEditorPage
        {
            Id = id,
            Request = request,
            Weights = weights,
            ImagePath = id == null ? null : await context.Cakes.AsNoTracking().Where(c => c.Cake_ID == id).Select(c => c.ImagePath).FirstOrDefaultAsync(),
            Categories = await context.Categories.OrderBy(c => c.CategoryName).Select(c => new SelectListItem(c.CategoryName, c.Category_ID.ToString())).ToListAsync(),
            Flavors = await context.Flavors.OrderBy(f => f.FlavorName).Select(f => new SelectListItem(f.FlavorName, f.Flavor_ID.ToString())).ToListAsync()
        };
    }
}
