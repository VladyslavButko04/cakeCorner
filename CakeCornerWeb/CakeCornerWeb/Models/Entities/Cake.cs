namespace CakeCornerWeb.Models.Entities
{
    public class Cake
    {
        public int Cake_ID { get; set; }
        public int Category_ID { get; set; }
        public int Flavor_ID { get; set; }

        public string CakeName { get; set; } = string.Empty;
        public string DescriptionLong { get; set; } = string.Empty;
        public string DescriptionShort { get; set; } = string.Empty;

        public string ImagePath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public Category Category { get; set; } = null!;
        public Flavor Flavor { get; set; } = null!;
        public ICollection<CakeWeight> CakeWeights { get; set; } = new List<CakeWeight>();
    }
}
