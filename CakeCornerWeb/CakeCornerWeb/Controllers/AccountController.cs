using CakeCornerWeb.Interfaces;
using Microsoft.AspNetCore.Mvc;
using CakeCornerWeb.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using CakeCornerWeb.Extensions;
using Microsoft.Extensions.Options;
using CakeCornerWeb.Services.Security;
using Microsoft.EntityFrameworkCore;
using CakeCornerWeb.Data;

public class AccountController : Controller
{
    private readonly IPersonService _personService;
    private readonly ApplicationDbContext _context;
    private readonly JwtOptions _jwtOptions;

    public AccountController(
        IPersonService personService,
        ApplicationDbContext context,
        IOptions<JwtOptions> jwtOptions)    
    {
        _personService = personService;
        _context = context;
        _jwtOptions = jwtOptions.Value;
    }



    // REGISTER
    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var clientRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Client");
        if (clientRole == null)
        {
            ModelState.AddModelError("", "Реєстрація тимчасово недоступнаю.");
            return View(model);
        }
        int defaultRoleId = clientRole.Role_ID;

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
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return View();
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
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
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddHours(_jwtOptions.ExpiresHours)
        });

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Home");
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
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
        var personId = User.PersonId();
        if (personId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var person = await _context.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.Person_ID == personId);
        if (person == null)
        {
            return NotFound("Користувач не знайдений.");
        }

        return View(person);
    }

}
