using System.Text.Json;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Api.Auth;
using Microsoft.EntityFrameworkCore;
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

    private async Task<JsonDocument> ExecJson(string gql)
    {
        var res = await _executor.ExecuteAsync(builder => builder.SetQuery(gql));
        var json = await res.ToJsonAsync();
        return JsonDocument.Parse(json);
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

        var doc = await ExecJson(mutation);
        Assert.False(doc.RootElement.TryGetProperty("errors", out _));

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
