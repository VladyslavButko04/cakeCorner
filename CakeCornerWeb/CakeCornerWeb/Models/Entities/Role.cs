namespace CakeCornerWeb.Models.Entities
{
    public class Role
    {
        public int Role_ID { get; set; }
        public string Name { get; set; }

        public ICollection<Person> Persons { get; set; } = [];
    }
}
