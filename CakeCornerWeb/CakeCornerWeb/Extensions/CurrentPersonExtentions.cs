using System.Security.Claims;

namespace CakeCornerWeb.Extensions
{
    public static class CurrentPersonExtentions
    {
        public static int? PersonId(this ClaimsPrincipal user)
        {
            var claim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("person_ID")?.Value;
            return user.Identity?.IsAuthenticated == true && int.TryParse(claim, out var id) && id > 0 ? id : null;
        }
    }
}
