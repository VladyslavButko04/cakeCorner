using System.ComponentModel.DataAnnotations;

namespace CakeCornerWeb.Models;

public class AddToCartRequest
{
    [Range(1, int.MaxValue)]
    public int CakeWeightId { get; set; }
    [Range(1, CartLimits.MaxQuantity, ErrorMessage = "Quantity must be between 1 and 99.")]
    public int Quantity { get; set; } = 1;
}

public class UpdateCartItemRequest
{
    [Range(1, int.MaxValue)]
    public int CartItemId { get; set; }
    [Range(1, CartLimits.MaxQuantity, ErrorMessage = "Quantity must be between 1 and 99.")]
    public int Quantity { get; set; }
}