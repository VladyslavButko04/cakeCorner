using Microsoft.AspNetCore.RateLimiting;
using CakeCornerWeb;
using CakeCornerWeb.Extensions;
using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Services;
using CakeCornerWeb.Services.Security;
using Microsoft.EntityFrameworkCore;
using CakeCornerWeb.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<CakeCornerWeb.Recommendations.Runtime.RecommendationRuntimeOptions>(builder.Configuration.GetSection("Recommendations"));
var recommendationMethodPath = Path.Combine(builder.Environment.ContentRootPath, "Recommendations", "method.runtime.json");
var recommendationMethod = File.Exists(recommendationMethodPath)
    ? System.Text.Json.JsonSerializer.Deserialize<CakeCornerWeb.Recommendations.RecommendationMethod>(File.ReadAllText(recommendationMethodPath), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("Invalid recommendation method configuration.")
    : CakeCornerWeb.Recommendations.Core.RecommendationDefaults.Create();
CakeCornerWeb.Recommendations.Core.RecommendationEngine.ValidateMethod(recommendationMethod);
builder.Services.AddSingleton(recommendationMethod);
builder.Services.AddSingleton<CakeCornerWeb.Recommendations.Core.RecommendationEngine>();
builder.Services.AddScoped<CakeCornerWeb.Recommendations.Persistence.RecommendationStore>();
builder.Services.AddScoped<CakeCornerWeb.Recommendations.Runtime.RecommendationService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("recommendations", http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
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
app.UseRateLimiter();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program { }
