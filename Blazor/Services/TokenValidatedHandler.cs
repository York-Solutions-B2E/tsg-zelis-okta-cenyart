using System.IdentityModel.Tokens.Jwt;
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

                // Persist JWT and keep Okta id_token
                var authResult = await ctx.HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                var principalToUse = authResult.Principal ?? ctx.Principal;

                var props = authResult.Properties ?? new AuthenticationProperties();
                var currentTokens = props.GetTokens()?.ToList() ?? new List<AuthenticationToken>();

                // keep any id_token already present
                var idToken = ctx.TokenEndpointResponse?.IdToken
                              ?? currentTokens.FirstOrDefault(t => t.Name == "id_token")?.Value;

                // replace our own access_token with backend-issued JWT
                currentTokens.RemoveAll(t => string.Equals(t.Name, "access_token", StringComparison.OrdinalIgnoreCase));
                currentTokens.Add(new AuthenticationToken { Name = "access_token", Value = jwt });

                // re-add the id_token if we found one
                if (!string.IsNullOrEmpty(idToken) && !currentTokens.Any(t => t.Name == "id_token"))
                {
                    currentTokens.Add(new AuthenticationToken { Name = "id_token", Value = idToken });
                }

                props.StoreTokens(currentTokens);

                if (principalToUse != null)
                {
                    await ctx.HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        principalToUse,
                        props);
                }

                _logger.LogInformation("Provisioning completed. Stored JWT + preserved Okta id_token for externalId={Sub}", sub);
            }
            catch (Exception ex)
            {
                // Last-resort catch-all to avoid breaking login flow
                _logger.LogError(ex, "Unexpected error in TokenValidatedHandler.HandleAsync");
            }
        }

        public async Task LoginSuccessEvent(HttpContext httpContext, CancellationToken ct = default)
        {
            try
            {
                var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                var props = authResult.Properties;

                if (props == null)
                {
                    _logger.LogWarning("LoginSuccessEvent: No authentication properties found.");
                    return;
                }

                var tokens = props.GetTokens();
                var accessToken = tokens?.FirstOrDefault(t => t.Name == "access_token")?.Value;

                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.LogWarning("LoginSuccessEvent: No backend access_token found.");
                    return;
                }

                var handler = new JwtSecurityTokenHandler();
                var token = handler.ReadJwtToken(accessToken);

                var uidClaim = token.Claims.FirstOrDefault(c => c.Type == "uid")?.Value;
                var providerClaim = token.Claims.FirstOrDefault(c => c.Type == "provider")?.Value;

                if (!string.IsNullOrEmpty(uidClaim))
                {
                    await _mutationService.AddSecurityEventAsync(
                        "LoginSuccess",
                        Guid.Parse(uidClaim),
                        $"provider={providerClaim}",
                        ct
                    );

                    _logger.LogDebug("LoginSuccess Event created for user {Uid}", uidClaim);
                }
                else
                {
                    _logger.LogWarning("LoginSuccessEvent: 'uid' claim missing in backend JWT.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log LoginSuccess event from backend JWT");
            }
        }
    }
}
