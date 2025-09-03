using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.Services;

namespace Api.GraphQL;

public class Query(AppDbContext db, IHttpContextAccessor http, SecurityEventService securityEvents)
{
    private readonly AppDbContext _db = db;
    private readonly IHttpContextAccessor _http = http;
    private readonly SecurityEventService _securityEvents = securityEvents;

    [Authorize]
    public IQueryable<User> GetUsers() => _db.Users.Include(u => u.Role);

    [Authorize]
    public IQueryable<Role> GetRoles() => _db.Roles;

    [Authorize]
    public IQueryable<SecurityEvent> GetSecurityEvents()
    {
        var user = _http.HttpContext?.User;
        if (user == null || !user.Identity?.IsAuthenticated == true)
            return Enumerable.Empty<SecurityEvent>().AsQueryable();

        return _securityEvents.GetEventsForUser(user);
    }
}
