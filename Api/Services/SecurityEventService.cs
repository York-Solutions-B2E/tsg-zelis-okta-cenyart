using Api.Data;
using System.Security.Claims;

namespace Api.Services;

public class SecurityEventService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    // -------------------------------
    // Query logic
    // -------------------------------
    public IQueryable<SecurityEvent> GetEventsForUser(ClaimsPrincipal user)
    {
        if (user == null || !user.Identity?.IsAuthenticated == true)
            return Enumerable.Empty<SecurityEvent>().AsQueryable();

        var hasViewAuth = user.HasClaim("permissions", "Audit.ViewAuthEvents");
        var hasRoleChanges = user.HasClaim("permissions", "Audit.RoleChanges");

        if (hasRoleChanges)
        {
            return _db.SecurityEvents
                .OrderByDescending(e => e.OccurredUtc);
        }
        else if (hasViewAuth)
        {
            return _db.SecurityEvents
                .Where(e => e.EventType.StartsWith("Login"))
                .OrderByDescending(e => e.OccurredUtc);
        }

        return Enumerable.Empty<SecurityEvent>().AsQueryable();
    }

    // -------------------------------
    // Create events
    // -------------------------------
    public async Task<SecurityEvent> CreateEventAsync(
        string eventType,
        Guid authorUserId,
        Guid affectedUserId,
        string? details = null,
        CancellationToken ct = default)
    {
        var ev = new SecurityEvent
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            AuthorUserId = authorUserId,
            AffectedUserId = affectedUserId,
            Details = details,
            OccurredUtc = DateTime.UtcNow
        };

        _db.SecurityEvents.Add(ev);
        await _db.SaveChangesAsync(ct);
        return ev;
    }

    public Task<SecurityEvent> CreateLoginSuccessAsync(Guid userId, string provider, CancellationToken ct = default)
        => CreateEventAsync("LoginSuccess", userId, userId, $"provider={provider}", ct);

    public Task<SecurityEvent> CreateLogoutAsync(Guid userId, string? details = null, CancellationToken ct = default)
        => CreateEventAsync("Logout", userId, userId, details ?? "local sign-out", ct);

    public Task<SecurityEvent> CreateRoleAssignedAsync(Guid authorUserId, Guid affectedUserId, string from, string to, CancellationToken ct = default)
        => CreateEventAsync("RoleAssigned", authorUserId, affectedUserId, $"from={from} to={to}", ct);
}
