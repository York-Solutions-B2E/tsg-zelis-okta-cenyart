using Microsoft.EntityFrameworkCore;
using Api.Data;

namespace Api.Services;

/// <summary>
/// Provisions a user (create if missing), writes a LoginSuccess event, and issues a JWT via TokenService.
/// Returns the JWT string (signed).
/// </summary>
public class ProvisioningService(AppDbContext db, TokenService tokens)
{
    private readonly AppDbContext _db = db;
    private readonly TokenService _tokens = tokens;

    /// <summary>
    /// Provision (or find) a user and issue a signed JWT containing role + permissions.
    /// Returns the JWT string.
    /// </summary>
    public async Task<string> ProvisionAndIssueTokenAsync(string externalId, string email, string provider, CancellationToken ct = default)
    {
        var user = await _db.Users.Include(u => u.Role).ThenInclude(r => r.Claims)
            .FirstOrDefaultAsync(u => u.ExternalId == externalId, ct);

        if (user == null)
        {
            var basic = await _db.Roles.FirstAsync(r => r.Name == "BasicUser", ct);
            user = new User { Id = Guid.NewGuid(), ExternalId = externalId, Email = email, RoleId = basic.Id };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);

            // reload role + claims
            await _db.Entry(user).Reference(u => u.Role).LoadAsync(ct);
            await _db.Entry(user.Role!).Collection(r => r.Claims).LoadAsync(ct);
        }

        // write login success event
        var ev = new SecurityEvent
        {
            Id = Guid.NewGuid(),
            EventType = "LoginSuccess",
            AuthorUserId = user.Id,
            AffectedUserId = user.Id,
            OccurredUtc = DateTime.UtcNow,
            Details = $"provider={provider}"
        };
        _db.SecurityEvents.Add(ev);
        await _db.SaveChangesAsync(ct);

        var perms = user.Role?.Claims.Where(c => c.Type == "permissions").Select(c => c.Value).ToList() ?? new List<string>();

        var jwt = _tokens.CreateToken(user.Id, user.Email, user.Role?.Name ?? "BasicUser", perms);
        return jwt;
    }
}
