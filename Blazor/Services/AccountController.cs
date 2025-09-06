using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blazor.Services;

[AllowAnonymous]
[Route("Account")]
public class AccountController(IConfiguration config, ILogger<AccountController> logger) : Controller
{
    private readonly IConfiguration _config = config;
    private readonly ILogger<AccountController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // GET /Account/SignInUser/{provider}?returnUrl=/somewhere
    [HttpGet("SignInUser/{provider}")]
    public IActionResult SignInUser(string provider, string? returnUrl = "/")
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider must be specified (Okta or Google).");

        var props = new AuthenticationProperties { RedirectUri = returnUrl };
        return Challenge(props, provider); // provider must match the scheme name ("Okta" or "Google")
    }

    // GET /Account/SignOutUser
    [HttpGet("SignOutUser")]
    public async Task<IActionResult> SignOutUser()
    {
        // Authenticate and get cookie properties
        var authResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var props = authResult.Properties;

        // Get backend JWT from cookie tokens
        var tokens = props.GetTokens();
        var accessToken = tokens?.FirstOrDefault(t => t.Name == "access_token")?.Value;

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Backend JWT (access_token) is missing.");

        // Read claims from backend JWT
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(accessToken);

        var uidClaim = token.Claims.FirstOrDefault(c => c.Type == "uid")?.Value;
        var provider = token.Claims.FirstOrDefault(c => c.Type == "provider")?.Value;

        if (string.IsNullOrWhiteSpace(provider))
            throw new InvalidOperationException("Provider claim is missing in backend JWT.");
        if (string.IsNullOrWhiteSpace(uidClaim))
            throw new InvalidOperationException("UID claim is missing in backend JWT.");

        var uid = Guid.Parse(uidClaim);

        // Remove backend JWT from cookie tokens
        var tokenList = props.GetTokens()?.ToList() ?? new List<AuthenticationToken>();
        tokenList.RemoveAll(t => string.Equals(t.Name, "access_token", StringComparison.OrdinalIgnoreCase));
        props.StoreTokens(tokenList);

        if (authResult.Principal != null)
        {
            // Re-issue cookie without JWT
            await HttpContext.SignInAsync(authResult.Principal, props);
        }

        // Grab Okta id_token if available
        var idToken = await HttpContext.GetTokenAsync("id_token");

        // Sign out locally
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignOutAsync(provider);

        // Log security event using backend JWT claims
        var mutationService = HttpContext.RequestServices.GetRequiredService<MutationService>();
        await mutationService.AddSecurityEventAsync(
            "Logout",
            uid,
            "local sign-out",
            HttpContext.RequestAborted
        );

        _logger.LogDebug("Logout Event created for user {Uid} using backend JWT claims", uid);

        // Okta logout redirect if needed
        if (provider.Equals("Okta", StringComparison.OrdinalIgnoreCase))
        {
            var clientId = _config["Okta:ClientId"];
            var oktaDomain = _config["Okta:OktaDomain"] ?? "https://integrator-7281285.okta.com";

            var postLogoutRedirect = "https://localhost:5001/signout/callback";

            var oktaLogoutUrl = $"{oktaDomain}/oauth2/default/v1/logout" +
                                $"?id_token_hint={Uri.EscapeDataString(idToken)}" +
                                $"&post_logout_redirect_uri={Uri.EscapeDataString(postLogoutRedirect)}" +
                                $"&client_id={Uri.EscapeDataString(clientId)}";

            return Redirect(oktaLogoutUrl);
        }

        return Redirect("/");
    }
}
