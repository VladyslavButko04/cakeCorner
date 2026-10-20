using CakeCornerWeb.Models.Entities;
using System.ComponentModel.DataAnnotations;
using System.Reflection.Metadata;

namespace CakeCornerWeb.Models;

public class CheckoutRequest
{
    public Guid CheckoutToken { get; set; }
    [Required, StringLength(64, MinimumLength = 64)]
    public string CartFingerprint { get; set; } = "";
    public decimal ExpectedTotal { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Оберіть пункт видачі.")]
    public int PickupLocationId { get; set; }
    [Required, Phone, StringLength(20)]
    public string PhoneNumber { get; set; } = "";
    [DataType(DataType.DateTime)]
    public DateTime DeliveryDateTime { get; set; }
    [StringLength(300)]
    public string? Comment { get; set; }
    public bool RequiredCallback { get; set; }
}

public class CheckoutPage
{
    public CheckoutRequest Request { get; set; } = new();
    public Cart Cart { get; set; } = null!;
    public List<PickupLocation> Locations { get; set; } = [];
    public bool HasUnavailableItems => Cart.CartItems.Any(i => !i.CakeWeight.IsActive || !i.CakeWeight.Cake.IsActive)
        || Cart.CustomItems.Any(i => !CakeCornerWeb.Services.CatalogAvailability.IsAvailable(i.CustomCake));
    public decimal Total => Cart.CartItems.Sum(i => i.Price * i.Quantity) + Cart.CustomItems.Sum(i => i.UnitPrice * i.Quantity);
}

public class OrderListPage
{
    public List<PersonOrder> Orders { get; set; } = [];
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public bool History { get; set; }
    public string Scope { get; set; } = "all";
    public bool IsAdmin { get; set; }
    public string? Status { get; set; }
}

public class OrderDetailsPage
{
    public PersonOrder Order { get; set; } = null!;
    public bool IsAdmin { get; set; }
}

public class OrderChangeRequest
{
    [Required]
    public string RowVersion { get; set; } = "";
    [Required]
    public string StatusCode { get; set; } = "";
    [StringLength(500)]
    public string? Reason { get; set; }
}

public class CustomCakeRequest
{
    [Range(1, int.MaxValue)] public int BiscuitId { get; set; }
    [Range(1, int.MaxValue)] public int FillingId { get; set; }
    [Range(1, int.MaxValue)] public int CreamId { get; set; }
    [Range(1, int.MaxValue)] public int MousseId { get; set; }
    [Range(typeof(decimal), "0.5", "10.0")]
    public decimal Weight { get; set; } = 1.5m;
    [Range(1, CartLimits.MaxQuantity)] public int Quantity { get; set; } = 1;
    [StringLength(50)] public string? Inscription { get; set; }
    [Required, RegularExpression("^(Round|Square|Heart)$", ErrorMessage = "Оберіть форму торта зі списку.")]
    public string Shape { get; set; } = "Round";
    [Range(1, 50)] public int? GuestCount { get; set; }
    [StringLength(500)] public string? DesignNotes { get; set; }
}

public class ConstructorPage
{
    public CustomCakeRequest Request { get; set; } = new();
    public List<Ingredient> Ingredients { get; set; } = [];
}