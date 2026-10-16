using Microsoft.AspNetCore.Mvc;

namespace CakeCornerWeb.Controllers
{
    public class AuthController : Controller
    {
        public IActionResult Register()
        {
            return RedirectToAction("Register", "Account");
        }

        public IActionResult Login()
        {
            return RedirectToAction("Login", "Account");
        }
    }
}