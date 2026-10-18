namespace CakeCornerWeb.Models.Entities;

public class IngredientType
{
    public int IngredientsTypes_ID { get; set; }
    public string IngredientsTypesName { get; set; } = "";
}

public class Ingredient
{
    public int Ingredients_ID { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    public int IngredientTypes_ID { get; set; }
    public string? IngredientName { get; set; }
    public decimal Price { get; set; }
    public string? IngredientDescription { get; set; }
    public string? IngredientImagePath { get; set; }
    public IngredientType Type { get; set; } = null!;
}

public class CustomCake
{
    public int CustomCake_ID { get; set; }
    public int Person_ID { get; set; }
    public int Biscuit_ID { get; set; }
    public int Filling_ID { get; set; }
    public int Cream_ID { get; set; }
    public int Soaking_ID { get; set; }
    public int MousseCheeseCake_ID { get; set; }
    public decimal Price { get; set; }
    public decimal Weight { get; set; }
    public string? Inscription { get; set; }
    public string? Shape { get; set; }
    public int? GuestCount { get; set; }
    public string? DesignNotes { get; set; }
    public DateTime CreatedAt { get; set; }
    public Person Person { get; set; } = null!;
    public Ingredient Biscuit { get; set; } = null!;
    public Ingredient Filling { get; set; } = null!;
    public Ingredient Cream { get; set; } = null!;
    public Ingredient Soaking { get; set; } = null!;
    public Ingredient Mousse { get; set; } = null!;
    public string Description => $"Бісквіт: {Biscuit.IngredientName}; начинка: {Filling.IngredientName}; крем: {Cream.IngredientName}; просочення: {Soaking.IngredientName}; мус: {Mousse.IngredientName}";
    public string ConfigurationDescription => Description
        + (Shape == null ? "" : $"; форма: {ShapeLabel(Shape)}")
        + (GuestCount == null ? "" : $"; гостей: {GuestCount}")
        + (string.IsNullOrWhiteSpace(DesignNotes) ? "" : $"; декор: {DesignNotes}");
    public static string ShapeLabel(string shape) => shape switch { "Round" => "Кругла", "Square" => "Квадратна", "Heart" => "Серце", _ => shape };
}

public class CustomCakeCart
{
    public int CustomCakeCart_ID { get; set; }
    public int CustomCake_ID { get; set; }
    public int? Order_ID { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal? UnitPriceSnapshot { get; set; }
    public decimal? WeightSnapshot { get; set; }
    public string? DescriptionSnapshot { get; set; }
    public CustomCake CustomCake { get; set; } = null!;
    public PersonOrder? Order { get; set; }
    public decimal UnitPrice => UnitPriceSnapshot ?? CustomCake.Price;
}
