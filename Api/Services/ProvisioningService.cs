using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Api.Data;
using Claim = System.Security.Claims.Claim;

namespace Api.Services;

public class ProvisioningService(AppDbContext db, IConfiguration config, SecurityEventService events)
{
    private readonly AppDbContext _db = db;
    private readonly IConfiguration _config = config;
    private readonly SecurityEventService _events = events;

    /// <summary>
    /// Ensures the user exists, writes a LoginSuccess event, and returns a JWT token containing user, role and permissions claims.
    /// </summary>
    public async Task<string> ProvisionOnLoginAsync(string externalId, string email, string provider, CancellationToken ct = default)
    {
        // Try to find existing user (load role+claims)
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

            // reload role and claims
            await _db.Entry(user).Reference(u => u.Role).LoadAsync(ct);
            await _db.Entry(user.Role).Collection(r => r.Claims).LoadAsync(ct);
        }

        // Create LoginSuccess event via service
        await _events.CreateLoginSuccessAsync(user.Id, provider, ct);

        // Build claims for JWT
        var claims = new List<Claim>
        {
            new Claim("uid", user.Id.ToString()),
            new Claim("email", user.Email ?? string.Empty),
            new Claim("role", user.Role?.Name ?? "BasicUser")
        };

        if (user.Role?.Claims != null)
        {
            foreach (var c in user.Role.Claims)
            {
                if (!string.IsNullOrWhiteSpace(c.Value))
                    claims.Add(new Claim("permissions", c.Value));
            }
        }

        return CreateJwt(claims);
    }

    private string CreateJwt(IEnumerable<Claim> claims)
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
