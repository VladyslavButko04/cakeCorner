using CakeCornerWeb.Services.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace CakeCornerWeb.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static void AddJwtAuthentication(this IServiceCollection services,
            IOptions<JwtOptions> jwtOptions)
        {
            var options = jwtOptions.Value;

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, jwt =>
                {
                    //setting token
                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(options.SecretKey))
                    };

                    //token from cookie
                    jwt.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            var token = context.Request.Cookies["testy-cookies"];
                            context.Token = token;
                            return Task.CompletedTask;
                        }
                    };
                });


            //adding policy for autorized users
            services.AddAuthorization(options =>
            {
                options.AddPolicy("Client", policy =>
                {
                    policy.RequireRole("Client");
                });

                options.AddPolicy("AdminOnly", policy =>
                {
                    policy.RequireRole("Admin");
                });
            });
        }
    }
}