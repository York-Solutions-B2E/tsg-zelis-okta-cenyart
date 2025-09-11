using Microsoft.EntityFrameworkCore;
using Api.Data;
using Shared;

namespace Api.Services;

public class ProvisioningService(AppDbContext db, ILogger<ProvisioningService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<ProvisioningService> _logger = logger;

    /// <summary>
    /// Ensures the user exists, assigns BasicUser if new, writes login event, and returns a UserDto with claims.
    /// </summary>
    public async Task<UserDto> ProvisionOnLoginAsync(
        string externalId,
        string email,
        string provider,
        CancellationToken ct = default)
    {
        _logger.LogInformation("ProvisionOnLoginAsync start externalId={ExternalId} email={Email} provider={Provider}",
            externalId, email, provider);

        var user = await _db.Users
            .Include(u => u.Role)
                .ThenInclude(r => r.Claims)
            .FirstOrDefaultAsync(u => u.ExternalId == externalId && u.Provider == provider, ct);

        if (user == null)
        {
            var basicRole = await _db.Roles.FirstAsync(r => r.Name == "BasicUser", ct);

            user = new User
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                ExternalId = externalId,
                Email = email,
                RoleId = basicRole.Id
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);

            // reload role + claims
            await _db.Entry(user).Reference(u => u.Role).LoadAsync(ct);
            await _db.Entry(user.Role).Collection(r => r.Claims).LoadAsync(ct);

            _logger.LogInformation("Created new user {UserId} with role BasicUser provider={Provider}", user.Id, provider);
        }
        else
        {
            _logger.LogInformation("Found existing user {UserId} role={Role} provider={Provider}", user.Id, user.Role.Name, provider);
        }

        // ---- Convert to DTO with claims ----
        var roleDto = new RoleDto(user.Role.Id, user.Role.Name);
        var claims = user.Role.Claims
            .Select(c => new ClaimDto("permissions", c.Value))
            .ToList();

        claims.Add(new ClaimDto("provider", provider));
        claims.Add(new ClaimDto("email", user.Email));

        var dto = new UserDto(user.Id, user.ExternalId, user.Provider, user.Email, roleDto, claims);

        return dto;
    }
}
