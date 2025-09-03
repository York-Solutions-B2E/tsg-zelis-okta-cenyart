using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Blazor + Razor Pages
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// HttpContextAccessor + JWT AccessTokenHandler
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AccessTokenHandler>();

// Named HttpClient for backend GraphQL calls
builder.Services.AddHttpClient("Backend", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Backend:BaseUrl"] ?? "https://localhost:5001/"
    );
})
.AddHttpMessageHandler<AccessTokenHandler>();

// Typed clients
builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("Backend");
});
builder.Services.AddScoped<ProvisioningClient>();
builder.Services.AddScoped<GraphQLService>();

// JWT helper
builder.Services.AddSingleton<JwtUtils>();

// Authentication: Cookie + OpenID Connect (Okta)
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie()
.AddOpenIdConnect("Okta", options =>
{
    options.Authority = builder.Configuration["Okta:OktaDomain"] ?? "https://integrator-7281285.okta.com";
    options.ClientId = builder.Configuration["Okta:ClientId"];
    options.ClientSecret = builder.Configuration["Okta:ClientSecret"];
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.SaveTokens = true; // persist tokens in cookie
    options.GetClaimsFromUserInfoEndpoint = true;
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");

    options.Events = new OpenIdConnectEvents
    {
        OnTokenValidated = async ctx =>
        {
            try
            {
                var principal = ctx.Principal;
                var sub = principal?.FindFirst("sub")?.Value ??
                          principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var email = principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? string.Empty;

                if (!string.IsNullOrEmpty(sub))
                {
                    // Call backend GraphQL provisioning mutation → get JWT
                    using var scope = ctx.HttpContext.RequestServices.CreateScope();
                    var provClient = scope.ServiceProvider.GetRequiredService<ProvisioningClient>();

                    var jwt = await provClient.ProvisionOnLoginAsync(
                        sub, email, "Okta", ctx.HttpContext.RequestAborted
                    );

                    // Store JWT in cookie auth properties for AccessTokenHandler
                    var authResult = await ctx.HttpContext.AuthenticateAsync();
                    var props = authResult.Properties ?? new AuthenticationProperties();
                    var tokens = props.GetTokens()?.ToList() ?? new List<AuthenticationToken>();

                    tokens.RemoveAll(t => t.Name == "access_token");
                    tokens.Add(new AuthenticationToken { Name = "access_token", Value = jwt });

                    props.StoreTokens(tokens);

                    // Re-issue auth cookie with updated tokens
                    await ctx.HttpContext.SignInAsync(authResult.Principal!, props);
                }
            }
            catch (Exception ex)
            {
                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "Provisioning failed during OnTokenValidated");
                // allow login to continue even if provisioning fails
            }
        }
    };
});

// UI-level authorization (AuthorizeView, [Authorize] in Blazor)
builder.Services.AddAuthorizationCore();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
