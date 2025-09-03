using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blazor.Services;

[AllowAnonymous]
[Route("Account")]
public class AccountController(IConfiguration config) : Controller
{
    private readonly IConfiguration _config = config;

    // GET /Account/OktaSignIn?returnUrl=/somewhere
    [HttpGet("OktaSignIn")]
    public IActionResult OktaSignIn(string? returnUrl = "/")
    {
        var props = new AuthenticationProperties { RedirectUri = returnUrl };
        return Challenge(props, OpenIdConnectDefaults.AuthenticationScheme);
    }

    // GET /Account/OktaSignOut
    [HttpGet("OktaSignOut")]
    public async Task<IActionResult> OktaSignOut()
    {
        // Get id_token from current session
        var idToken = await HttpContext.GetTokenAsync("id_token");
        if (string.IsNullOrEmpty(idToken))
            throw new InvalidOperationException("id_token is missing from the current session.");

        // Get Okta config
        var clientId = _config["Okta:ClientId"];
        var oktaDomain = _config["Okta:OktaDomain"];
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(oktaDomain))
            throw new InvalidOperationException("Okta configuration missing: OktaDomain or ClientId is null or empty.");

        // Sign out locally first
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);

        var postLogoutRedirect = "https://localhost:5001/signout/callback";

        var oktaLogoutUrl = $"{oktaDomain}/oauth2/default/v1/logout" +
                             $"?id_token_hint={Uri.EscapeDataString(idToken)}" +
                             $"&post_logout_redirect_uri={Uri.EscapeDataString(postLogoutRedirect)}" +
                             $"&client_id={Uri.EscapeDataString(clientId)}";

        return Redirect(oktaLogoutUrl);
    }

    // GET /signout/callback
    [HttpGet("/signout/callback")]
    public IActionResult SignOutCallback()
    {
        // After Okta logout, redirect to home or login page
        return Redirect("/");
    }
}