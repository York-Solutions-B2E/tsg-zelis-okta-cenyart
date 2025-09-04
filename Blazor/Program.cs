using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Blazor.Services;

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
builder.Services.AddScoped<MutationService>();
builder.Services.AddScoped<TokenValidatedHandler>();

// Authentication: Cookie principal for browser sessions, two OIDC providers (Okta + Google)
// Default scheme is cookie, default challenge will be Okta (so Challenge() will go to Okta).
builder.Services
  .AddAuthentication(options =>
  {
      options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
      options.DefaultChallengeScheme = "Okta";
  })
  .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
  {
      o.SlidingExpiration = true;
      o.ExpireTimeSpan = TimeSpan.FromHours(1);
  })
  .AddOpenIdConnect("Okta", o =>
  {
      o.Authority = builder.Configuration["Okta:Authority"] ?? "https://integrator-7281285.okta.com";
      o.ClientId = builder.Configuration["Okta:ClientId"];
      o.ClientSecret = builder.Configuration["Okta:ClientSecret"];
      o.ResponseType = OpenIdConnectResponseType.Code;
      o.UsePkce = true;
      o.SaveTokens = true;
      o.Scope.Clear();
      o.Scope.Add("openid"); o.Scope.Add("profile"); o.Scope.Add("email");
      o.GetClaimsFromUserInfoEndpoint = true;

      o.Events = new OpenIdConnectEvents
      {
          OnTokenValidated = ctx =>
              ctx.HttpContext.RequestServices
                 .GetRequiredService<TokenValidatedHandler>()
                 .HandleAsync(ctx)
      };
  });
//   .AddOpenIdConnect("Google", o =>
//   {
//       o.Authority = "https://accounts.google.com";
//       o.ClientId = builder.Configuration["Google:ClientId"];
//       o.ClientSecret = builder.Configuration["Google:ClientSecret"];
//       o.ResponseType = OpenIdConnectResponseType.Code;
//       o.UsePkce = true;
//       o.SaveTokens = true;
//       o.Scope.Clear();
//       o.Scope.Add("openid"); o.Scope.Add("profile"); o.Scope.Add("email");
//       o.GetClaimsFromUserInfoEndpoint = true;

//       o.Events = new OpenIdConnectEvents
//       {
//           OnTokenValidated = ctx =>
//               ctx.HttpContext.RequestServices
//                  .GetRequiredService<TokenValidatedHandler>()
//                  .HandleAsync(ctx)
//       };
//   });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CanViewAuthEvents", p => p.RequireClaim("permissions", "Audit.ViewAuthEvents"))
    .AddPolicy("CanViewRoleChanges", p => p.RequireClaim("permissions", "Audit.RoleChanges"));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

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
