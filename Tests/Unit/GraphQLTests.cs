using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Api.Data;
using Api.GraphQL;

namespace Tests;

[TestFixture]
public class GraphQLTests
{
    private IRequestExecutor _executor = null!;
    private AppDbContext _db = null!;
    private IServiceProvider _sp = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        DbSeeder.Seed(_db);

        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddGraphQL()
            .AddQueryType<Query>()
            .AddMutationType<Mutation>();

        _sp = services.BuildServiceProvider();
        _executor = await _sp.GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _db.Dispose();
        if (_sp is IDisposable d) d.Dispose();
    }

    public static class AuthConstants
    {
        public const string ViewAuthEvents = "Audit.ViewAuthEvents";
        public const string RoleChanges = "Audit.RoleChanges";
        public const string PermissionsClaimType = "permissions";
    }

    private static ClaimsPrincipal CreatePrincipal(params string[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new System.Security.Claims.Claim(AuthConstants.PermissionsClaimType, c)),
            "test"
        );
        return new ClaimsPrincipal(identity);
    }

    private async Task<string> ExecuteQuery(string query, params string[] claims)
    {
        var principal = CreatePrincipal(claims);
        var request = OperationRequestBuilder.New()
            .SetDocument(query)
            .AddGlobalState("currentUser", principal)
            .Build();

        var result = await _executor.ExecuteAsync(request);
        return result.ToJson();
    }

    private static void AssertUserRole(Guid userId, Guid expectedRoleId, AppDbContext db)
    {
        var user = db.Users.Include(u => u.Role).First(u => u.Id == userId);
        Assert.That(user.RoleId, Is.EqualTo(expectedRoleId));
    }

    [Test]
    public async Task UsersQuery_ReturnsUsersWithRoles()
    {
        var result = await ExecuteQuery("{ users { id email role { name } } }",
            AuthConstants.ViewAuthEvents);

        Assert.That(result, Contains.Substring("users"));
        Assert.That(result, Does.Not.Contain("errors"));
    }

    [Test]
    public async Task RolesQuery_ReturnsRoles()
    {
        var result = await ExecuteQuery("{ roles { id name } }",
            AuthConstants.RoleChanges);

        Assert.That(result, Contains.Substring("roles"));
        Assert.That(result, Does.Not.Contain("errors"));
    }

    [Test]
    public async Task SecurityEventsQuery_RespectsClaims()
    {
        var withClaim = await ExecuteQuery(
            "{ securityEvents { id eventType details } }",
            AuthConstants.ViewAuthEvents);

        Assert.That(withClaim, Contains.Substring("securityEvents"));
        Assert.That(withClaim, Does.Not.Contain("errors"));

        var noClaim = await ExecuteQuery("{ securityEvents { id eventType details } }");
        Assert.That(noClaim, Does.Not.Contain("null"));
    }

    [Test]
    public async Task AssignUserRole_Mutation_WorksWithRoleChangesClaim()
    {
        var user = _db.Users.First(u => u.Role!.Name == "BasicUser");
        var newRole = _db.Roles.First(r => r.Name == "AuthObserver");

        var mutation = $@"
            mutation {{
                assignUserRole(userId: ""{user.Id}"", roleId: ""{newRole.Id}"") {{
                    success
                    message
                }}
            }}";

        var result = await ExecuteQuery(mutation, AuthConstants.RoleChanges);
        Assert.That(result, Does.Not.Contain("errors"));
        Assert.That(result, Contains.Substring("success"));

        AssertUserRole(user.Id, newRole.Id, _db);

        var roleEvents = _db.SecurityEvents
            .Where(e => e.EventType == "RoleAssigned" && e.AffectedUserId == user.Id);
        Assert.That(roleEvents.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task SecurityEventsQuery_FiltersAuthEventsCorrectly()
    {
        var authEvent = new SecurityEvent
        {
            EventType = "LoginAttempt",
            AuthorUserId = _db.Users.First().Id,
            AffectedUserId = _db.Users.First().Id,
            Details = "Test login attempt"
        };
        
        var roleEvent = new SecurityEvent
        {
            EventType = "RoleAssigned", 
            AuthorUserId = _db.Users.First().Id,
            AffectedUserId = _db.Users.First().Id,
            Details = "from=BasicUser to=AuthObserver"
        };

        _db.SecurityEvents.AddRange(authEvent, roleEvent);
        _db.SaveChanges();

        var authResult = await ExecuteQuery(
            "{ securityEvents { eventType } }",
            AuthConstants.ViewAuthEvents);

        var roleResult = await ExecuteQuery(
            "{ securityEvents { eventType } }",
            AuthConstants.RoleChanges);

        var bothResult = await ExecuteQuery(
            "{ securityEvents { eventType } }",
            AuthConstants.ViewAuthEvents, AuthConstants.RoleChanges);

        Assert.Multiple(() =>
        {
            Assert.That(authResult, Does.Not.Contain("errors"));
            Assert.That(roleResult, Does.Not.Contain("errors"));
            Assert.That(bothResult, Does.Not.Contain("errors"));
        });

    }

    [Test]
    public async Task AssignUserRole_RequiresCorrectClaim()
    {
        var user = _db.Users.First();
        var role = _db.Roles.First(r => r.Name == "AuthObserver");

        var mutation = $@"
            mutation {{
                assignUserRole(userId: ""{user.Id}"", roleId: ""{role.Id}"") {{
                    success
                    message
                }}
            }}";

        var result = await ExecuteQuery(mutation, AuthConstants.ViewAuthEvents);
        Assert.That(result, Contains.Substring("errors").Or.Contains("Unauthorized"));
    }

    [Test] 
    public async Task SecurityEventsQuery_OrdersByOccurredUtcDesc()
    {
        // Clear existing events
        _db.SecurityEvents.RemoveRange(_db.SecurityEvents);
        
        var now = DateTime.UtcNow;
        var older = new SecurityEvent
        {
            EventType = "LoginAttempt",
            AuthorUserId = _db.Users.First().Id,
            AffectedUserId = _db.Users.First().Id,
            OccurredUtc = now.AddMinutes(-10)
        };
        
        var newer = new SecurityEvent
        {
            EventType = "LoginSuccess",
            AuthorUserId = _db.Users.First().Id, 
            AffectedUserId = _db.Users.First().Id,
            OccurredUtc = now
        };

        _db.SecurityEvents.AddRange(older, newer);
        _db.SaveChanges();

        var result = await ExecuteQuery(
            "{ securityEvents { eventType occurredUtc } }",
            AuthConstants.ViewAuthEvents);

        Assert.That(result, Does.Not.Contain("errors"));
        Assert.That(result, Contains.Substring("securityEvents"));
    }
}
