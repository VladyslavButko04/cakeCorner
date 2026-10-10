namespace CakecornerWebsite.Controllers;

using CakecornerWebsite.Interfaces;
using CakecornerWebsite.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

public class CakeController : Controller
{
    private readonly ICakeRepository _cakeRepository;

    public CakeController(ICakeRepository cakeRepository)
    {
        _cakeRepository = cakeRepository;
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
                Weight = cw.WeightOption.Weight,
                Price = cw.Price
            }).ToList()
        };

        return View(viewModel);
    }
}