using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.GraphQL;
using Api.Auth;

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
    .AddScoped<ProvisioningService>();

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

// ---------------- GraphQL ----------------
builder.Services
    .AddGraphQLServer()
    .AddQueryType<AuthorizationQueries>()
    .AddMutationType<ProvisioningMutations>();

app.UseHttpsRedirection();

app.Run();
