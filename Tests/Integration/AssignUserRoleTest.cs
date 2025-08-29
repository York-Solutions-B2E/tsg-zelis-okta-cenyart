using System.Collections;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Api.Data;
using Api.Auth;
using Api.GraphQL;

namespace Tests.Integration;

[TestFixture]
public class AssignUserRoleTest : TestInMemory
{
    private IRequestExecutor _executor = null!;

    [SetUp]
    public async Task SetUpGraphql()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Db);

        // Use EF provider for real integration
        services.AddSingleton<IUserRoleProvider>(sp => new EfUserRoleProvider(Db));
        services.AddSingleton<AuthorizationService>();
        services.AddSingleton<ProvisioningService>(sp => new ProvisioningService(Db));

        // Register GraphQL types
        services.AddGraphQLServer()
            .AddQueryType<Query>()
            .AddMutationType<Mutation>();

        var sp = services.BuildServiceProvider();
        _executor = await sp.GetRequiredService<IRequestExecutorResolver>().GetRequestExecutorAsync();
    }

    private async Task<IExecutionResult> Exec(string gql)
    {
        var res = await _executor.ExecuteAsync(gql);
        return res;
    }

    private static bool HasErrors(IExecutionResult result)
    {
        dynamic dyn = result;
        var errs = dyn.Errors;
        if (errs == null) return false;
        var enumerable = errs as IEnumerable;
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
    public async Task AssignUserRole_Mutation_WritesSingleRoleAssignedEvent()
    {
        // Arrange: create user with SecurityAuditor role (authorized to assign roles)
        var user = new User
        {
            Id = Guid.NewGuid(),
            ExternalId = "int-assign-1",
            Email = "assign1@example.com",
            RoleId = Db.Roles.First(r => r.Name == "SecurityAuditor").Id
        };
        Db.Users.Add(user);
        Db.SaveChanges();

        // Target role to assign
        var newRole = Db.Roles.First(r => r.Name == "AuthObserver");

        // GraphQL mutation
        var mutation = $@"
            mutation {{
                assignUserRole(userId: ""{user.Id}"", roleId: ""{newRole.Id}"") {{
                    id
                    role {{ name }}
                }}
            }}";

        // Act
        var result = await Exec(mutation);

        // Assert
        Assert.False(HasErrors(result), "GraphQL returned errors");

        // Verify user role updated
        var updated = Db.Users.Include(u => u.Role).First(u => u.Id == user.Id);
        Assert.That(updated.Role.Name, Is.EqualTo("AuthObserver"));

        // There should be exactly one RoleAssigned event for that user
        var events = Db.SecurityEvents
            .Where(e => e.EventType == "RoleAssigned" && e.AffectedUserId == user.Id)
            .ToList();
        Assert.That(events.Count, Is.EqualTo(1));
        Assert.That(events[0].Details, Does.Contain("from=").And.Contain("to="));
    }
}
