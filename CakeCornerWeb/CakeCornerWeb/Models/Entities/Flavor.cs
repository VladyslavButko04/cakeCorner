namespace CakeCornerWeb.Models.Entities
{
    public class Flavor
    {
        public int Flavor_ID { get; set; }
        public string FlavorName { get; set; } = string.Empty;
        public ICollection<Cake> Cakes { get; set; } = new List<Cake>();
    }
}
