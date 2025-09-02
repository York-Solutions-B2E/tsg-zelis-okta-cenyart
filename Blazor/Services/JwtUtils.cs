using System.IdentityModel.Tokens.Jwt;

namespace Blazor.Services;

public class JwtUtils
{
    private readonly JwtSecurityTokenHandler _handler = new JwtSecurityTokenHandler();

    public IDictionary<string, string> ReadClaims(string jwt)
    {
        var res = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(jwt)) return res;

        try
        {
            var token = _handler.ReadJwtToken(jwt);
            foreach (var c in token.Claims)
            {
                // last-one-wins string representation
                res[c.Type] = c.Value;
            }
        }
        catch
        {
            // invalid token format — ignore
        }

        return res;
    }

    public string? GetClaim(string jwt, string claimType)
    {
        var map = ReadClaims(jwt);
        return map.TryGetValue(claimType, out var v) ? v : null;
    }

    public IEnumerable<string> GetPermissions(string jwt)
    {
        if (string.IsNullOrEmpty(jwt)) return Enumerable.Empty<string>();
        var token = _handler.ReadJwtToken(jwt);
        return token.Claims.Where(c => c.Type == "permissions").Select(c => c.Value);
    }
}
