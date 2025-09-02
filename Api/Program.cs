using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.GraphQL;
using Api.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------- User Secrets ----------------
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

// ---------------- Database ----------------
var sqlConnectionString = builder.Configuration.GetConnectionString("ZelisOkta")
        ?? throw new InvalidOperationException("Missing SQL Server connection string: 'ZelisOkta'");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(sqlConnectionString));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---------------- DI ----------------
builder.Services
    .AddScoped<IUserRoleProvider, EfUserRoleProvider>()
    .AddScoped<AuthorizationService>()
    .AddScoped<ProvisioningService>()
    .AddScoped<TokenService>();

var jwtKey = builder.Configuration["Jwt:Key"] ?? "replace-with-very-long-secret-in-prod";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "client";
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CanViewAuthEvents", p => p.RequireClaim("permissions", "Audit.ViewAuthEvents"))
    .AddPolicy("CanViewRoleChanges", p => p.RequireClaim("permissions", "Audit.RoleChanges"));

// ---------------- GraphQL ----------------
builder.Services
    .AddGraphQLServer()
    .AddAuthorization() // enable HotChocolate auth integration
    .AddMutationType(d => d.Name("Mutation")) // root mutation type
    .AddType<ProvisioningMutations>()
    .AddQueryType(d => d.Name("Query"))
    .AddTypeExtension<AuthorizationQueries>();

// ---------------- Build app ----------------
var app = builder.Build();

// ---------------- DB Migrations ----------------
using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
db.Database.Migrate();
DbSeeder.Seed(db);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Run();
