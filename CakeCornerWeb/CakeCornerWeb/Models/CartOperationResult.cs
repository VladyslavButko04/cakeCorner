namespace CakeCornerWeb.Models
{
    public enum CartOperationResult
    {
        Success,
        NotFound,
        InvalidQuantity
    }

    public static class CartLimits
    {
        public const int MaxQuantity = 99;
    }
}