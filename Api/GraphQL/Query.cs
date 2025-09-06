using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.Services;
using System.Security.Claims;

namespace Api.GraphQL;

public class Query
{
    // users and roles require authentication
    [Authorize]
    public IQueryable<User> GetUsers([Service] AppDbContext db)
        => db.Users.Include(u => u.Role);

    [Authorize]
    public IQueryable<Role> GetRoles([Service] AppDbContext db)
        => db.Roles;

    // securityEvents gated based on permissions in caller's JWT
    // Return IEnumerable to let the service decide query vs. materialization.
    [Authorize]
    public IEnumerable<SecurityEvent> GetSecurityEvents(
        [Service] IHttpContextAccessor http,
        [Service] SecurityEventService securityEvents)
    {
        var user = http.HttpContext?.User ?? new ClaimsPrincipal();
        return securityEvents.GetEventsForUser(user);
    }
}
