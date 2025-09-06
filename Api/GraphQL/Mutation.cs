using Microsoft.AspNetCore.Authorization;
using Api.Services;
using Shared;
using Api.Data;

namespace Api.GraphQL;

public class Mutation
{
    // Public provision mutation — used by the frontend after OIDC token validated.
    // Returns JWT token that contains uid, role, and permissions.
    public async Task<string> ProvisionOnLoginAsync(
        string externalId,
        string email,
        string provider,
        [Service] ProvisioningService prov,
        [Service] ILogger<Mutation> logger,
        CancellationToken ct = default)
    {
        logger.LogInformation("ProvisionOnLogin called. externalId={ExternalId} email={Email} provider={Provider}",
            externalId, email, provider);

        try
        {
            var token = await prov.ProvisionOnLoginAsync(externalId, email, provider, ct);
            logger.LogInformation("ProvisionOnLogin succeeded for externalId={ExternalId}", externalId);
            return token;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ProvisionOnLogin failed for externalId={ExternalId}", externalId);
            throw;
        }
    }

    // Add arbitrary security event (Logout etc.) — requires authentication
    [Authorize]
    public async Task<SecurityEvent> AddSecurityEventAsync(
        string eventType,
        Guid affectedUserId,
        string details,
        [Service] IHttpContextAccessor http,
        [Service] SecurityEventService events,
        [Service] ILogger<Mutation> logger,
        CancellationToken ct = default)
    {
        var authorUserId = GetUserIdFromClaims(http);
        logger.LogInformation("AddSecurityEvent called by {Author} for {Affected} type={Type}", authorUserId, affectedUserId, eventType);
        return await events.CreateEventAsync(eventType, authorUserId, affectedUserId, details, ct);
    }

    // Assign role to user — requires the RoleChanges permission
    [Authorize(Policy = "CanViewRoleChanges")]
    public async Task<AssignRoleResultDto> AssignUserRoleAsync(
        Guid userId,
        Guid roleId,
        [Service] IHttpContextAccessor http,
        [Service] RoleService roles,
        [Service] ILogger<Mutation> logger,
        CancellationToken ct = default)
    {
        var authorUserId = GetUserIdFromClaims(http);
        logger.LogInformation("AssignUserRoleAsync called by {Author} for user {UserId} -> role {RoleId}", authorUserId, userId, roleId);

        var (success, message, oldRoleName, newRoleName) = await roles.UpdateUserRoleAsync(userId, roleId, ct);
        if (!success)
        {
            logger.LogWarning("AssignUserRoleAsync failed to update user {UserId}: {Message}", userId, message);
            throw new GraphQLException(message);
        }

        return new AssignRoleResultDto(true, message);
    }

    private static Guid GetUserIdFromClaims(IHttpContextAccessor http)
    {
        var user = http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            throw new GraphQLException("Unauthenticated");

        var uidClaim = user.FindFirst("uid")?.Value;
        if (!Guid.TryParse(uidClaim, out var id))
            throw new GraphQLException("Invalid uid claim in token");

        return id;
    }
}
