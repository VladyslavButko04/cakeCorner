namespace CakeCornerWeb.Models.Entities
{
    public class CartItem
    {
        public int CartItem_ID { get; set; }
        public int Cart_ID { get; set; }
        public int CakeWeight_ID { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public DateTime CreatedAt { get; set; }
        public Cart Cart { get; set; } = null!;
        public CakeWeight CakeWeight { get; set; } = null!;
    }
}
