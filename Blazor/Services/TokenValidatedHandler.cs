using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Shared;

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
            if (principal == null)
            {
                _logger.LogWarning("TokenValidated: principal is null.");
                return;
            }

            var sub = principal.FindFirst("sub")?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = principal.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
            var provider = ctx.Scheme?.Name ?? "Unknown";

            if (string.IsNullOrEmpty(sub))
            {
                _logger.LogWarning("TokenValidated: missing 'sub' claim; skipping provisioning.");
                return;
            }

            // Call backend provisioning
            var dto = await _mutationService.ProvisionOnLoginAsync(
                sub,
                email,
                provider,
                ctx.HttpContext.RequestAborted);

            if (dto?.User == null)
            {
                _logger.LogWarning("Provisioning returned null User for externalId={Sub}", sub);
                return;
            }

            var identity = principal.Identity as ClaimsIdentity ?? new ClaimsIdentity();

            // Add UID
            identity.AddClaim(new Claim("uid", dto.User.Id.ToString()));

            // Add role
            if (dto.User.Role != null && !string.IsNullOrEmpty(dto.User.Role.Name))
                identity.AddClaim(new Claim("role", dto.User.Role.Name));

            // Add provider
            identity.AddClaim(new Claim("provider", provider));

            // Add permissions/claims from backend
            if (dto.User.Claims != null)
            {
                foreach (var c in dto.User.Claims)
                {
                    identity.AddClaim(new Claim(c.Type, c.Value));
                }
            }

            // Replace cookie with updated principal
            var newPrincipal = new ClaimsPrincipal(identity);
            await ctx.HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                newPrincipal);

            _logger.LogInformation("Provisioning completed. Claims added for {UserId}", dto.User.Id);

            // Log LoginSuccess
            await LoginSuccessEvent(ctx.HttpContext, ctx.HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in TokenValidatedHandler.HandleAsync");
        }
    }

    /// <summary>
    /// Logs a LoginSuccess security event using claims from the current principal.
    /// Can be called after any provider login (Okta, Google, etc.).
    /// </summary>
    public async Task LoginSuccessEvent(HttpContext httpContext, CancellationToken ct = default)
    {
        try
        {
            var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = authResult.Principal;

            if (principal == null)
            {
                _logger.LogWarning("LoginSuccessEvent: no principal found.");
                return;
            }

            var uidClaim = principal.FindFirst("uid")?.Value;
            var providerClaim = principal.FindFirst("provider")?.Value ?? "Unknown";

            if (!string.IsNullOrEmpty(uidClaim))
            {
                await _mutationService.AddSecurityEventAsync(
                    eventType: "LoginSuccess",
                    authorUserId: Guid.Parse(uidClaim),
                    affectedUserId: Guid.Parse(uidClaim),
                    details: $"provider={providerClaim}",
                    ct: ct
                );

                _logger.LogDebug("LoginSuccess Event created for user {Uid}", uidClaim);
            }
            else
            {
                _logger.LogWarning("LoginSuccessEvent: missing 'uid' claim.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log LoginSuccess event from claims");
        }
    }
}
