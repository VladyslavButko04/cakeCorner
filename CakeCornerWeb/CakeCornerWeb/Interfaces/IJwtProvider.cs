using CakeCornerWeb.Models.Entities;

namespace CakeCornerWeb.Interfaces
{
    public interface IJwtProvider
    {
        string GenerateToken(Person person);
    }
}
