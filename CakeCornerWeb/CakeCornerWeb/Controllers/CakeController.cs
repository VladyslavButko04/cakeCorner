namespace CakeCornerWeb.Controllers;
using CakeCornerWeb.Data;
using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Models;
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

    public async Task<IActionResult> Index(int? categoryId, int? flavorId)
    {
        var cakes = await _cakeRepository.GetFilteredCakeAsync(categoryId, flavorId);

        var filterModel = new CakeFilterViewModel
        {
            CategoryId = categoryId,
            FlavorId = flavorId,
            Categories = new List<SelectListItem>
        {
            new SelectListItem { Value = "", Text = "Усі категорії" }
        },
            Flavors = new List<SelectListItem>
        {
            new SelectListItem { Value = "", Text = "Усі смаки" }
        }
        };

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

        ViewBag.FilterModel = filterModel;

        return View(cakes);
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
    public async Task<IActionResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return RedirectToAction("Index");
        }

        var searchTerm = query.ToLower();

        var searchResults = await _context.Cakes
            .Include(c => c.CakeWeights)
                .ThenInclude(cw => cw.WeightOption)
            .Where(c =>
                c.CakeName.ToLower().Contains(searchTerm) ||
                (c.DescriptionShort != null && c.DescriptionShort.ToLower().Contains(searchTerm)) ||
                (c.DescriptionLong != null && c.DescriptionLong.ToLower().Contains(searchTerm))
            )
            .ToListAsync();

        ViewData["SearchQuery"] = query;

        ViewBag.FilterModel = new CakeCornerWeb.Models.CakeFilterViewModel
        {
            Categories = await _context.Categories.Select(c => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = c.Category_ID.ToString(), Text = c.CategoryName }).ToListAsync(),
            Flavors = await _context.Flavors.Select(f => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = f.Flavor_ID.ToString(), Text = f.FlavorName }).ToListAsync()
        };

        return View("Index", searchResults);
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