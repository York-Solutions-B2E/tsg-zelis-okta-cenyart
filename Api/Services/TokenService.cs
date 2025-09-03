using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Api.Services;

public class TokenService
{
    private readonly IConfiguration _cfg;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiresMinutes;

    public TokenService(IConfiguration cfg)
    {
        _cfg = cfg;
        var key = _cfg["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key missing from configuration");
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        _issuer = _cfg["Jwt:Issuer"] ?? "api";
        _audience = _cfg["Jwt:Audience"] ?? "client";
        _expiresMinutes = int.TryParse(_cfg["Jwt:ExpiresMinutes"], out var m) ? m : 60;
    }

    public string CreateToken(Guid userId, string email, string roleName, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.Name, email),
            new Claim("uid", userId.ToString()),
            new Claim(ClaimTypes.Role, roleName)
        };

        foreach (var p in permissions.Distinct())
            claims.Add(new Claim("permissions", p));

        var creds = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expiresMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
