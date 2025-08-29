using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Api.Data;
using Api.Auth;
using Api.GraphQL;

namespace Tests.Integration;

[TestFixture]
public class GraphQLProvisioningTest : TestInMemory
{
    private IRequestExecutor _executor = null!;

    [SetUp]
    public async Task SetUpGraphql()
    {
        var services = new ServiceCollection();

        // use same AppDbContext instance (TestInMemory provides Db)
        services.AddSingleton(Db);

        // register provisioning service and real role provider + auth service
        services.AddSingleton<ProvisioningService>(sp => new ProvisioningService(Db));
        services.AddSingleton<IUserRoleProvider>(sp => new EfUserRoleProvider(Db));
        services.AddSingleton<AuthorizationService>(sp => new AuthorizationService(sp.GetRequiredService<IUserRoleProvider>()));

        // register GraphQL pieces
        services.AddGraphQLServer()
            .AddMutationType<ProvisioningMutations>()
            .AddQueryType<AuthorizationQueries>();

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
    public async Task ProvisionOnLogin_Mutation_CreatesUserAndWritesLoginSuccessEvent()
    {
        var externalId = "okta|integration-1";
        var email = "provision-int@example.com";
        var provider = "Okta";

        var mutation = $@"
        mutation {{
            provisionOnLogin(externalId: ""{externalId}"", email: ""{email}"", provider: ""{provider}"") {{
                success
                message
                user {{ id email role {{ name }} }}
            }}
        }}";

        var result = await Exec(mutation);
        Assert.False(HasErrors(result), "GraphQL returned errors");

        var data = DataDict(result);
        var payload = (IReadOnlyDictionary<string, object?>)data["provisionOnLogin"]!;
        Assert.That(payload["success"] is true, Is.True);

        // verify DB: user created and role BasicUser
        var user = Db.Users.Include(u => u.Role).FirstOrDefault(u => u.ExternalId == externalId);
        Assert.NotNull(user);
        Assert.That(user!.Role.Name, Is.EqualTo("BasicUser"));

        // verify exactly one LoginSuccess event written
        var events = Db.SecurityEvents.Where(e => e.EventType == "LoginSuccess" && e.AffectedUserId == user.Id).ToList();
        Assert.That(events.Count, Is.EqualTo(1));
        Assert.That(events[0].Details, Does.Contain("provider=Okta"));
    }
}
