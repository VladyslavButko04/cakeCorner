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
public sealed class AdminIngredientsController(ApplicationDbContext context, CatalogAdminService catalog,
    IngredientImageService images, ILogger<AdminIngredientsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? query, bool archived = false, int page = 1)
    {
        query = query?.Trim();
        if (query?.Length > 100) query = query[..100];
        var items = context.Ingredients.AsNoTracking().Include(i => i.Type).Where(i => i.IsActive != archived);
        if (!string.IsNullOrWhiteSpace(query)) items = items.Where(i => i.IngredientName != null && i.IngredientName.Contains(query));
        var pages = (int)Math.Ceiling(await items.CountAsync() / 20m);
        page = Math.Clamp(page, 1, Math.Max(1, pages));
        return View(new CatalogListPage<Models.Entities.Ingredient>
        {
            Items = await items.OrderByDescending(i => i.Ingredients_ID).Skip((page - 1) * 20).Take(20).ToListAsync(),
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
        var ingredient = await context.Ingredients.AsNoTracking().SingleOrDefaultAsync(i => i.Ingredients_ID == id && i.IsActive);
        if (ingredient == null) return NotFound();
        return View(await PageAsync(id, new IngredientEditorRequest
        {
            Name = ingredient.IngredientName ?? "",
            TypeId = ingredient.IngredientTypes_ID,
            Description = ingredient.IngredientDescription,
            Price = ingredient.Price,
            RowVersion = Convert.ToBase64String(ingredient.RowVersion)
        }));
    }

    [HttpPost]
    public Task<IActionResult> Create(IngredientEditorRequest request, CancellationToken ct) => SaveAsync(null, request, ct);

    [HttpPost]
    public Task<IActionResult> Edit(int id, IngredientEditorRequest request, CancellationToken ct) => SaveAsync(id, request, ct);

    private async Task<IActionResult> SaveAsync(int? id, IngredientEditorRequest request, CancellationToken ct)
    {
        if (id != null && !await context.Ingredients.AsNoTracking().AnyAsync(i => i.Ingredients_ID == id, ct)) return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                await catalog.SaveIngredientAsync(id, request, ct);
                TempData["CatalogSuccess"] = id == null ? "Інгредієнт додано до конструктора." : "Інгредієнт оновлено.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (ex is CatalogRuleException or ValidationException)
            { ModelState.AddModelError("", ex.Message); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException)
            {
                logger.LogError(ex, "Could not save a catalog ingredient.");
                ModelState.AddModelError("", "Не вдалося зберегти інгредієнт. Перевірте доступ до бази та каталогу зображень.");
            }
        }
        return View("Edit", await PageAsync(id, request));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var ingredient = await context.Ingredients.AsNoTracking().SingleOrDefaultAsync(i => i.Ingredients_ID == id && i.IsActive);
        return ingredient == null ? NotFound() : View("~/Views/Shared/CatalogDelete.cshtml", new CatalogDeletePage
        { Id = id, Name = ingredient.IngredientName ?? "Інгредієнт", Controller = "AdminIngredients", RowVersion = Convert.ToBase64String(ingredient.RowVersion) });
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
            await catalog.SetIngredientActiveAsync(id, request.RowVersion, active, ct);
            TempData["CatalogSuccess"] = active ? "Інгредієнт відновлено." : "Інгредієнт видалено з конструктора та збережено в архіві.";
        }
        catch (CatalogRuleException ex) { TempData["CatalogError"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { archived = !active });
    }

    private async Task<IngredientEditorPage> PageAsync(int? id, IngredientEditorRequest request)
    {
        var ingredient = id == null ? null : await context.Ingredients.AsNoTracking().Include(i => i.Type)
            .SingleOrDefaultAsync(i => i.Ingredients_ID == id);
        return new IngredientEditorPage
        {
            Id = id,
            Request = request,
            ImagePath = ingredient == null ? null : images.ForIngredient(ingredient, ingredient.Type.IngredientsTypesName),
            Types = await context.IngredientTypes.Where(t => CatalogAdminService.IngredientTypes.Contains(t.IngredientsTypesName))
                .OrderBy(t => t.IngredientsTypesName).Select(t => new SelectListItem(t.IngredientsTypesName, t.IngredientsTypes_ID.ToString())).ToListAsync()
        };
    }
}