namespace CakeCornerWeb.Models.Entities;

public class PersonOrder
{
    public int Order_ID { get; set; }
    public int Person_ID { get; set; }
    public int OrderStatus_ID { get; set; }
    public int PickupLocation_ID { get; set; }
    public decimal TotalPrice { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime DeliveryDateTime { get; set; }
    public string? Comment { get; set; }
    public string PhoneNumber { get; set; } = "";
    public bool RequiredCallback { get; set; }
    public Guid? CheckoutToken { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Person Person { get; set; } = null!;
    public OrderStatus Status { get; set; } = null!;
    public PickupLocation PickupLocation { get; set; } = null!;
    public Payment? Payment { get; set; }
    public ICollection<OrderItem> Items { get; set; } = [];
    public ICollection<CustomCakeCart> CustomItems { get; set; } = [];
    public ICollection<OrderStatusHistory> History { get; set; } = [];
}

public class OrderItem
{
    public int OrderItem_ID { get; set; }
    public int Order_ID { get; set; }
    public int CakeWeight_ID { get; set; }
    public int Quantity { get; set; }
    public decimal ItemPrice { get; set; }
    public string? CakeNameSnapshot { get; set; }
    public decimal? WeightSnapshot { get; set; }
    public PersonOrder Order { get; set; } = null!;
    public CakeWeight CakeWeight { get; set; } = null!;
}

public class OrderStatus
{
    public int OrderStatus_ID { get; set; }
    public string OrderStatusName { get; set; } = "";
    public string? Code { get; set; }
}

public class OrderStatusHistory
{
    public int Id { get; set; }
    public int Order_ID { get; set; }
    public string? FromCode { get; set; }
    public string ToCode { get; set; } = "";
    public DateTime ChangedAt { get; set; }
    public int? ChangedByPerson_ID { get; set; }
    public string? Note { get; set; }
    public PersonOrder Order { get; set; } = null!;
}

public class Payment
{
    public int Payment_ID { get; set; }
    public int PersonOrder_ID { get; set; }
    public decimal OnlineAmount { get; set; }
    public bool? IsOnlinePaid { get; set; }
    public DateTime? OnlinePaidAt { get; set; }
    public decimal RemaininAmount { get; set; }
    public bool? IsFinalPaid { get; set; }
    public DateTime? FinalPaidAt { get; set; }
    public decimal PaidAmount { get; set; }
    public PersonOrder Order { get; set; } = null!;
}

public class City
{
    public int City_ID { get; set; }
    public string CityName { get; set; } = "";
}

public class PickupLocation
{
    public int PickupLocation_ID { get; set; }
    public int City_ID { get; set; }
    public string Address { get; set; } = "";
    public string? PhoneNumber { get; set; }
    public string? Notes { get; set; }
    public City City { get; set; } = null!;
}
