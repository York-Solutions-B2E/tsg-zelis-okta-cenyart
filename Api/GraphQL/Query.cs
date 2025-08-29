using Api.Auth;
using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.GraphQL;

// Mark this class as an extension of the root Query type
[ExtendObjectType("Query")]
public class AuthorizationQueries
{
    // GraphQL: canViewAuthEvents(userId: ID!): Boolean!
    public async Task<bool> CanViewAuthEventsAsync(
        Guid userId,
        [Service] AuthorizationService authorization)
    {
        return await authorization.CanViewAuthEventsAsync(userId);
    }

    // GraphQL: canViewRoleChanges(userId: ID!): Boolean!
    public async Task<bool> CanViewRoleChangesAsync(
        Guid userId,
        [Service] AuthorizationService authorization)
    {
        return await authorization.CanViewRoleChangesAsync(userId);
    }
}

// General queries
public class Query(AppDbContext db, AuthorizationService auth)
{
    private readonly AppDbContext _db = db;
    private readonly AuthorizationService _auth = auth;

    // Fetch all users with role
    public IQueryable<User> GetUsers() => _db.Users.Include(u => u.Role);

    // Fetch all roles
    public IQueryable<Role> GetRoles() => _db.Roles;

    // Security events gated by authorization
    public async Task<IQueryable<SecurityEvent>> GetSecurityEvents(Guid callerId)
    {
        if (await _auth.CanViewRoleChangesAsync(callerId))
        {
            return _db.SecurityEvents; // full access for SecurityAuditor
        }

        if (await _auth.CanViewAuthEventsAsync(callerId))
        {
            return _db.SecurityEvents.Where(e => e.EventType.StartsWith("Login")); // AuthObserver
        }

        return Enumerable.Empty<SecurityEvent>().AsQueryable(); // BasicUser cannot see any
    }
}
