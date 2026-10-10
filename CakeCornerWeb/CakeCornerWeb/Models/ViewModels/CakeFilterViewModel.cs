using Microsoft.AspNetCore.Mvc.Rendering;

namespace CakecornerWebsite.Models.ViewModels
{
    public class CakeFilterViewModel
    {
        public int? CategoryId { get; set; }
        public int? FlavorId { get; set; }
        public List<SelectListItem> Categories { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Flavors { get; set; } = new List<SelectListItem>();
    }
}