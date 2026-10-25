using CakeCornerWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.AccessControl;

namespace CakeCornerWeb.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Person> Persons { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Cake> Cakes { get; set; }
        public DbSet<CakeWeight> CakeWeights { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<WeightOption> WeightOptions { get; set; }
        public DbSet<Flavor> Flavors { get; set; }
        public DbSet<CakeFlavor> CakeFlavors => Set<CakeFlavor>();
        public DbSet<Cart> Carts { get; set; }
        public DbSet<CartItem> CartItems { get; set; }
        public DbSet<PersonOrder> Orders => Set<PersonOrder>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        public DbSet<OrderStatus> OrderStatuses => Set<OrderStatus>();
        public DbSet<OrderStatusHistory> OrderHistory => Set<OrderStatusHistory>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<PickupLocation> PickupLocations => Set<PickupLocation>();
        public DbSet<City> Cities => Set<City>();
        public DbSet<Ingredient> Ingredients => Set<Ingredient>();
        public DbSet<IngredientType> IngredientTypes => Set<IngredientType>();
        public DbSet<CustomCake> CustomCakes => Set<CustomCake>();
        public DbSet<CustomCakeCart> CustomCartItems => Set<CustomCakeCart>();
        public DbSet<CakeProductInformation> CakeProductInformation => Set<CakeProductInformation>();
        public DbSet<Allergen> Allergens => Set<Allergen>();
        public DbSet<CakeAllergen> CakeAllergens => Set<CakeAllergen>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            OrderModelConfiguration.Configure(modelBuilder);
            CakeInformationConfiguration.Configure(modelBuilder);

            // Person
            modelBuilder.Entity<Person>().ToTable("Person").HasKey(c => c.Person_ID);
            modelBuilder.Entity<Role>().ToTable("Role").HasKey(r => r.Role_ID);

            modelBuilder.Entity<Person>()
                .HasOne(c => c.Role)
                .WithMany(r => r.Persons)
                .HasForeignKey(c => c.Role_ID);

            // DefaultCakes
            modelBuilder.Entity<Cake>().ToTable("Cake").HasKey(c => c.Cake_ID);
            modelBuilder.Entity<CakeWeight>().ToTable("CakeWeight").HasKey(cw => cw.CakeWeight_ID);
            modelBuilder.Entity<Category>().ToTable("Category").HasKey(cat => cat.Category_ID);
            modelBuilder.Entity<WeightOption>().ToTable("WeightOption").HasKey(w => w.WeightOption_ID);
            modelBuilder.Entity<Flavor>().ToTable("Flavor").HasKey(f => f.Flavor_ID);

            modelBuilder.Entity<Cake>()
                .HasMany(c => c.CakeWeights)
                .WithOne(w => w.Cake)
                .HasForeignKey(w => w.Cake_ID);

            modelBuilder.Entity<Cake>()
                .HasOne(c => c.Category)
                .WithMany(cat => cat.Cakes)
                .HasForeignKey(c => c.Category_ID);

            var cakeFlavor = modelBuilder.Entity<CakeFlavor>();
            cakeFlavor.ToTable("CakeFlavor").HasKey(cf => new { cf.Cake_ID, cf.Flavor_ID });
            cakeFlavor.HasOne(cf => cf.Cake).WithMany(c => c.CakeFlavors)
                .HasForeignKey(cf => cf.Cake_ID).OnDelete(DeleteBehavior.Cascade);
            cakeFlavor.HasOne(cf => cf.Flavor).WithMany(f => f.CakeFlavors)
                .HasForeignKey(cf => cf.Flavor_ID).OnDelete(DeleteBehavior.Restrict);
            cakeFlavor.HasIndex(cf => cf.Flavor_ID).HasDatabaseName("IX_CakeFlavor_Flavor_ID");

            modelBuilder.Entity<CakeWeight>()
                .HasOne(cw => cw.WeightOption)
                .WithMany(wo => wo.CakeWeights)
                .HasForeignKey(cw => cw.WeightOption_ID);


            // Cart
            modelBuilder.Entity<Cart>(entity =>
            {
                entity.ToTable("Cart");
                entity.HasKey(c => c.Cart_ID);

                entity.HasOne(c => c.Person)
                    .WithMany()
                    .HasForeignKey(c => c.Person_ID);
            });

            modelBuilder.Entity<CartItem>(entity =>
            {
                entity.ToTable("CartItem");
                entity.HasKey(ci => ci.CartItem_ID);

                entity.HasOne(ci => ci.Cart)
                    .WithMany(c => c.CartItems)
                    .HasForeignKey(ci => ci.Cart_ID);

                entity.HasOne(ci => ci.CakeWeight)
                    .WithMany()
                    .HasForeignKey(ci => ci.CakeWeight_ID);
            });
        }
    }
}
