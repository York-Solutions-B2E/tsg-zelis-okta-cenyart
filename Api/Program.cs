using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Api.Data;
using Api.GraphQL;
using Microsoft.EntityFrameworkCore;
using Api.Services;

var builder = WebApplication.CreateBuilder(args);

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "https://example.local";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "api";
var jwtKey = builder.Configuration["Jwt:Key"] ?? "very-long-secret-key-change-this";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Authentication - JWT bearer
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
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        NameClaimType = "uid"
    };
});

// Authorization policies
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CanViewAuthEvents", p => p.RequireClaim("permissions", "Audit.ViewAuthEvents"))
    .AddPolicy("CanViewRoleChanges", p => p.RequireClaim("permissions", "Audit.RoleChanges"));

// Register services used by GraphQL resolvers
builder.Services.AddScoped<ProvisioningService>();

// HotChocolate GraphQL
builder.Services
    .AddGraphQLServer()
    .AddAuthorization()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddTypeExtension<ProvisioningMutations>();

var app = builder.Build();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapGraphQL("/graphql");

app.Run();
