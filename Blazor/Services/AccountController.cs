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
        return Challenge(props, provider);
    }

    // GET /Account/SignOutUser
    [HttpGet("SignOutUser")]
    public async Task<IActionResult> SignOutUser()
    {
        // Determine provider from current cookie claims
        var provider = User.FindFirst("provider")?.Value ?? "Unknown";

        // Get UID from cookie claims for logging
        var uidClaim = User.FindFirst("uid")?.Value;
        Guid? uid = null;
        if (!string.IsNullOrEmpty(uidClaim) && Guid.TryParse(uidClaim, out var parsedUid))
            uid = parsedUid;

        // Sign out locally
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignOutAsync(provider);
        _logger.LogInformation("User signed out locally from {Provider}", provider);

        // Log security event
        if (uid.HasValue)
        {
            var mutationService = HttpContext.RequestServices.GetRequiredService<MutationService>();
            await mutationService.AddSecurityEventAsync(
                eventType: "Logout",
                authorUserId: uid.Value,
                affectedUserId: uid.Value,
                details: "local sign-out",
                ct: HttpContext.RequestAborted
            );
            _logger.LogDebug("Logout event created for user {Uid}", uid);
        }

        // Okta logout redirect
        if (provider.Equals("Okta", StringComparison.OrdinalIgnoreCase))
        {
            var clientId = _config["Okta:ClientId"] ?? "";
            var oktaDomain = _config["Okta:OktaDomain"] ?? "https://integrator-7281285.okta.com";
            var postLogoutRedirect = "https://localhost:5001/signout/callback";

            var idToken = await HttpContext.GetTokenAsync("id_token") ?? "";

            var oktaLogoutUrl = $"{oktaDomain}/oauth2/default/v1/logout" +
                                $"?id_token_hint={Uri.EscapeDataString(idToken)}" +
                                $"&post_logout_redirect_uri={Uri.EscapeDataString(postLogoutRedirect)}" +
                                $"&client_id={Uri.EscapeDataString(clientId)}";

            return Redirect(oktaLogoutUrl);
        }

        // For Google or other providers, redirect home
        return Redirect("/");
    }
}
