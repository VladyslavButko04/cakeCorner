namespace CakeCornerWeb.Models.Entities
{
    public class Category
    {
        public int Category_ID { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public ICollection<Cake> Cakes { get; set; } = new List<Cake>();
    }
}
