namespace CakeCornerWeb.Models.Entities;

public sealed class CakeFlavor
{
    public int Cake_ID { get; set; }
    public Cake Cake { get; set; } = null!;
    public int Flavor_ID { get; set; }
    public Flavor Flavor { get; set; } = null!;
}
