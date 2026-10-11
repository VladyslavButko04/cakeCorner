namespace CakeCornerWeb.Models.Entities
{
    public class Cart
    {
        public int Cart_ID { get; set; }
        public int Person_ID { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Person Person { get; set; } = null!;
        public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
        public static Cart Create(int personId)
        {
            return new Cart
            {
                Person_ID = personId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = null
            };
        }
    }
}
