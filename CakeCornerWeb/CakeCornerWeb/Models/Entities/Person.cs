namespace CakeCornerWeb.Models.Entities
{
    public class Person
    {
        public int Person_ID { get; set; }
        public int Role_ID { get; set; }
        public DateTime CreatedAt { get; set; }
        public string PasswordHash { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Phone { get; set; }
        public Role Role { get; set; } = null!;

        public static Person Create(string name, string email,
            int role_ID, string passwordHash,
            string? phone = null)
        {
            return new Person
            {
                Name = name,
                Email = email,
                PasswordHash = passwordHash,
                Phone = phone,
                Role_ID = role_ID,
                CreatedAt = DateTime.UtcNow
            };
        }
    }
}
