using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

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
        var provider = User.FindFirst("provider")?.Value ?? "Unknown";

        // UID for logging
        var uidClaim = User.FindFirst("uid")?.Value;
        Guid? uid = Guid.TryParse(uidClaim, out var parsed) ? parsed : null;

        // Read id_token before signing out
        var idToken = await HttpContext.GetTokenAsync("Okta", "id_token");
        if (string.IsNullOrEmpty(idToken))
            throw new InvalidOperationException("id_token is missing from the current session.");

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
        }

        // Sign out local cookie
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Okta logout redirect
        if (provider.Equals("Okta", StringComparison.OrdinalIgnoreCase))
        {
            var clientId = _config["Okta:ClientId"] ?? "";
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

    [HttpGet("grant-role-change")]
    public async Task<IActionResult> GrantRoleChange(string returnUrl = "/")
    {
        // Ensure returnUrl is local
        if (string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl))
            returnUrl = "/";

        // Authenticate existing cookie
        var auth = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = auth.Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
        var identity = principal.Identity as ClaimsIdentity ?? new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);

        // Add permission claim if missing (for UI)
        if (!identity.HasClaim(c => c.Type == "permissions" && c.Value == "Audit.RoleChanges"))
            identity.AddClaim(new Claim("permissions", "Audit.RoleChanges"));

        // Create API JWT token including all claims
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("this-is-a-very-strong-secret-key-123456"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var apiToken = new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(
                issuer: "your-app",
                audience: "your-api",
                claims: identity.Claims,  // includes permissions now
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds
            )
        );

        // Store API token so AccessTokenHandler can pick it up
        HttpContext.Items["ApiAccessToken"] = apiToken;

        // Re-issue cookie for UI with same AuthenticationProperties
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            auth.Properties ?? new AuthenticationProperties()
        );

        return LocalRedirect(returnUrl);
    }
}
