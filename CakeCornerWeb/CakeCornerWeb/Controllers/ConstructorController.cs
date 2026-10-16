using Microsoft.AspNetCore.Mvc;

namespace CakeCornerWeb.Controllers
{
    public class ConstructorController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }
    }
}