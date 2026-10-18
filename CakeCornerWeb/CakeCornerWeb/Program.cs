using CakeCornerWeb;
using CakeCornerWeb.Extensions;
using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Services;
using CakeCornerWeb.Services.Security;
using Microsoft.EntityFrameworkCore;
using CakeCornerWeb.Data;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString) || connectionString == "YOUR_CONNECTION_STRING")
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection in local settings or environment variables.");

// 1. Добавление контроллеров и представлений (MVC)
builder.Services.AddControllersWithViews();

// 2. Настройка подключения к базе данных
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// 3. Регистрация сервисов и репозиториев (DI)
builder.Services.AddScoped<ICakeRepository, CakeRepository>();
builder.Services.AddScoped<IPersonService, PersonService>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtProvider, JwtProvider>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<CatalogAdminService>();
builder.Services.AddSingleton<CatalogImageStore>();
builder.Services.AddScoped<CustomCakeService>();
builder.Services.AddSingleton<IngredientImageService>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, CurrentAdminHandler>();

builder.Services.AddAutoMapper(cfg => { }, typeof(Program).Assembly);
builder.Services.AddHttpContextAccessor();

// 4. Считывание и регистрация JwtOptions
var jwtOptions = builder.Configuration.GetSection("JwtOptions").Get<JwtOptions>() ?? new JwtOptions();
if (System.Text.Encoding.UTF8.GetByteCount(jwtOptions.SecretKey ?? "") < 32 || jwtOptions.ExpiresHours <= 0)
    throw new InvalidOperationException("Configure JwtOptions:SecretKey with at least 32 UTF-8 bytes and a positive JwtOptions:ExpiresHours.");
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("JwtOptions"));

// 5. Добавление JWT-аутентификации без BuildServiceProvider()
builder.Services.AddJwtAuthentication(jwtOptions);

var app = builder.Build();

// 6. Настройка HTTP-пайплайна
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

var cultureInfo = new System.Globalization.CultureInfo("uk-UA");
cultureInfo.NumberFormat.NumberDecimalSeparator = ".";
cultureInfo.NumberFormat.CurrencyDecimalSeparator = ".";

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(cultureInfo),
    SupportedCultures = new[] { cultureInfo },
    SupportedUICultures = new[] { cultureInfo }
});

app.UseStaticFiles();
app.UseHttpsRedirection();
app.UseRouting();

// Аутентификация обязана идти ДО авторизации
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program { }
