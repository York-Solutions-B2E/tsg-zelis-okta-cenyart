using Microsoft.EntityFrameworkCore;
using Api.Data;

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
