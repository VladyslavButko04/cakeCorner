using CakeCornerWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CakeCornerWeb.Data;

public static class OrderModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        var order = model.Entity<PersonOrder>();
        order.ToTable("PersonOrder").HasKey(o => o.Order_ID);
        order.Property(o => o.TotalPrice).HasPrecision(10, 2);
        order.Property(o => o.PhoneNumber).HasMaxLength(20);
        order.Property(o => o.Comment).HasMaxLength(300);
        order.Property(o => o.RowVersion).IsRowVersion();
        order.HasIndex(o => new { o.Person_ID, o.CheckoutToken }).IsUnique().HasFilter("[CheckoutToken] IS NOT NULL");
        order.HasIndex(o => new { o.Person_ID, o.CreatedAt });
        order.HasIndex(o => new { o.OrderStatus_ID, o.CreatedAt });
        order.HasOne(o => o.Person).WithMany().HasForeignKey(o => o.Person_ID).OnDelete(DeleteBehavior.NoAction);
        order.HasOne(o => o.Status).WithMany().HasForeignKey(o => o.OrderStatus_ID).OnDelete(DeleteBehavior.NoAction);
        order.HasOne(o => o.PickupLocation).WithMany().HasForeignKey(o => o.PickupLocation_ID).OnDelete(DeleteBehavior.NoAction);

        var item = model.Entity<OrderItem>();
        item.ToTable("OrderItem").HasKey(i => i.OrderItem_ID);
        item.Property(i => i.ItemPrice).HasPrecision(10, 2);
        item.Property(i => i.WeightSnapshot).HasPrecision(3, 1);
        item.Property(i => i.CakeNameSnapshot).HasMaxLength(200);
        item.HasOne(i => i.Order).WithMany(o => o.Items).HasForeignKey(i => i.Order_ID).OnDelete(DeleteBehavior.NoAction);
        item.HasOne(i => i.CakeWeight).WithMany().HasForeignKey(i => i.CakeWeight_ID).OnDelete(DeleteBehavior.NoAction);
        item.HasIndex(i => i.Order_ID);

        var status = model.Entity<OrderStatus>();
        status.ToTable("OrderStatus").HasKey(s => s.OrderStatus_ID);
        status.Property(s => s.OrderStatusName).HasMaxLength(50);
        status.Property(s => s.Code).HasMaxLength(32);
        status.HasIndex(s => s.Code).IsUnique().HasFilter("[Code] IS NOT NULL");

        var history = model.Entity<OrderStatusHistory>();
        history.ToTable("OrderStatusHistory").HasKey(h => h.Id);
        history.Property(h => h.FromCode).HasMaxLength(32);
        history.Property(h => h.ToCode).HasMaxLength(32);
        history.Property(h => h.Note).HasMaxLength(500);
        history.HasOne(h => h.Order).WithMany(o => o.History).HasForeignKey(h => h.Order_ID).OnDelete(DeleteBehavior.NoAction);
        history.HasOne<Person>().WithMany().HasForeignKey(h => h.ChangedByPerson_ID).OnDelete(DeleteBehavior.NoAction);
        history.HasIndex(h => new { h.Order_ID, h.ChangedAt });

        var payment = model.Entity<Payment>();
        payment.ToTable("Payment").HasKey(p => p.Payment_ID);
        payment.Property(p => p.OnlineAmount).HasPrecision(10, 2);
        payment.Property(p => p.RemaininAmount).HasPrecision(10, 2);
        payment.Property(p => p.PaidAmount).HasPrecision(10, 2);
        payment.HasOne(p => p.Order).WithOne(o => o.Payment).HasForeignKey<Payment>(p => p.PersonOrder_ID).OnDelete(DeleteBehavior.NoAction);

        model.Entity<City>().ToTable("City").HasKey(c => c.City_ID);
        model.Entity<City>().Property(c => c.CityName).HasMaxLength(100);
        var pickup = model.Entity<PickupLocation>();
        pickup.ToTable("PickupLocation").HasKey(p => p.PickupLocation_ID);
        pickup.Property(p => p.Address).HasMaxLength(200);
        pickup.Property(p => p.PhoneNumber).HasMaxLength(20);
        pickup.Property(p => p.Notes).HasMaxLength(300);
        pickup.HasOne(p => p.City).WithMany().HasForeignKey(p => p.City_ID).OnDelete(DeleteBehavior.NoAction);

        model.Entity<IngredientType>().ToTable("IngredientTypes").HasKey(t => t.IngredientsTypes_ID);
        model.Entity<IngredientType>().Property(t => t.IngredientsTypesName).HasMaxLength(20);
        model.Entity<Cake>().Property(c => c.IsActive).HasDefaultValue(true);
        model.Entity<Cake>().Property(c => c.RowVersion).IsRowVersion();
        model.Entity<Cake>().Property(c => c.CakeName).HasMaxLength(100);
        model.Entity<Cake>().Property(c => c.DescriptionShort).HasMaxLength(300);
        model.Entity<Cake>().Property(c => c.DescriptionLong).HasMaxLength(4000);
        model.Entity<Cake>().Property(c => c.ImagePath).HasMaxLength(255);
        model.Entity<Category>().Property(c => c.CategoryName).HasMaxLength(100);
        model.Entity<Flavor>().Property(c => c.FlavorName).HasMaxLength(100);
        model.Entity<CakeWeight>().Property(w => w.IsActive).HasDefaultValue(true);
        var ingredient = model.Entity<Ingredient>();
        ingredient.Property(i => i.IsActive).HasDefaultValue(true);
        ingredient.Property(i => i.RowVersion).IsRowVersion();
        ingredient.ToTable("Ingredient").HasKey(i => i.Ingredients_ID);
        ingredient.Property(i => i.IngredientName).HasMaxLength(100);
        ingredient.Property(i => i.IngredientDescription).HasMaxLength(255);
        ingredient.Property(i => i.IngredientImagePath).HasMaxLength(100);
        ingredient.Property(i => i.Price).HasPrecision(10, 2);
        ingredient.HasOne(i => i.Type).WithMany().HasForeignKey(i => i.IngredientTypes_ID).OnDelete(DeleteBehavior.NoAction);

        var custom = model.Entity<CustomCake>();
        custom.ToTable("CustomCake").HasKey(c => c.CustomCake_ID);
        custom.Property(c => c.Price).HasPrecision(10, 2);
        custom.Property(c => c.Weight).HasPrecision(3, 1);
        custom.Property(c => c.Inscription).HasMaxLength(50);
        custom.Property(c => c.Shape).HasMaxLength(20);
        custom.Property(c => c.DesignNotes).HasMaxLength(500);
        custom.Ignore(c => c.Description);
        custom.Ignore(c => c.ConfigurationDescription);
        custom.HasOne(c => c.Person).WithMany().HasForeignKey(c => c.Person_ID).OnDelete(DeleteBehavior.NoAction);
        custom.HasOne(c => c.Biscuit).WithMany().HasForeignKey(c => c.Biscuit_ID).OnDelete(DeleteBehavior.NoAction);
        custom.HasOne(c => c.Filling).WithMany().HasForeignKey(c => c.Filling_ID).OnDelete(DeleteBehavior.NoAction);
        custom.HasOne(c => c.Cream).WithMany().HasForeignKey(c => c.Cream_ID).OnDelete(DeleteBehavior.NoAction);
        custom.HasOne(c => c.Soaking).WithMany().HasForeignKey(c => c.Soaking_ID).OnDelete(DeleteBehavior.NoAction);
        custom.HasOne(c => c.Mousse).WithMany().HasForeignKey(c => c.MousseCheeseCake_ID).OnDelete(DeleteBehavior.NoAction);

        var customItem = model.Entity<CustomCakeCart>();
        customItem.ToTable("CustomCakeCart").HasKey(c => c.CustomCakeCart_ID);
        customItem.Property(c => c.UnitPriceSnapshot).HasPrecision(10, 2);
        customItem.Property(c => c.WeightSnapshot).HasPrecision(3, 1);
        customItem.Property(c => c.DescriptionSnapshot).HasMaxLength(2000);
        customItem.Ignore(c => c.UnitPrice);
        customItem.HasOne(c => c.CustomCake).WithMany().HasForeignKey(c => c.CustomCake_ID).OnDelete(DeleteBehavior.NoAction);
        customItem.HasOne(c => c.Order).WithMany(o => o.CustomItems).HasForeignKey(c => c.Order_ID).OnDelete(DeleteBehavior.NoAction);
        customItem.HasIndex(c => c.Order_ID);

        model.Entity<CakeWeight>().Property(w => w.Price).HasPrecision(10, 2);
        model.Entity<WeightOption>().Property(w => w.Weight).HasPrecision(3, 1);
        model.Entity<CartItem>().Property(i => i.Price).HasPrecision(10, 2);
        model.Entity<Cart>().HasIndex(c => c.Person_ID).IsUnique();
        model.Entity<CartItem>().HasIndex(c => new { c.Cart_ID, c.CakeWeight_ID }).IsUnique();
    }
}