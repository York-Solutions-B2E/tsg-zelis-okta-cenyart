using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Blazor.Services;

[AllowAnonymous]
[Route("Account")]
public class AccountController(MutationService mutationService, ILogger<AccountController> logger) : Controller
{
    private readonly MutationService _mutationService = mutationService;
    private readonly ILogger<AccountController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // -------------------
    // Sign-in
    // -------------------
    // GET /Account/SignInUser/{provider}?returnUrl=/somewhere
    [HttpGet("SignInUser/{provider}")]
    public IActionResult SignInUser(string provider, string? returnUrl = "/")
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider must be specified (Okta or Google).");

        var props = new AuthenticationProperties { RedirectUri = returnUrl };
        return Challenge(props, provider);
    }

    // -------------------
    // Sign-out
    // -------------------
    [HttpGet("SignOutUser")]
    public IActionResult SignOutUser()
    {
        var provider = User.FindFirst("provider")?.Value;

        if (string.Equals(provider, "Okta", StringComparison.OrdinalIgnoreCase))
        {
            // triggers OpenIdConnect’s federated logout
            return SignOut(
                new AuthenticationProperties { RedirectUri = "/signout/callback" },
                CookieAuthenticationDefaults.AuthenticationScheme,
                "Okta");
        }

        if (string.Equals(provider, "Google", StringComparison.OrdinalIgnoreCase))
        {
            return SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                CookieAuthenticationDefaults.AuthenticationScheme);
        }

        // fallback: local cookie logout
        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // -------------------
    // Okta redirect url
    // -------------------
    [Route("signout/callback")]
    public IActionResult SignoutCallback()
    {
        return Redirect("/");
    }

    // -------------------
    // Dev allow basic user temporary access to role change
    // -------------------
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
