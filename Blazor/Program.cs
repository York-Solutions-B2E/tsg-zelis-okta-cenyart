using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Blazor.Services;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// UI
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// HttpContextAccessor + AccessToken handler for outgoing API calls
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AccessTokenHandler>();

builder.Services.AddHttpClient("Backend", http =>
{
    http.BaseAddress = new Uri("https://localhost:7188");
})
.AddHttpMessageHandler<AccessTokenHandler>();

builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Backend"));
builder.Services.AddScoped<QueryService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<MutationService>();
builder.Services.AddScoped<TokenValidatedHandler>();

// Authentication: Cookie principal for browser sessions, two OIDC providers (Okta + Google)
// Default scheme is cookie, default challenge will be Okta (so Challenge() will go to Okta).
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = "Okta"; // default provider
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
    {
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromHours(1);
    })
    .AddOpenIdConnect("Okta", o =>
    {
        o.Authority = builder.Configuration["Okta:Authority"] ?? "https://integrator-7281285.okta.com/oauth2/default";
        o.ClientId = builder.Configuration["Okta:ClientId"];
        o.ClientSecret = builder.Configuration["Okta:ClientSecret"];
        o.ResponseType = OpenIdConnectResponseType.Code;
        o.UsePkce = true;
        o.SaveTokens = true;
        o.Scope.Clear();
        o.Scope.Add("openid");
        o.Scope.Add("profile");
        o.Scope.Add("email");
        o.GetClaimsFromUserInfoEndpoint = true;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "name"
        };
        o.SignedOutRedirectUri = "https://localhost:5001/signout/callback";
        o.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = async ctx =>
            {
                var handler = ctx.HttpContext.RequestServices.GetRequiredService<TokenValidatedHandler>();
                await handler.HandleAsync(ctx);

                var account = ctx.HttpContext.RequestServices.GetRequiredService<AccountService>();
                await account.LoginSuccessEvent(ctx.Principal, ctx.HttpContext.RequestAborted);
            },
            OnRedirectToIdentityProviderForSignOut = async ctx =>
            {
                var uidClaim = ctx.HttpContext.User?.FindFirst("uid")?.Value;
                Guid? uid = Guid.TryParse(uidClaim, out var parsed) ? parsed : null;

                var idToken = await ctx.HttpContext.GetTokenAsync("id_token");
                if (!string.IsNullOrEmpty(idToken))
                {
                    ctx.ProtocolMessage.IdTokenHint = idToken;
                }
                ctx.ProtocolMessage.PostLogoutRedirectUri = o.SignedOutRedirectUri;

                var account = ctx.HttpContext.RequestServices.GetRequiredService<AccountService>();
                await account.LogoutEvent(uid);
            }
        };
    })
    .AddOpenIdConnect("Google", o =>
    {
        o.Authority = "https://accounts.google.com";
        o.ClientId = builder.Configuration["Google:ClientId"];
        o.ClientSecret = builder.Configuration["Google:ClientSecret"];
        o.CallbackPath = "/signin-google";
        o.ResponseType = OpenIdConnectResponseType.Code;
        o.UsePkce = true;
        o.SaveTokens = true;
        o.Scope.Clear();
        o.Scope.Add("openid");
        o.Scope.Add("profile");
        o.Scope.Add("email");
        o.GetClaimsFromUserInfoEndpoint = true;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "name"
        };
        o.SignedOutRedirectUri = "https://localhost:5001/";
        o.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = async ctx =>
            {
                var handler = ctx.HttpContext.RequestServices.GetRequiredService<TokenValidatedHandler>();
                await handler.HandleAsync(ctx);

                var account = ctx.HttpContext.RequestServices.GetRequiredService<AccountService>();
                await account.LoginSuccessEvent(ctx.Principal, ctx.HttpContext.RequestAborted);
            },
            OnRedirectToIdentityProviderForSignOut = async ctx =>
            {
                var uidClaim = ctx.HttpContext.User?.FindFirst("uid")?.Value;
                Guid? uid = Guid.TryParse(uidClaim, out var parsed) ? parsed : null;

                ctx.ProtocolMessage.PostLogoutRedirectUri = o.SignedOutRedirectUri;

                var account = ctx.HttpContext.RequestServices.GetRequiredService<AccountService>();
                await account.LogoutEvent(uid);
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CanViewAuthEvents", p => p.RequireClaim("permissions", "Audit.ViewAuthEvents"))
    .AddPolicy("CanViewRoleChanges", p => p.RequireClaim("permissions", "Audit.RoleChanges"));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("Blazor", LogLevel.Debug);

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
