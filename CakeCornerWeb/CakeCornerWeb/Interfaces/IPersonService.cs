using CakeCornerWeb.Models;
using CakeCornerWeb.Models.Entities;


namespace CakeCornerWeb.Interfaces
{
    public interface IPersonService
    {
        Task<bool> RegisterAsync(RegisterViewModel model, int defaultRoleId);
        Task<string?> LoginAsync(string email, string password);
        Task<Person?> GetByEmailAsync(string email);
        Task<Person?> GetProfileAsync(string email);
    }
}
