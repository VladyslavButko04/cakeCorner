using CakeCornerWeb;
using CakeCornerWeb.Extensions;
using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Services;
using CakeCornerWeb.Services.Security;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Добавление контроллеров и представлений (MVC)
builder.Services.AddControllersWithViews();

// 2. Настройка подключения к базе данных
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3. Регистрация сервисов и репозиториев (DI)
builder.Services.AddScoped<ICakeRepository, CakeRepository>();
builder.Services.AddScoped<IPersonService, PersonService>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtProvider, JwtProvider>();
builder.Services.AddScoped<ICartService, CartService>();

builder.Services.AddAutoMapper(cfg => { }, typeof(Program).Assembly);
builder.Services.AddHttpContextAccessor();

// 4. Считывание и регистрация JwtOptions
var jwtOptions = builder.Configuration.GetSection("JwtOptions").Get<JwtOptions>() ?? new JwtOptions();
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