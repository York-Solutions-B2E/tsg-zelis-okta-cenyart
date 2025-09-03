using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Blazor
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// HttpContextAccessor + AccessTokenHandler for outgoing API calls
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AccessTokenHandler>();

// Typed ProvisioningClient: HttpClient will attach AccessTokenHandler which reads JWT from cookie
builder.Services.AddHttpClient<ProvisioningClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Backend:BaseUrl"] ?? "https://localhost:5001");
}).AddHttpMessageHandler<AccessTokenHandler>();

// If you need generic HttpClient (fallback)
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("default"));

builder.Services
    .AddScoped<GraphQLService>()
    .AddScoped<ProvisioningClient>();

// Authentication (cookie + OIDC)
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
    options.SaveTokens = true; // tokens are stored in cookie auth properties
    options.GetClaimsFromUserInfoEndpoint = true;
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");

    // On token validated: call backend to provision and store returned JWT in cookie tokens
    options.Events = new OpenIdConnectEvents
    {
        OnTokenValidated = async ctx =>
        {
            try
            {
                var principal = ctx.Principal;
                var sub = principal?.FindFirst("sub")?.Value ?? principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var email = principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? string.Empty;

                if (!string.IsNullOrEmpty(sub))
                {
                    // Resolve ProvisioningClient from request services and call provisioning mutation.
                    using var scope = ctx.HttpContext.RequestServices.CreateScope();
                    var prov = scope.ServiceProvider.GetRequiredService<ProvisioningClient>();

                    // GraphQL mutation returns JWT
                    var jwt = await prov.ProvisionOnLoginAsync(sub, email, "Okta", ctx.HttpContext.RequestAborted);

                    // Persist JWT into cookie auth tokens (access_token)
                    var current = await ctx.HttpContext.AuthenticateAsync();
                    var props = current.Properties ?? new AuthenticationProperties();
                    var tokens = props.GetTokens()?.ToList() ?? new List<AuthenticationToken>();

                    // remove previous access_token if any, then add new one
                    tokens.RemoveAll(t => t.Name == "access_token");
                    tokens.Add(new AuthenticationToken { Name = "access_token", Value = jwt });

                    props.StoreTokens(tokens);

                    // Re-issue the principal cookie with updated tokens so AccessTokenHandler can read it later
                    await ctx.HttpContext.SignInAsync(current.Principal!, props);
                }
            }
            catch (Exception ex)
            {
                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "Provisioning failed during OnTokenValidated");
                // do not block sign-in
            }
        }
    };
});

builder.Services.AddAuthorizationCore(); // for UI AuthorizeView etc.

// Add MVC for AccountController endpoint
builder.Services.AddControllers();

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

app.MapControllers(); // AccountController routes
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
