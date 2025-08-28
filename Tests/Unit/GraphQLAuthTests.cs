using System.Text.Json;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Api.Auth;
using Api.GraphQL;

namespace Tests.Unit;

[TestFixture]
public class GraphQLAuthTests
{
    private IRequestExecutor _executor = null!;

    private static async Task<JsonDocument> ExecJsonAsync(IRequestExecutor executor, string gql)
    {
        var result = await executor.ExecuteAsync(builder => builder.SetQuery(gql));
        var json = await result.ToJsonAsync();
        return JsonDocument.Parse(json);
    }

    [Test]
    public async Task CanViewAuthEvents_Query_True_For_AuthObserver()
    {
        var userId = Guid.NewGuid();

        // Mock IUserRoleProvider to return "AuthObserver" for this user
        var mockProvider = new Mock<IUserRoleProvider>();
        mockProvider.Setup(p => p.GetRoleNameAsync(userId, default)).ReturnsAsync("AuthObserver");

        // Compose DI
        var services = new ServiceCollection();
        services.AddSingleton<IUserRoleProvider>(mockProvider.Object);
        services.AddSingleton<AuthorizationService>(); // depends on IUserRoleProvider
        services.AddGraphQLServer()
            .AddQueryType<AuthorizationQueries>();

        var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IRequestExecutorResolver>();
        _executor = await resolver.GetRequestExecutorAsync();

        var gql = $@"{{ canViewAuthEvents(userId: ""{userId}"") }}";
        var doc = await ExecJsonAsync(_executor, gql);

        Assert.False(doc.RootElement.TryGetProperty("errors", out _));
        var val = doc.RootElement.GetProperty("data").GetProperty("canViewAuthEvents").GetBoolean();
        Assert.IsTrue(val);
    }

    [Test]
    public async Task CanViewRoleChanges_Query_False_For_AuthObserver()
    {
        var userId = Guid.NewGuid();

        var mockProvider = new Mock<IUserRoleProvider>();
        mockProvider.Setup(p => p.GetRoleNameAsync(userId, default)).ReturnsAsync("AuthObserver");

        var services = new ServiceCollection();
        services.AddSingleton<IUserRoleProvider>(mockProvider.Object);
        services.AddSingleton<AuthorizationService>();
        services.AddGraphQLServer()
            .AddQueryType<AuthorizationQueries>();

        var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IRequestExecutorResolver>();
        _executor = await resolver.GetRequestExecutorAsync();

        var gql = $@"{{ canViewRoleChanges(userId: ""{userId}"") }}";
        var doc = await ExecJsonAsync(_executor, gql);

        Assert.False(doc.RootElement.TryGetProperty("errors", out _));
        var val = doc.RootElement.GetProperty("data").GetProperty("canViewRoleChanges").GetBoolean();
        Assert.IsFalse(val);
    }

    [Test]
    public async Task CanViewRoleChanges_Query_True_For_SecurityAuditor()
    {
        var userId = Guid.NewGuid();

        var mockProvider = new Mock<IUserRoleProvider>();
        mockProvider.Setup(p => p.GetRoleNameAsync(userId, default)).ReturnsAsync("SecurityAuditor");

        var services = new ServiceCollection();
        services.AddSingleton<IUserRoleProvider>(mockProvider.Object);
        services.AddSingleton<AuthorizationService>();
        services.AddGraphQLServer()
            .AddQueryType<AuthorizationQueries>();

        var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IRequestExecutorResolver>();
        _executor = await resolver.GetRequestExecutorAsync();

        var gql = $@"{{ canViewRoleChanges(userId: ""{userId}"") }}";
        var doc = await ExecJsonAsync(_executor, gql);

        Assert.False(doc.RootElement.TryGetProperty("errors", out _));
        var val = doc.RootElement.GetProperty("data").GetProperty("canViewRoleChanges").GetBoolean();
        Assert.IsTrue(val);
    }
}
