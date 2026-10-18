using System.Security.Claims;
using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CakeCornerWeb.Controllers
{
    [Authorize]
    [AutoValidateAntiforgeryToken]
    public class CartController : Controller
    {
        private readonly ICartService _cartService;

        public CartController(ICartService cartService)
        {
            _cartService = cartService;
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst("Person_ID")?.Value
                        ?? User.FindFirst("person_ID")?.Value
                        ?? User.FindFirst("sub")?.Value;

            if (int.TryParse(userIdClaim, out int userId) && userId > 0)
            {
                return userId;
            }

            return null;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Index()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var cart = await _cartService.GetOrCreateCartAsync(userId.Value);
            return View(cart);
        }

        [HttpPost]
        public async Task<IActionResult> Add(AddToCartRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            if (!ModelState.IsValid)
                return InvalidQuantity();


            var result = await _cartService.AddToCartAsync(userId.Value, request.CakeWeightId, request.Quantity);
            return OperationResult(result);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateQuantity(UpdateCartItemRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            if (!ModelState.IsValid)
                return InvalidQuantity();

            var result = await _cartService.UpdateQuantityAsync(userId.Value, request.CartItemId, request.Quantity);
            return OperationResult(result);
        }

        [HttpPost]
        public async Task<IActionResult> Clear()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            await _cartService.ClearCartAsync(userId.Value);
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> Remove(int cartItemId)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }
            if (!ModelState.IsValid || cartItemId <= 0)
            {
                return BadRequest();
            }

            var result = await _cartService.RemoveFromCartAsync(userId.Value, cartItemId);
            return OperationResult(result);
        }

        private IActionResult OperationResult(CartOperationResult result) => result switch
        {
            CartOperationResult.Success => RedirectToAction("Index"),
            CartOperationResult.NotFound => NotFound(),
            CartOperationResult.InvalidQuantity => InvalidQuantity(),
            CartOperationResult.Unavailable => Unavailable(),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };

        private IActionResult Unavailable()
        {
            TempData["CartError"] = "Цей торт або ваговий варіант більше недоступний. Видаліть його з кошика.";
            return RedirectToAction(nameof(Index));
        }

        private IActionResult InvalidQuantity()
        {
            TempData["CartError"] = $"Кількість має бути від 1 до {CartLimits.MaxQuantity}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
