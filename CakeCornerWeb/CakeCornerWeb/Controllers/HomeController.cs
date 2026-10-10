using CakecornerWebsite.Interfaces;
using Microsoft.AspNetCore.Mvc;

public class HomeController : Controller
{
    private readonly ICakeRepository _cakeRepository;

    public HomeController(ICakeRepository cakeRepository)
    {
        _cakeRepository = cakeRepository;
    }

    public async Task<IActionResult> Index()
    {
        var cakes = await _cakeRepository.GetAllCakesWithMinPriceAsync();
        return View(cakes);
    }
}