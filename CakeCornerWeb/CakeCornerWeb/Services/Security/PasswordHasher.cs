using CakeCornerWeb.Interfaces;

namespace CakeCornerWeb.Services.Security
{   
    public class PasswordHasher : IPasswordHasher
    {
        public string Generate(string password) =>
            BCrypt.Net.BCrypt.HashPassword(password);

        public bool Verify(string password, string hashedPassword) =>
            BCrypt.Net.BCrypt.Verify(password, hashedPassword);
    }
}