using CakeCornerWeb.Extensions;
using CakeCornerWeb.Recommendations;
using CakeCornerWeb.Recommendations.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CakeCornerWeb.Controllers;

[Authorize(Policy = "AdminOnly")]
[AutoValidateAntiforgeryToken]
public sealed class AdminRecommendationsController(RecommendationStore store, RecommendationMethod method) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await store.GetStatusAsync(ct));

    [HttpPost, RequestSizeLimit(12 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 12 * 1024 * 1024)]
    public async Task<IActionResult> Import(string annotationVersion, IFormFile? ingredients, IFormFile? compatibility,
        IFormFile? readyCakes, IFormFile? availability, CancellationToken ct)
    {
        if (User.PersonId() is not int personId) return Forbid();
        var uploads = new Dictionary<string, IFormFile?>
        {
            ["ingredient-features.csv"] = ingredients, ["compatibility.csv"] = compatibility,
            ["ready-cake-features.csv"] = readyCakes, ["ready-variant-availability.csv"] = availability
        };
        if (!ModelState.IsValid || uploads.Values.Any(f => f == null || f.Length == 0 || f.Length > 3 * 1024 * 1024))
        { TempData["CatalogError"] = "Завантажте всі чотири CSV-файли, кожен до 3 МБ."; return RedirectToAction(nameof(Index)); }
        var streams = uploads.ToDictionary(p => p.Key, p => p.Value!.OpenReadStream());
        try
        {
            var result = await store.ImportDemoAsync(personId, annotationVersion, streams, method, ct);
            TempData["CatalogSuccess"] = "Демонстраційні дані імпортовано. Версія залишається чернеткою; експертна перевірка не підтверджується автоматично.";
        }
        catch (Exception ex) when (ex is RecommendationValidationException or RecommendationUnavailableException or InvalidDataException)
        { TempData["CatalogError"] = ex.Message; }
        finally { foreach (var stream in streams.Values) await stream.DisposeAsync(); }
        return RedirectToAction(nameof(Index));
    }
}
