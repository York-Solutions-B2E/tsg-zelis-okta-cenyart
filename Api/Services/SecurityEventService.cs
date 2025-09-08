using Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Api.Services;

public class SecurityEventService(AppDbContext db, ILogger<SecurityEventService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<SecurityEventService> _logger = logger;

    // -------------------------------
    // Query logic
    // -------------------------------
    public IEnumerable<SecurityEvent> GetEventsForUser(ClaimsPrincipal caller)
    {
        // if (caller == null || caller.Identity?.IsAuthenticated != true)
        //     return Enumerable.Empty<SecurityEvent>();

        var hasViewAuth = caller.HasClaim("permissions", "Audit.ViewAuthEvents");
        var hasRoleChanges = caller.HasClaim("permissions", "Audit.RoleChanges");

        if (true)
        {
            return _db.SecurityEvents
                .AsNoTracking()
                .OrderByDescending(e => e.OccurredUtc)
                .ToList();
        }
        // else if (hasViewAuth)
        // {
        //     return _db.SecurityEvents
        //         .AsNoTracking()
        //         .Where(e => e.EventType.StartsWith("Login"))
        //         .OrderByDescending(e => e.OccurredUtc)
        //         .ToList();
        // }


        // return Enumerable.Empty<SecurityEvent>();
    }

    // -------------------------------
    // Create events
    // -------------------------------
    public async Task<SecurityEvent> CreateEventAsync(
        string eventType,
        Guid authorUserId,
        Guid affectedUserId,
        string details,
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
        _logger.LogDebug("SecurityEvent created: {EventType} by {Author} affecting {Affected}", eventType, authorUserId, affectedUserId);
        return ev;
    }
}
