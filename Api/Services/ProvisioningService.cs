using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Api.Data;
using Claim = System.Security.Claims.Claim;

namespace Api.Services;

public class ProvisioningService(AppDbContext db, IConfiguration config)
{
    private readonly AppDbContext _db = db;
    private readonly IConfiguration _config = config;

    /// <summary>
    /// Ensures local user exists and returns a JWT that includes role + permissions claims.
    /// </summary>
    public async Task<string> ProvisionOnLoginAsync(string externalId, string email, string provider, CancellationToken ct = default)
    {
        // locate or create user
        var user = await _db.Users
            .Include(u => u.Role)
            .ThenInclude(r => r.Claims)
            .FirstOrDefaultAsync(u => u.ExternalId == externalId, ct);

        if (user == null)
        {
            var basicRole = await _db.Roles.FirstAsync(r => r.Name == "BasicUser", ct);
            user = new User
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                Email = email,
                RoleId = basicRole.Id
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);

            // record LoginSuccess event
            _db.SecurityEvents.Add(new SecurityEvent
            {
                Id = Guid.NewGuid(),
                EventType = "LoginSuccess",
                AuthorUserId = user.Id,
                AffectedUserId = user.Id,
                Details = $"provider={provider}",
                OccurredUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            // existing user - optionally write LoginSuccess too
            _db.SecurityEvents.Add(new SecurityEvent
            {
                Id = Guid.NewGuid(),
                EventType = "LoginSuccess",
                AuthorUserId = user.Id,
                AffectedUserId = user.Id,
                Details = $"provider={provider}",
                OccurredUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            // reload role & claims
            await _db.Entry(user).Reference(u => u.Role).LoadAsync(ct);
            await _db.Entry(user.Role).Collection(r => r.Claims).LoadAsync(ct);
        }

        // build claims list for JWT
        await _db.Entry(user).Reference(u => u.Role).LoadAsync(ct);
        await _db.Entry(user.Role).Collection(r => r.Claims).LoadAsync(ct);

        var claims = new List<Claim>
        {
            new Claim("uid", user.Id.ToString()),
            new Claim("email", user.Email),
            new Claim("role", user.Role?.Name ?? "BasicUser")
        };

        // add permissions as repeated "permissions" claims
        if (user.Role?.Claims != null)
        {
            foreach (var c in user.Role.Claims)
            {
                if (!string.IsNullOrWhiteSpace(c.Value))
                    claims.Add(new Claim("permissions", c.Value));
            }
        }

        var jwt = CreateJwtToken(claims);
        return jwt;
    }

    private string CreateJwtToken(IEnumerable<Claim> claims)
    {
        var issuer = _config["Jwt:Issuer"] ?? "https://example.local";
        var audience = _config["Jwt:Audience"] ?? "api";
        var key = _config["Jwt:Key"] ?? "very-long-secret-key-change-this";

        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
