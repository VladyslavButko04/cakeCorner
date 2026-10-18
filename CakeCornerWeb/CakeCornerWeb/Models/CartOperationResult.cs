namespace CakeCornerWeb.Models
{
    public enum CartOperationResult
    {
        Success,
        NotFound,
        InvalidQuantity,
        Unavailable
    }

    public static class CartLimits
    {
        public const int MaxQuantity = 99;
    }
}