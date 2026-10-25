public class CakeDetailViewModel
{
    public int CakeId { get; set; }
    public string CakeName { get; set; } = string.Empty;
    public string DescriptionLong { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public List<string> FlavorNames { get; set; } = new();
    public List<WeightPrice> Weights { get; set; } = new();
    public CakeCornerWeb.Models.CakeAdditionalInformationViewModel AdditionalInformation { get; set; } = new();
    public decimal MinPrice => Weights.Count == 0 ? 0 : Weights.Min(w => w.Price);

    public class WeightPrice
    {
        public int CakeWeightId { get; set; }
        public decimal Weight { get; set; }
        public decimal Price { get; set; }
    }
}
