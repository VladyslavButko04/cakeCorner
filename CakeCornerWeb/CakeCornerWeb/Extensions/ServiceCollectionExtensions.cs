using CakeCornerWeb.Services.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace CakeCornerWeb.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddJwtAuthentication(
            this IServiceCollection services,
            JwtOptions jwtOptions)
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, jwt =>
            {
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
                    // Важно: указывает ASP.NET Core, какие клеймы считать ID и Ролью
                    NameClaimType = ClaimTypes.NameIdentifier,
                    RoleClaimType = ClaimTypes.Role
                };

                // Извлечение токена из куки "testy-cookies"
                jwt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Cookies.TryGetValue("testy-cookies", out var token))
                        {
                            context.Token = token;
                        }
                        return Task.CompletedTask;
                    },

                    OnChallenge = context =>
                    {
                        // MVC navigation should take visitors to login; mutation/API requests retain 401.
                        if (HttpMethods.IsGet(context.Request.Method) &&
                            (context.Request.Path.StartsWithSegments("/Orders") ||
                             context.Request.Path.StartsWithSegments("/Constructor") ||
                             context.Request.Path.StartsWithSegments("/AdminOrders") ||
                             context.Request.Path.StartsWithSegments("/Account/Profile")))
                        {
                            context.HandleResponse();
                            var returnUrl = context.Request.Path + context.Request.QueryString;
                            context.Response.Redirect("/Account/Login?returnUrl=" + Uri.EscapeDataString(returnUrl));
                        }
                        return Task.CompletedTask;
                    }
                };
            });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("Client", policy => policy.RequireRole("Client"));
                options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
            });

            return services;
        }
    }
}