namespace CakeCornerWeb.Models.Entities
{
    public class CakeWeight
    {
        public int CakeWeight_ID { get; set; }

        public int Cake_ID { get; set; }
        public Cake Cake { get; set; }

        public int WeightOption_ID { get; set; }
        public WeightOption WeightOption { get; set; }
        public decimal Price { get; set; }
    }
}
