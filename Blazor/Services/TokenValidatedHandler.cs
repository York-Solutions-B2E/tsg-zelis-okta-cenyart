using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Blazor.Services;

public class TokenValidatedHandler(MutationService mutationService, ILogger<TokenValidatedHandler> logger)
{
    private readonly MutationService _mutationService = mutationService;
    private readonly ILogger<TokenValidatedHandler> _logger = logger;

    public async Task HandleAsync(TokenValidatedContext ctx)
    {
        try
        {
            var principal = ctx.Principal;
            if (principal == null) return;

            var sub = principal.FindFirst("sub")?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = principal.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
            var provider = ctx.Scheme?.Name ?? "Unknown";

            if (string.IsNullOrEmpty(sub)) return;

            // Provision user in DB
            var dto = await _mutationService.ProvisionOnLoginAsync(sub, email, provider, ctx.HttpContext.RequestAborted);
            if (dto?.User == null) return;

            var identity = principal.Identity as ClaimsIdentity
                           ?? new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);

            // Add UID, Role, and DB permission claims
            identity.AddClaim(new Claim("uid", dto.User.Id.ToString()));
            if (dto.User.Role != null)
                identity.AddClaim(new Claim(ClaimTypes.Role, dto.User.Role.Name));

            dto.User.Claims?.ToList().ForEach(c =>
                identity.AddClaim(new Claim(c.Type, c.Value))
            );

            // Create API JWT
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("this-is-a-very-strong-secret-key-123456"));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var jwt = new JwtSecurityToken(
                issuer: "your-app",
                audience: "your-api",
                claims: identity.Claims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds
            );

            var apiToken = new JwtSecurityTokenHandler().WriteToken(jwt);

            // remove any old api_access_token claims
            var existingApiTokenClaim = identity.FindFirst("api_access_token");
            if (existingApiTokenClaim != null)
                identity.RemoveClaim(existingApiTokenClaim);

            // add as a claim
            identity.AddClaim(new Claim("api_access_token", apiToken));

            // replace principal with updated identity
            ctx.Principal = new ClaimsPrincipal(identity);

            // issue new cookie with updated claims
            await ctx.HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                ctx.Principal);

            _logger.LogInformation("Provisioning completed. Custom ApiAccessToken claim created for {UserId}", dto.User.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in TokenValidatedHandler.HandleAsync");
        }
    }

    public async Task LoginSuccessEvent(ClaimsPrincipal? principal, CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("LoginSuccessEvent: starting");

            if (principal == null || principal.Identity?.IsAuthenticated != true)
            {
                _logger.LogWarning("LoginSuccessEvent: principal is null or not authenticated.");
                return;
            }

            _logger.LogDebug("LoginSuccessEvent: principal found with {ClaimCount} claims", principal.Claims.Count());

            var uidClaim = principal.FindFirst("uid")?.Value;
            var providerClaim = principal.FindFirst("provider")?.Value ?? "Unknown";

            if (string.IsNullOrEmpty(uidClaim))
            {
                _logger.LogWarning("LoginSuccessEvent: 'uid' claim not found.");
                return;
            }

            _logger.LogDebug("LoginSuccessEvent: creating security event for UID={Uid}, provider={Provider}", uidClaim, providerClaim);

            await _mutationService.AddSecurityEventAsync(
                eventType: "LoginSuccess",
                authorUserId: Guid.Parse(uidClaim),
                affectedUserId: Guid.Parse(uidClaim),
                details: $"provider={providerClaim}",
                ct: ct
            );

            _logger.LogInformation("LoginSuccessEvent: successfully created LoginSuccess event for UID={Uid}", uidClaim);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoginSuccessEvent: failed to log LoginSuccess event");
        }
    }
}
