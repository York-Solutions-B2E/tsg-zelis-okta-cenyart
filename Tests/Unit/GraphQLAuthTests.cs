using System;
using System.Text.Json;
using System.Threading.Tasks;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Api.Auth;
using Api.GraphQL;
using NUnit.Framework;

namespace Tests.Unit;

[TestFixture]
public class GraphQLAuthTests
{
    private IRequestExecutor _executor = null!;

    // --- Error helpers using JSON from the execution result ---
    private static bool HasErrors(IExecutionResult result)
    {
        var json = result.ToJson();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0;
    }

    private static string GetErrors(IExecutionResult result)
    {
        var json = result.ToJson();
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("errors", out var errors))
        {
            return errors.ToString();
        }

        return string.Empty;
    }

    private static void PrintErrors(IExecutionResult result)
    {
        var errors = GetErrors(result);
        if (!string.IsNullOrEmpty(errors))
        {
            Console.Error.WriteLine("GraphQL errors: " + errors);
        }
    }

    private static async Task<JsonDocument> ExecJsonAsync(IRequestExecutor executor, string gql)
    {
        IExecutionResult result = await executor.ExecuteAsync(gql);
        PrintErrors(result);
        return JsonDocument.Parse(result.ToJson());
    }

    private static async Task<IRequestExecutor> BuildExecutorAsync(Mock<IUserRoleProvider> mockProvider)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IUserRoleProvider>(mockProvider.Object);
        services.AddSingleton<AuthorizationService>();

        // Register Query in DI
        services.AddScoped<Query>();

        services.AddGraphQLServer()
                .AddQueryType<Query>()
                .AddTypeExtension<AuthorizationQueries>();

        var sp = services.BuildServiceProvider();
        return await sp.GetRequiredService<IRequestExecutorResolver>()
                       .GetRequestExecutorAsync();
    }

    [SetUp]
    public async Task SetupExecutor()
    {
        var mockProvider = new Mock<IUserRoleProvider>();
        mockProvider.Setup(p => p.GetRoleNameAsync(It.IsAny<Guid>(), default))
                    .ReturnsAsync((Guid id, System.Threading.CancellationToken _) => "BasicUser");

        _executor = await BuildExecutorAsync(mockProvider);
    }

    [Test]
    public async Task CanViewAuthEvents_Query_True_For_AuthObserver()
    {
        var userId = Guid.NewGuid();
        var mockProvider = new Mock<IUserRoleProvider>();
        mockProvider.Setup(p => p.GetRoleNameAsync(userId, default)).ReturnsAsync("AuthObserver");

        _executor = await BuildExecutorAsync(mockProvider);

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

        _executor = await BuildExecutorAsync(mockProvider);

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

        _executor = await BuildExecutorAsync(mockProvider);

        var gql = $@"{{ canViewRoleChanges(userId: ""{userId}"") }}";
        var doc = await ExecJsonAsync(_executor, gql);

        Assert.False(doc.RootElement.TryGetProperty("errors", out _));
        var val = doc.RootElement.GetProperty("data").GetProperty("canViewRoleChanges").GetBoolean();
        Assert.IsTrue(val);
    }
}
