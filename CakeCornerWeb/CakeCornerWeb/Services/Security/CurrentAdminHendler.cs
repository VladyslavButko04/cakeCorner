using CakeCornerWeb.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using CakeCornerWeb.Extensions;

namespace CakeCornerWeb.Services.Security;

public sealed class CurrentAdminRequirement : IAuthorizationRequirement;

public sealed class CurrentAdminHandler(ApplicationDbContext context) : AuthorizationHandler<CurrentAdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext authorization, CurrentAdminRequirement requirement)
    {
        if (authorization.User.IsInRole("Admin") && authorization.User.PersonId() is int id &&
            await context.Persons.AnyAsync(p => p.Person_ID == id && p.Role.RoleName == "Admin"))
            authorization.Succeed(requirement);
    }
}
