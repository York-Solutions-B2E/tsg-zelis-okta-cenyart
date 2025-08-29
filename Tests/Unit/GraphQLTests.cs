using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Api.Auth;
using Api.Data;
using Api.GraphQL;

namespace Tests.Unit;

[TestFixture]
public class GraphQLTests
{
    private IRequestExecutor _executor = null!;
    private AppDbContext _db = null!;
    private IServiceProvider _sp = null!;
    private Mock<IUserRoleProvider> _mockRoleProvider = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        // 1️⃣ In-memory DB
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        DbSeeder.Seed(_db);

        // 2️⃣ Mock IUserRoleProvider
        _mockRoleProvider = new Mock<IUserRoleProvider>();
        _mockRoleProvider
            .Setup(p => p.GetRoleNameAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Guid uid, System.Threading.CancellationToken _) =>
            {
                var user = _db.Users.Include(u => u.Role).FirstOrDefault(u => u.Id == uid);
                return user?.Role?.Name ?? "BasicUser";
            });

        // 3️⃣ DI registration
        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddSingleton<IUserRoleProvider>(_mockRoleProvider.Object);

        services.AddSingleton<AuthorizationService>(sp =>
        {
            var roleProvider = sp.GetRequiredService<IUserRoleProvider>();
            return new AuthorizationService(roleProvider);
        });

        // 4️⃣ GraphQL setup
        services.AddGraphQLServer()
            .AddQueryType<AuthorizationQueries>()
            .AddTypeExtension<Query>()
            .AddMutationType<ProvisioningMutations>();

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

    private async Task<IExecutionResult> Exec(string gql)
        => await _executor.ExecuteAsync(gql);

    private static bool HasErrors(IExecutionResult result)
    {
        dynamic dyn = result;
        var errs = dyn.Errors;
        if (errs == null) return false;
        var enumerable = errs as System.Collections.IEnumerable;
        if (enumerable == null) return false;
        foreach (var _ in enumerable) return true;
        return false;
    }

    private static IReadOnlyDictionary<string, object?> DataDict(IExecutionResult result)
    {
        dynamic dyn = result;
        return (IReadOnlyDictionary<string, object?>)dyn.Data!;
    }

    [Test]
    public async Task UsersQuery_ReturnsUsersWithRoles()
    {
        var result = await Exec("{ users { id email role { name } } }");
        Assert.False(HasErrors(result));
        var data = DataDict(result);
        var users = (IReadOnlyList<object?>)data["users"]!;
        Assert.IsNotEmpty(users);
    }

    [Test]
    public async Task RolesQuery_ReturnsRoles()
    {
        var result = await Exec("{ roles { id name } }");
        Assert.False(HasErrors(result));
        var data = DataDict(result);
        var roles = (IReadOnlyList<object?>)data["roles"]!;
        Assert.IsNotEmpty(roles);
    }

    [Test]
    public async Task CanViewAuthEvents_Query_True_For_AuthObserver()
    {
        var role = _db.Roles.First(r => r.Name == "AuthObserver");
        var u = new User { Id = Guid.NewGuid(), ExternalId = "ut-authobs", Email = "obs@example.com", RoleId = role.Id };
        _db.Users.Add(u);
        _db.SaveChanges();

        _mockRoleProvider.Setup(p => p.GetRoleNameAsync(u.Id, default)).ReturnsAsync("AuthObserver");

        var q = $@"{{ canViewAuthEvents(userId: ""{u.Id}"") }}";
        var res = await Exec(q);
        Assert.False(HasErrors(res));
        var val = (bool)DataDict(res)["canViewAuthEvents"]!;
        Assert.IsTrue(val);
    }

    [Test]
    public async Task CanViewRoleChanges_Query_False_For_AuthObserver()
    {
        var role = _db.Roles.First(r => r.Name == "AuthObserver");
        var u = new User { Id = Guid.NewGuid(), ExternalId = "ut-authobs2", Email = "obs2@example.com", RoleId = role.Id };
        _db.Users.Add(u);
        _db.SaveChanges();

        _mockRoleProvider.Setup(p => p.GetRoleNameAsync(u.Id, default)).ReturnsAsync("AuthObserver");

        var q = $@"{{ canViewRoleChanges(userId: ""{u.Id}"") }}";
        var res = await Exec(q);
        Assert.False(HasErrors(res));
        var val = (bool)DataDict(res)["canViewRoleChanges"]!;
        Assert.IsFalse(val);
    }

    [Test]
    public async Task CanViewRoleChanges_Query_True_For_SecurityAuditor()
    {
        var role = _db.Roles.First(r => r.Name == "SecurityAuditor");
        var u = new User { Id = Guid.NewGuid(), ExternalId = "ut-aud", Email = "aud@example.com", RoleId = role.Id };
        _db.Users.Add(u);
        _db.SaveChanges();

        _mockRoleProvider.Setup(p => p.GetRoleNameAsync(u.Id, default)).ReturnsAsync("SecurityAuditor");

        var q = $@"{{ canViewRoleChanges(userId: ""{u.Id}"") }}";
        var res = await Exec(q);
        Assert.False(HasErrors(res));
        var val = (bool)DataDict(res)["canViewRoleChanges"]!;
        Assert.IsTrue(val);
    }
}
