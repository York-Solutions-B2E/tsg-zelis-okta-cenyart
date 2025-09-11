using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Shared;

namespace Blazor.Services;

public class TokenValidatedHandler(MutationService mutationService, ILogger<TokenValidatedHandler> logger)
{
    private readonly MutationService _mutationService = mutationService ?? throw new ArgumentNullException(nameof(mutationService));
    private readonly ILogger<TokenValidatedHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task HandleAsync(TokenValidatedContext ctx)
    {
        try
        {
            var principal = ctx.Principal;
            if (principal == null)
            {
                _logger.LogWarning("TokenValidated: principal is null.");
                return;
            }

            var sub = principal.FindFirst("sub")?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = principal.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
            var providerFromOidc = ctx.Scheme?.Name ?? "Unknown";

            if (string.IsNullOrEmpty(sub))
            {
                _logger.LogWarning("TokenValidated: missing 'sub' claim; skipping provisioning.");
                return;
            }

            // Call backend provisioning: it returns ProvisionPayload(UserDto)
            var dto = await _mutation_service_provision(sub, email, providerFromOidc, ctx.HttpContext.RequestAborted);

            if (dto?.User == null)
            {
                _logger.LogWarning("Provisioning returned null User for externalId={Sub}", sub);
                return;
            }

            var user = dto.User;

            var identity = principal.Identity as ClaimsIdentity
                           ?? new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);

            // Add UID
            identity.AddClaim(new Claim("uid", user.Id.ToString()));

            // Add role (use ClaimTypes.Role to align with ASP.NET role handling)
            if (user.Role != null && !string.IsNullOrEmpty(user.Role.Name))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.Name));
            }

            // Add provider claim (prefer value from backend dto; fallback to OIDC provider name)
            // var providerToClaim = !string.IsNullOrEmpty(user.Provider) ? user.Provider : providerFromOidc;
            // identity.AddClaim(new Claim("provider", providerToClaim));

            // Add permissions/other claims from backend role/claims
            if (user.Claims != null)
            {
                foreach (var c in user.Claims)
                {
                    // avoid duplicating uid/provider/email/role claims if backend included them
                    identity.AddClaim(new Claim(c.Type, c.Value));
                }
            }

            // Create API JWT (signed with symmetric key that your API validates)
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

            // Remove any existing api_access_token claim and add the new one
            var existing = identity.FindFirst("api_access_token");
            if (existing != null) identity.RemoveClaim(existing);
            identity.AddClaim(new Claim("api_access_token", apiToken));

            // Replace principal and issue refreshed cookie (so UI sees updated claims)
            var newPrincipal = new ClaimsPrincipal(identity);

            // Preserve existing auth properties (if any) to keep RedirectUri etc.
            var authResult = await ctx.HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var props = authResult.Properties ?? new AuthenticationProperties();

            await ctx.HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, newPrincipal, props);

            _logger.LogInformation("Provisioning completed. Custom ApiAccessToken claim created for {UserId}", user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in TokenValidatedHandler.HandleAsync");
        }
    }

    // small wrapper to call mutation (keeps code easier to unit test/mock if needed)
    private async Task<ProvisionPayload?> _mutation_service_provision(string externalId, string email, string provider, CancellationToken ct)
    {
        return await _mutationService.ProvisionOnLoginAsync(externalId, email, provider, ct);
    }

    /// <summary>
    /// Logs a LoginSuccess security event using the supplied principal's claims.
    /// </summary>
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
