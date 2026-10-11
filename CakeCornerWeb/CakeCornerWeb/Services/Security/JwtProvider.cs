using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Models.Entities;
using CakeCornerWeb.Services.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

public class JwtProvider(IOptions<JwtOptions> options) : IJwtProvider
{

    private readonly JwtOptions _options = options.Value;

    public string GenerateToken(Person person)
    {
        Claim[] claims = [
            new(ClaimTypes.NameIdentifier, person.Person_ID.ToString()),
            new("person_ID", person.Person_ID.ToString()),
            new(ClaimTypes.Name, person.Name),
            new(ClaimTypes.Role, person.Role.RoleName),
            new(ClaimTypes.Email, person.Email)];

        var signinCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            signingCredentials: signinCredentials,
            expires: DateTime.UtcNow.AddHours(_options.ExpiresHours));

        var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);

        return tokenValue;
    }
}
