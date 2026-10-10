namespace CakeCornerWeb.Models.Entities
{
    public class WeightOption
    {
        public int WeightOption_ID { get; set; }
        public decimal Weight { get; set; }

        public ICollection<CakeWeight> CakeWeights { get; set; }
    }
}
