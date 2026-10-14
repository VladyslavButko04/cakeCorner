using CakeCornerWeb.Interfaces;
using CakeCornerWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CakeCornerWeb.Services
{
    public class PersonService : IPersonService
    {
        private readonly IPasswordHasher _passwordHasher;
        private readonly ApplicationDbContext _context;
        private readonly IJwtProvider _jwtProvider;

        public PersonService(
            ApplicationDbContext context,
            IPasswordHasher passwordHasher,
            IJwtProvider jwtProvider)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _jwtProvider = jwtProvider;
        }

        public async Task<bool> RegisterAsync(RegisterViewModel model, int defaultRoleId)
        {
            var email = model.Email.Trim().ToLowerInvariant();
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await SqlServerLocks.AcquireAsync(_context, $"CakeCorner:Register:{email}");
            if (await _context.Persons.AnyAsync(p => p.Email.ToLower() == email))
                return false;

            var hashedPassword = _passwordHasher.Generate(model.Password);

            var person = Person.Create(
                name: model.Name.Trim(),
                email: email,
                passwordHash: hashedPassword,
                role_ID: defaultRoleId,
                phone: model.Phone
            );

            _context.Persons.Add(person);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return true;
        }

        public async Task<Person?> GetByEmailAsync(string email)
        {
            return await _context.Persons
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Email == email);
        }

        public async Task<string?> LoginAsync(string email, string password)
        {
            email = email.Trim().ToLowerInvariant();
            var person = await _context.Persons
                .Include(p => p.Role)
                .FirstOrDefaultAsync(p => p.Email.ToLower().Trim() == email.ToLower().Trim());

            if (person == null)
            {
                return null;
            }

            var valid = _passwordHasher.Verify(password, person.PasswordHash);


            if (!valid)
                return null;

            var token = _jwtProvider.GenerateToken(person);

            return token;
        }

        public async Task<Person?> GetProfileAsync(string email)
        {
            var person = await _context.Persons
                .Include(p => p.Role)
                .FirstOrDefaultAsync(p => p.Email.ToLower().Trim() == email.ToLower().Trim());

            return person;
        }
    }
}