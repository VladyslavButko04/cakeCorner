using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CakeCornerWeb.Controllers
{
    [Authorize]
    public class OrdersController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Checkout()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SubmitOrder(String? deliveryMethod)
        {
            TempData["SuccessMessage"] = "Order submitted successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}
