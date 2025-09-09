using System.Text;
using Microsoft.IdentityModel.Tokens;
using Api.Data;
using Api.GraphQL;
using Microsoft.EntityFrameworkCore;
using Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ZelisOkta")));

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "your-app",
            ValidateAudience = true,
            ValidAudience = "your-api",
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("this-is-a-very-strong-secret-key-123456"))
        };

        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(ctx.Exception, "JWT authentication failed");
                return Task.CompletedTask;
            },
            OnTokenValidated = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                var claims = string.Join(", ", ctx.Principal!.Claims.Select(c => $"{c.Type}={c.Value}"));
                logger.LogInformation("JWT validated. Claims: {Claims}", claims);
                return Task.CompletedTask;
            }
        };
    });

// Authorization policies
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CanViewAuthEvents", p => p.RequireClaim("permissions", "Audit.ViewAuthEvents"))
    .AddPolicy("CanViewRoleChanges", p => p.RequireClaim("permissions", "Audit.RoleChanges"));

// Register services used by GraphQL resolvers
builder.Services.AddScoped<ProvisioningService>();
builder.Services.AddScoped<SecurityEventService>();
builder.Services.AddScoped<RoleService>();

// IHttpContextAccessor used by queries/mutations
builder.Services.AddHttpContextAccessor();

// HotChocolate GraphQL
builder.Services
    .AddGraphQLServer()
    .AddAuthorization()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .ModifyRequestOptions(opt => opt.IncludeExceptionDetails = true);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("Api", LogLevel.Debug);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapGraphQL("/graphql");

app.Run();
