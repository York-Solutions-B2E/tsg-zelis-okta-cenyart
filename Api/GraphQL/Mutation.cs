using Api.Data;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Shared;

namespace Api.GraphQL;

[ExtendObjectType("Mutation")]
public class ProvisioningMutations(ProvisioningService prov)
{
    private readonly ProvisioningService _prov = prov;

    public async Task<string> ProvisionOnLoginAsync(string externalId, string email, string provider, CancellationToken ct = default)
    {
        return await _prov.ProvisionOnLoginAsync(externalId, email, provider, ct);
    }
}

[ExtendObjectType("Mutation")]
public class Mutation(IHttpContextAccessor http, SecurityEventService events, RoleService roles)
{
    private readonly IHttpContextAccessor _http = http;
    private readonly SecurityEventService _events = events;
    private readonly RoleService _roles = roles;

    [Authorize]
    public async Task<SecurityEvent> AddSecurityEventAsync(string eventType, Guid affectedUserId, string? details = null, CancellationToken ct = default)
    {
        var authorUserId = GetUserIdFromClaims();
        return await _events.CreateEventAsync(eventType, authorUserId, affectedUserId, details, ct);
    }

    [Authorize(Policy = "CanViewRoleChanges")]
    public async Task<AssignRoleResultDto> AssignUserRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default)
    {
        var authorUserId = GetUserIdFromClaims();

        var (success, message, oldRole, newRole) = await _roles.UpdateUserRoleAsync(userId, roleId, ct);
        if (!success)
            throw new GraphQLException(message);

        // Add RoleAssigned event after successful update
        await _events.CreateRoleAssignedAsync(authorUserId, userId, oldRole, newRole, ct);

        return new AssignRoleResultDto(true, message);
    }

    private Guid GetUserIdFromClaims()
    {
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            throw new GraphQLException("Unauthenticated");

        if (!Guid.TryParse(user.FindFirst("uid")?.Value, out var id))
            throw new GraphQLException("Invalid uid claim in token");

        return id;
    }
}
