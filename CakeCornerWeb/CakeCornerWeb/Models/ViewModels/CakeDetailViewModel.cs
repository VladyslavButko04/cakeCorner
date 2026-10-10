public class CakeDetailViewModel
{
    public int CakeId { get; set; }
    public string CakeName { get; set; } = string.Empty;
    public string DescriptionLong { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public List<WeightPrice> Weights { get; set; } = new();
    public decimal MinPrice => Weights.Min(w => w.Price);

    public class WeightPrice
    {
        public decimal Weight { get; set; }
        public decimal Price { get; set; }
    }
}