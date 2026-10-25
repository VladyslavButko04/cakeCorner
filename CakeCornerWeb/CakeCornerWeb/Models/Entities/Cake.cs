namespace CakeCornerWeb.Models.Entities
{
    public class Cake
    {
        public int Cake_ID { get; set; }
        public bool IsActive { get; set; } = true;
        public byte[] RowVersion { get; set; } = [];
        public int Category_ID { get; set; }

        public string CakeName { get; set; } = string.Empty;
        public string DescriptionLong { get; set; } = string.Empty;
        public string DescriptionShort { get; set; } = string.Empty;

        public string ImagePath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public Category Category { get; set; } = null!;
        public ICollection<CakeFlavor> CakeFlavors { get; set; } = new List<CakeFlavor>();
        public ICollection<CakeWeight> CakeWeights { get; set; } = new List<CakeWeight>();
        public CakeProductInformation? ProductInformation { get; set; }
        public ICollection<CakeAllergen> Allergens { get; set; } = new List<CakeAllergen>();
    }
}
