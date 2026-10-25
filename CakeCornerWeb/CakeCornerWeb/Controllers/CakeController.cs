namespace CakeCornerWeb.Controllers;
using CakeCornerWeb.Data;
using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Models;
using CakeCornerWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

public class CakeController : Controller
{
    private readonly ICakeRepository _cakeRepository;
    private readonly ApplicationDbContext _context;

    public CakeController(ICakeRepository cakeRepository, ApplicationDbContext context)
    {
        _cakeRepository = cakeRepository;
        _context = context;
    }

    public Task<IActionResult> Index(int? categoryId, int? flavorId, int page = 1, CancellationToken ct = default)
        => CatalogAsync(categoryId, flavorId, null, page, ct);

    private async Task<IActionResult> CatalogAsync(int? categoryId, int? flavorId, string? search, int page, CancellationToken ct)
    {
        var model = await _cakeRepository.GetCatalogPageAsync(categoryId, flavorId, search, page, ct);
        var filterModel = model.Filters;
        filterModel.Categories.Add(new SelectListItem { Value = "", Text = "Усі категорії" });
        filterModel.Flavors.Add(new SelectListItem { Value = "", Text = "Усі смаки" });

        var categories = await _cakeRepository.GetAllCategoriesAsync();
        filterModel.Categories.AddRange(categories.Select(c => new SelectListItem
        {
            Value = c.Category_ID.ToString(),
            Text = c.CategoryName
        }));

        var flavors = await _cakeRepository.GetAllFlavorsAsync();
        filterModel.Flavors.AddRange(flavors.Select(f => new SelectListItem
        {
            Value = f.Flavor_ID.ToString(),
            Text = f.FlavorName
        }));

        return View("Index", model);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> LiveSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            return Json(new List<object>());
        }

        var searchTerm = query.ToLower();

        var cakes = await _context.Cakes
            .Where(c => c.CakeName.ToLower().Contains(searchTerm))
            .Select(c => new
            {
                id = c.Cake_ID,
                name = c.CakeName,
                image = c.ImagePath
            })
            .Take(5)
            .ToListAsync();

        return Json(cakes);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Search(string? query, int? categoryId, int? flavorId, int page = 1, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return RedirectToAction(nameof(Index), new { categoryId, flavorId });
        }

        return await CatalogAsync(categoryId, flavorId, query, page, ct);
    }

    public async Task<IActionResult> Details(int id)
    {
        var cake = await _cakeRepository.GetCakeByIdAsync(id);
        if (cake == null) return NotFound();

        var viewModel = new CakeDetailViewModel
        {
            CakeId = cake.Cake_ID,
            CakeName = cake.CakeName,
            DescriptionLong = cake.DescriptionLong,
            ImagePath = cake.ImagePath,
            AdditionalInformation = CakeAdditionalInformationViewModel.FromCake(cake),
            FlavorNames = cake.CakeFlavors.OrderBy(cf => cf.Flavor.FlavorName).Select(cf => cf.Flavor.FlavorName).Distinct().ToList(),
            Weights = cake.CakeWeights.Select(cw => new CakeDetailViewModel.WeightPrice
            {
                CakeWeightId = cw.CakeWeight_ID,
                Weight = cw.WeightOption.Weight,
                Price = cw.Price
            }).ToList()
        };

        return View(viewModel);
    }
}
