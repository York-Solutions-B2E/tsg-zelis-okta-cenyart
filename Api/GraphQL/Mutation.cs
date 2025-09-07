using Microsoft.AspNetCore.Authorization;
using Api.Services;
using Shared;
using Api.Data;

namespace Api.GraphQL;

public class Mutation
{
    // Provision user after OIDC login — returns UserDto with role and claims
    public async Task<UserDto> ProvisionOnLoginAsync(
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
            var userDto = await prov.ProvisionOnLoginAsync(externalId, email, provider, ct);
            logger.LogInformation("ProvisionOnLogin succeeded for externalId={ExternalId}", externalId);
            return userDto;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ProvisionOnLogin failed for externalId={ExternalId}", externalId);
            throw;
        }
    }

    // Add arbitrary security event (Logout, etc.) — requires authentication
    [Authorize]
    public async Task<SecurityEventDto> AddSecurityEventAsync(
    string eventType,
    Guid affectedUserId,
    string details,
    [Service] IHttpContextAccessor http,
    [Service] SecurityEventService events,
    [Service] ILogger<Mutation> logger,
    CancellationToken ct = default)
    {
        var authorUserId = GetUserIdFromClaims(http);

        logger.LogInformation(
            "AddSecurityEvent called by {Author} for {Affected} type={Type}",
            authorUserId, affectedUserId, eventType
        );

        var ev = await events.CreateEventAsync(eventType, authorUserId, affectedUserId, details, ct);

        return new SecurityEventDto(
            Id: ev.Id,
            EventType: ev.EventType,
            AuthorUserId: ev.AuthorUserId,
            AffectedUserId: ev.AffectedUserId,
            OccurredUtc: ev.OccurredUtc,
            Details: ev.Details
        );
    }

    // Assign role to a user — requires the RoleChanges permission
    [Authorize(Policy = "CanViewRoleChanges")]
    public async Task<AssignRolePayload> AssignUserRoleAsync(
        Guid userId,
        Guid roleId,
        [Service] IHttpContextAccessor http,
        [Service] RoleService roles,
        [Service] SecurityEventService events,
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

        // Log security event for role change
        await events.CreateEventAsync(
            eventType: "RoleAssigned",
            authorUserId: authorUserId,
            affectedUserId: userId,
            details: $"from={oldRoleName} to={newRoleName}",
            ct: ct
        );

        return new AssignRolePayload(true, message);
    }

    // Helper to get current user ID from claims
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
