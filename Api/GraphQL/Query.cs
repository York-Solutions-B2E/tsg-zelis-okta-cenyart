using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Api.Data;

namespace Api.GraphQL;

public class Query(AppDbContext db, IHttpContextAccessor http)
{
    private readonly AppDbContext _db = db;
    private readonly IHttpContextAccessor _http = http;

    // users and roles are simple and require authentication
    [Authorize] // requires authenticated JWT
    public IQueryable<User> GetUsers() => _db.Users.Include(u => u.Role);

    [Authorize]
    public IQueryable<Role> GetRoles() => _db.Roles;

    // securityEvents is gated based on user's permissions in JWT
    [Authorize]
    public IQueryable<SecurityEvent> GetSecurityEvents()
    {
        var user = _http.HttpContext?.User;
        if (user == null || !user.Identity?.IsAuthenticated == true)
            return Enumerable.Empty<SecurityEvent>().AsQueryable();

        var hasViewAuth = user.HasClaim("permissions", "Audit.ViewAuthEvents");
        var hasRoleChanges = user.HasClaim("permissions", "Audit.RoleChanges");

        if (hasRoleChanges)
        {
            return _db.SecurityEvents.OrderByDescending(e => e.OccurredUtc);
        }
        else if (hasViewAuth)
        {
            return _db.SecurityEvents
                .Where(e => e.EventType.StartsWith("Login"))
                .OrderByDescending(e => e.OccurredUtc);
        }

        return Enumerable.Empty<SecurityEvent>().AsQueryable();
    }
}
