using System.Security.Cryptography;
using CakeCornerWeb.Extensions;
using CakeCornerWeb.Recommendations;
using CakeCornerWeb.Recommendations.Core;
using CakeCornerWeb.Recommendations.Persistence;
using CakeCornerWeb.Recommendations.Runtime;
using CakeCornerWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace CakeCornerWeb.Controllers;

[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RecommendationsController(RecommendationService recommendations, RecommendationStore store,
    RecommendationMethod method, RecommendationEngine engine, IOptions<RecommendationRuntimeOptions> options,
    IDataProtectionProvider protection) : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet, EnableRateLimiting("recommendations")]
    public async Task<IActionResult> State(CancellationToken ct)
    {
        if (!options.Value.Enabled) return Json(new { available = false, statusMessage = "Підбір тимчасово вимкнено." });
        try
        {
            var status = await store.GetStatusAsync(ct);
            // Loading metadata validates the configured immutable definition and installed schema too.
            if (status.Installed)
            {
                await store.LoadSnapshotAsync(options.Value.AnnotationVersion, method, "Demo", ct);
                return Json(new { available = true, annotationVersion = options.Value.AnnotationVersion, methodVersion = method.Version });
            }
            return Json(new { available = false, statusMessage = "Підбір ще не налаштовано. Каталог і конструктор доступні." });
        }
        catch (RecommendationUnavailableException ex) { return Json(new { available = false, statusMessage = ex.Message }); }
    }

    [HttpPost, RequestSizeLimit(32 * 1024), EnableRateLimiting("recommendations")]
    public async Task<IActionResult> Query([FromBody] RecommendationQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid || query == null) return BadRequest(new { status = "invalid_request", message = "Перевірте параметри підбору." });
        try { return Json(await recommendations.QueryAsync(User.PersonId(), SessionId(), query, ct)); }
        catch (RecommendationValidationException ex) { return BadRequest(new { status = "invalid_request", message = ex.Message }); }
        catch (RecommendationUnavailableException ex) { return StatusCode(503, new { status = "unavailable", message = ex.Message }); }
    }

    [Authorize, HttpGet]
    public async Task<IActionResult> Preferences(CancellationToken ct)
    {
        if (User.PersonId() is not int personId) return Forbid();
        try { return Json(await store.LoadPreferencesAsync(personId, ct)); }
        catch (RecommendationUnavailableException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [Authorize, HttpPost, RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Preferences([FromBody] RecommendationPreferences preferences, CancellationToken ct)
    {
        if (User.PersonId() is not int personId) return Forbid();
        if (!ModelState.IsValid || preferences == null) return BadRequest();
        try
        {
            engine.ValidateRequest(new RecommendationQuery { PreferredFlavors = preferences.PreferredFlavors,
                DesiredSweetness = preferences.DesiredSweetness, ExcludedAllergens = preferences.ExcludedAllergens });
            await store.SavePreferencesAsync(personId, preferences, ct);
            return Json(new { saved = true });
        }
        catch (RecommendationValidationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (RecommendationUnavailableException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [HttpPost, RequestSizeLimit(8 * 1024), EnableRateLimiting("recommendations")]
    public async Task<IActionResult> Event([FromBody] RecommendationEventRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request == null || request.EventKey == Guid.Empty ||
            request.EventType is not ("impression" or "detail_view" or "explicit_rating"))
            return BadRequest(new { message = "Неприпустима подія." });
        try
        {
            await store.RecordEventAsync(User.PersonId(), SessionId(), request, ct);
            return Ok();
        }
        catch (RecommendationValidationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (RecommendationUnavailableException ex) { return StatusCode(503, new { message = ex.Message }); }
    }

    [Authorize, HttpPost, RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Add(RecommendationAddRequest request, CancellationToken ct)
    {
        if (User.PersonId() is not int personId) return Forbid();
        if (!ModelState.IsValid) return BadRequest();
        try
        {
            await recommendations.AddAsync(personId, request, ct);
            TempData["CartSuccess"] = "Обраний торт додано до кошика.";
        }
        catch (Exception ex) when (ex is RecommendationValidationException or RecommendationUnavailableException or OrderRuleException or System.ComponentModel.DataAnnotations.ValidationException)
        { TempData["CartError"] = ex.Message; }
        return RedirectToAction("Index", "Cart");
    }

    private Guid? SessionId()
    {
        if (User.PersonId() != null) return null;
        const string cookieName = "CakeCorner.RecommendationSession";
        var protector = protection.CreateProtector("CakeCorner.Recommendations.Anonymous.v1");
        if (Request.Cookies.TryGetValue(cookieName, out var value))
        {
            try { if (Guid.TryParse(protector.Unprotect(value), out var id) && id != Guid.Empty) return id; }
            catch (CryptographicException) { /* Expired/invalid identity creates a new independent session. */ }
        }
        var session = Guid.NewGuid();
        Response.Cookies.Append(cookieName, protector.Protect(session.ToString()), new CookieOptions
        { HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, IsEssential = true, MaxAge = TimeSpan.FromDays(7) });
        return session;
    }
}
