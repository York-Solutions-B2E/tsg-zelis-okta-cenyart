using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.Services;
using Shared;
using System.Security.Claims;

namespace Api.GraphQL;

public class Query
{
    [Authorize]
    public async Task<List<UserDto>> GetUsersAsync([Service] AppDbContext db, CancellationToken ct = default)
    {
        var users = await db.Users
            .Include(u => u.Role)
                .ThenInclude(r => r.Claims)
            .ToListAsync(ct);

        var result = users.Select(u => new UserDto(
            u.Id,
            u.ExternalId,
            u.Provider,
            u.Email,
            new RoleDto(u.Role.Id, u.Role.Name),
            u.Role.Claims.Select(c => new ClaimDto(c.Type, c.Value))
        )).ToList();

        return result;
    }

    [Authorize]
    public async Task<List<RoleDto>> GetRolesAsync([Service] AppDbContext db)
    {
        var roles = await db.Roles.ToListAsync();
        return roles.Select(r => new RoleDto(r.Id, r.Name)).ToList();
    }

    [Authorize]
    public IEnumerable<SecurityEventDto> GetSecurityEvents(
        [Service] IHttpContextAccessor http,
        [Service] SecurityEventService securityEvents)
    {
        var user = http.HttpContext?.User ?? new ClaimsPrincipal();
        return securityEvents.GetEventsForUser(user)
            .Select(e => new SecurityEventDto(
                e.Id,
                e.EventType,
                e.AuthorUserId,
                e.AffectedUserId,
                e.OccurredUtc,
                e.Details
            ));
    }
}
