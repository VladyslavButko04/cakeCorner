using CakeCornerWeb.Interfaces;
using Microsoft.AspNetCore.Mvc;
using CakeCornerWeb.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

public class AccountController : Controller
{
    private readonly IPersonService _personService;
    private readonly ApplicationDbContext _context;

    public AccountController(
        IPersonService personService,
        ApplicationDbContext context)
    {
        _personService = personService;
        _context = context;
    }



    // REGISTER
    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        int defaultRoleId = 2;

        var result = await _personService.RegisterAsync(model, defaultRoleId);

        if (!result)
        {
            ModelState.AddModelError("", "Користувач з такою поштою вже існує.");
            return View(model);
        }

        return RedirectToAction("Login");
    }


    // LOGIN
    [HttpGet]
    public IActionResult Login()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            Console.WriteLine(">>> Model is not valid");
            return View(model);
        }

        var token = await _personService.LoginAsync(model.Email, model.Password);

        if (token == null)
        {
            ModelState.AddModelError("", "Невірна електронна пошта або пароль");
            return View(model);
        }

        HttpContext.Response.Cookies.Append("testy-cookies", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = false,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddHours(1)
        });

        return RedirectToAction("Index", "Home");
    }


    [HttpGet]
    public IActionResult Logout()
    {
        HttpContext.Response.Cookies.Delete("testy-cookies");
        return RedirectToAction("Index", "Home");
    }

    // PROFILE
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(email))
        {
            return RedirectToAction("Profile", "Account");
        }

        var person = await _personService.GetProfileAsync(email);
        if (person == null)
        {
            return NotFound("Користувач не знайдений.");
        }

        return View(person);
    }

}
