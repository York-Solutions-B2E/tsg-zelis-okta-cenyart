using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Blazor.Services
{
    /// <summary>
    /// Handles OnTokenValidated event: calls backend provisioning mutation and stores returned JWT
    /// into cookie auth tokens (access_token). Does not throw; logs errors for debugging.
    /// </summary>
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
                          ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                var email = principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                            ?? principal.FindFirst("email")?.Value ?? string.Empty;

                var provider = ctx.Scheme?.Name ?? "Unknown";

                if (string.IsNullOrEmpty(sub))
                {
                    _logger.LogWarning("TokenValidated: missing 'sub' claim; skipping provisioning.");
                    return;
                }

                _logger.LogDebug("TokenValidated: provisioning user externalId={Sub}, email={Email}, provider={Provider}", sub, email, provider);

                string jwt;
                try
                {
                    jwt = await _mutationService.ProvisionOnLoginAsync(sub, email, provider, ctx.HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    // Log the exact provisioning failure including thrown message (which includes status/body).
                    _logger.LogError(ex, "Provisioning mutation failed for externalId={Sub} provider={Provider}: {Message}", sub, provider, ex.Message);
                    return; // do not block signin
                }

                if (string.IsNullOrWhiteSpace(jwt))
                {
                    _logger.LogWarning("Provisioning returned an empty JWT for externalId={Sub}", sub);
                    return;
                }

                // Persist JWT to cookie auth tokens so AccessTokenHandler can attach to outgoing requests
                var authResult = await ctx.HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                var principalToUse = authResult.Principal ?? ctx.Principal;

                var props = authResult.Properties ?? new AuthenticationProperties();
                var currentTokens = props.GetTokens()?.ToList() ?? new List<AuthenticationToken>();

                // Replace any existing access_token value
                currentTokens.RemoveAll(t => string.Equals(t.Name, "access_token", StringComparison.OrdinalIgnoreCase));
                currentTokens.Add(new AuthenticationToken { Name = "access_token", Value = jwt });

                props.StoreTokens(currentTokens);

                if (principalToUse != null)
                {
                    await ctx.HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principalToUse, props);
                }

                _logger.LogInformation("Provisioning completed and JWT stored for externalId={Sub}", sub);
            }
            catch (Exception ex)
            {
                // Last-resort catch-all to avoid breaking login flow
                _logger.LogError(ex, "Unexpected error in TokenValidatedHandler.HandleAsync");
            }
        }
    }
}
