using Api.Data;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace Api.GraphQL;

[ExtendObjectType("Mutation")]
public class ProvisioningMutations
{
    /// <summary>
    /// Provision a user on login and return a signed JWT token that includes user, role, and claims.
    /// </summary>
    public async Task<string> ProvisionOnLoginAsync(
        string externalId,
        string email,
        string provider,
        [Service] ProvisioningService provisioning,
        CancellationToken ct = default)
    {
        // Service now returns just the token
        return await provisioning.ProvisionOnLoginAsync(externalId, email, provider, ct);
    }
}

public class Mutation(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    [Authorize(Policy = "CanViewRoleChanges")]
    public async Task<AssignRoleResultDto> AssignUserRole(Guid userId, Guid roleId)
    {
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) throw new GraphQLException("User not found");

        var oldRoleName = user.Role?.Name ?? "Unknown";
        var newRole = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
        if (newRole == null) throw new GraphQLException("Role not found");

        user.RoleId = roleId;
        await _db.SaveChangesAsync();

        // Single RoleAssigned event
        var ev = new SecurityEvent
        {
            Id = Guid.NewGuid(),
            EventType = "RoleAssigned",
            AuthorUserId = userId,      // if the author is the caller; you may want to use caller id from JWT
            AffectedUserId = userId,
            Details = $"from={oldRoleName} to={newRole.Name}",
            OccurredUtc = DateTime.UtcNow
        };
        _db.SecurityEvents.Add(ev);
        await _db.SaveChangesAsync();

        return new AssignRoleResultDto(true, $"Role changed from {oldRoleName} to {newRole.Name}");
    }
}
