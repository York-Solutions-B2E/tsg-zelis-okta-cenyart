using Api.Auth;
using Api.Data;
using Api.Services;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace Api.GraphQL;

// Provisioning mutations
[ExtendObjectType("Mutation")]
public class ProvisioningMutations
{
    /// <summary>
    /// provisionOnLogin(externalId, email, provider): String!
    /// Returns a signed JWT token (string).
    /// </summary>
    public async Task<string> ProvisionOnLoginAsync(
        string externalId,
        string email,
        string provider,
        [Service] ProvisioningService provisioning,
        CancellationToken ct = default)
    {
        var token = await provisioning.ProvisionAndIssueTokenAsync(externalId, email, provider, ct);
        return token;
    }
}

// General mutations
public class Mutation(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    // Assign role to user with audit event
    public async Task<User?> AssignUserRole(Guid userId, Guid roleId, [Service] AuthorizationService auth)
    {
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return null;

        if (!await auth.CanViewRoleChangesAsync(userId))
            throw new Exception("Unauthorized");

        var oldRoleName = user.Role?.Name ?? "Unknown";
        var newRole = await _db.Roles.FirstAsync(r => r.Id == roleId);

        user.RoleId = roleId;
        await _db.SaveChangesAsync();

        _db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.NewGuid(),
            EventType = "RoleAssigned",
            AuthorUserId = userId,
            AffectedUserId = userId,
            Details = $"from={oldRoleName} to={newRole.Name}",
            OccurredUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return user;
    }

    // Add arbitrary security event
    public async Task<SecurityEvent> AddSecurityEvent(SecurityEvent input)
    {
        input.Id = Guid.NewGuid();
        input.OccurredUtc = DateTime.UtcNow;
        _db.SecurityEvents.Add(input);
        await _db.SaveChangesAsync();
        return input;
    }
}
