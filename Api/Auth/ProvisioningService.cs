using Microsoft.EntityFrameworkCore;
using Api.Data;

namespace Api.Auth;

public class ProvisioningService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public async Task<User> ProvisionOnLoginAsync(string externalId, string email, string provider, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId, ct);
        if (user == null)
        {
            var basicRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "BasicUser", ct)
                ?? throw new InvalidOperationException("BasicUser role not seeded");

            user = new User
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                Email = email,
                RoleId = basicRole.Id
            };

            _db.Users.Add(user);
        }

        var ev = new SecurityEvent
        {
            Id = Guid.NewGuid(),
            EventType = "LoginSuccess",
            AuthorUserId = user.Id,
            AffectedUserId = user.Id,
            Details = $"provider={provider}",
            OccurredUtc = DateTime.UtcNow
        };

        _db.SecurityEvents.Add(ev);
        await _db.SaveChangesAsync(ct);

        return user;
    }
}
