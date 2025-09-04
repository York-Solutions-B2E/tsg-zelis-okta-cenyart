using Microsoft.AspNetCore.Authorization;
using Api.Data;
using Api.Services;
using Shared;

namespace Api.GraphQL;

public class Mutation(
    ProvisioningService prov,
    IHttpContextAccessor http,
    SecurityEventService events,
    RoleService roles,
    ILogger<Mutation> logger)
{
    private readonly ProvisioningService _prov = prov;
    private readonly IHttpContextAccessor _http = http;
    private readonly SecurityEventService _events = events;
    private readonly RoleService _roles = roles;
    private readonly ILogger _logger = logger;

    // Public provision mutation — used by the frontend after OIDC token validated.
    // Returns JWT token that contains uid, role, and permissions.
    public async Task<string> ProvisionOnLoginAsync(
        string externalId,
        string email,
        string provider,
        CancellationToken ct = default)
    {
        _logger.LogInformation("ProvisionOnLogin called. externalId={ExternalId} email={Email} provider={Provider}",
            externalId, email ?? "<null>", provider ?? "<null>");

        try
        {
            var token = await _prov.ProvisionOnLoginAsync(externalId, email, provider, ct);
            _logger.LogInformation("ProvisionOnLogin succeeded for externalId={ExternalId}", externalId);
            return token;
        }
        catch (Exception ex)
        {
            // Log and rethrow to allow upstream GraphQL error handling / logging middleware to capture it
            _logger.LogError(ex, "ProvisionOnLogin failed for externalId={ExternalId}", externalId);
            throw;
        }
    }

    // Add arbitrary security event (Logout etc.) — requires authentication
    [Authorize]
    public async Task<SecurityEvent> AddSecurityEventAsync(
        string eventType,
        Guid affectedUserId,
        string? details = null,
        CancellationToken ct = default)
    {
        var authorUserId = GetUserIdFromClaims();
        _logger.LogInformation("AddSecurityEvent called by {Author} for {Affected} type={Type}", authorUserId, affectedUserId, eventType);
        return await _events.CreateEventAsync(eventType, authorUserId, affectedUserId, details, ct);
    }

    // Assign role to user — requires the RoleChanges permission
    [Authorize(Policy = "CanViewRoleChanges")]
    public async Task<AssignRoleResultDto> AssignUserRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default)
    {
        var authorUserId = GetUserIdFromClaims();
        _logger.LogInformation("AssignUserRoleAsync called by {Author} for user {UserId} -> role {RoleId}", authorUserId, userId, roleId);

        var (success, message, oldRoleName, newRoleName) = await _roles.UpdateUserRoleAsync(userId, roleId, ct);
        if (!success)
        {
            _logger.LogWarning("AssignUserRoleAsync failed to update user {UserId}: {Message}", userId, message);
            throw new GraphQLException(message);
        }

        // Add RoleAssigned event after successful update
        await _events.CreateRoleAssignedAsync(authorUserId, userId, oldRoleName, newRoleName, ct);
        _logger.LogInformation("AssignUserRoleAsync emitted RoleAssigned for user {UserId} from={Old} to={New}", userId, oldRoleName, newRoleName);

        return new AssignRoleResultDto(true, message);
    }

    private Guid GetUserIdFromClaims()
    {
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            throw new GraphQLException("Unauthenticated");

        var uidClaim = user.FindFirst("uid")?.Value;
        if (!Guid.TryParse(uidClaim, out var id))
            throw new GraphQLException("Invalid uid claim in token");

        return id;
    }
}
