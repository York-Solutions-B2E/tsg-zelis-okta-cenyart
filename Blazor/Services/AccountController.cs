using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Blazor.Services;

[AllowAnonymous]
[Route("Account")]
public class AccountController(IConfiguration config) : Controller
{
    private readonly IConfiguration _config = config;

    // GET /Account/SignIn/{provider}?returnUrl=/somewhere
    [HttpGet("SignIn/{provider}")]
    public IActionResult SignIn(string provider, string? returnUrl = "/")
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider must be specified (Okta or Google).");

        var props = new AuthenticationProperties { RedirectUri = returnUrl };
        return Challenge(props, provider); // provider must match the scheme name ("Okta" or "Google")
    }

    // GET /Account/SignOut/{provider}
    [HttpGet("SignOut/{provider}")]
    public async Task<IActionResult> SignOut(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider must be specified (Okta or Google).");

        // Remove stored JWT token from cookie auth properties
        var authenticateResult = await HttpContext.AuthenticateAsync();
        var props = authenticateResult.Properties ?? new AuthenticationProperties();
        var tokens = props.GetTokens()?.ToList() ?? new List<AuthenticationToken>();

        tokens.RemoveAll(t => string.Equals(t.Name, "access_token", StringComparison.OrdinalIgnoreCase));
        props.StoreTokens(tokens);

        if (authenticateResult.Principal != null)
        {
            // Re-issue cookie without JWT token
            await HttpContext.SignInAsync(authenticateResult.Principal, props);
        }

        // Grab id_token if available (needed for remote logout)
        var idToken = await HttpContext.GetTokenAsync("id_token");
        if (string.IsNullOrEmpty(idToken))
            throw new InvalidOperationException("id_token is missing from the current session.");

        // Sign out locally
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignOutAsync(provider); // sign out from the chosen provider

        // If Okta, redirect to Okta logout endpoint
        // if (provider.Equals("Okta", StringComparison.OrdinalIgnoreCase))
        // {
        //     var clientId = _config["Okta:ClientId"];
        //     var oktaDomain = _config["Okta:OktaDomain"] ?? "https://integrator-7281285.okta.com";
        //     if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(oktaDomain))
        //         throw new InvalidOperationException("Okta configuration missing: OktaDomain or ClientId is null or empty.");

        //     var postLogoutRedirect = "https://localhost:5001/signout/callback";

        //     var oktaLogoutUrl = $"{oktaDomain}/oauth2/default/v1/logout" +
        //                         $"?id_token_hint={Uri.EscapeDataString(idToken)}" +
        //                         $"&post_logout_redirect_uri={Uri.EscapeDataString(postLogoutRedirect)}" +
        //                         $"&client_id={Uri.EscapeDataString(clientId)}";

        //     return Redirect(oktaLogoutUrl);
        // }

        // Google (and other providers) don’t require special remote logout
        return RedirectToAction("SignOutCallback");
    }

    // GET /Account/SignOutCallback
    [HttpGet("SignOutCallback")]
    public IActionResult SignOutCallback()
    {
        // After remote logout, redirect to home or login page
        return Redirect("/");
    }
}
