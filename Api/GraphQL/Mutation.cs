using Microsoft.AspNetCore.Authorization;
using Api.Services;
using Shared;

namespace Api.GraphQL;

public class Mutation
{
    // -------------------------------
    // AddSecurityEvent
    // -------------------------------
    [Authorize]
    public async Task<SecurityEventDto> AddSecurityEventAsync(
        string eventType,
        Guid authorUserId,
        Guid affectedUserId,
        string details,
        [Service] SecurityEventService events,
        CancellationToken ct = default)
    {
        // Create the security event
        var ev = await events.CreateEventAsync(
            eventType,
            authorUserId,
            affectedUserId,
            details,
            ct
        );

        // Convert to DTO
        return new SecurityEventDto(
            ev.Id,
            ev.EventType,
            ev.AuthorUserId,
            ev.AffectedUserId,
            ev.OccurredUtc,
            ev.Details
        );
    }

    // -------------------------------
    // AssignUserRole
    // -------------------------------
    [Authorize(Policy = "CanViewRoleChanges")]
    public async Task<AssignRolePayload> AssignUserRoleAsync(
        Guid userId,
        Guid roleId,
        string oldRole,
        string newRole,
        [Service] RoleService roles,
        [Service] ILogger<Mutation> logger,
        CancellationToken ct = default)
    {
        logger.LogInformation("AssignUserRoleAsync called for user {UserId} -> role {RoleId}", userId, roleId);

        var (success, message, oldRoleName, newRoleName) = await roles.UpdateUserRoleAsync(userId, roleId, ct);
        if (!success)
        {
            logger.LogWarning("AssignUserRoleAsync failed to update user {UserId}: {Message}", userId, message);
            throw new GraphQLException(message);
        }

        return new AssignRolePayload(true, message, oldRoleName, newRoleName);
    }

    // -------------------------------
    // ProvisionOnLogin
    // -------------------------------
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
}
