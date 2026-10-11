namespace CakeCornerWeb.Models.Entities
{
    public class Role
    {
        public int Role_ID { get; set; }
        public string RoleName { get; set; }

        public ICollection<Person> Persons { get; set; } = [];
    }
}
