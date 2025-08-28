using Api.Auth;

namespace Tests.Integration;

[TestFixture]
public class AuthorizationServiceIntegrationTests : TestInMemory
{
    [Test]
    public async Task AuthObserver_CanViewAuthEvents_ButNotRoleChanges()
    {
        // arrange
        var role = Db.Roles.First(r => r.Name == "AuthObserver");
        var user = new Api.Data.User { Id = Guid.NewGuid(), ExternalId = "int-authobs", Email = "a@a.com", RoleId = role.Id };
        Db.Users.Add(user);
        Db.SaveChanges();

        // provider uses the real Db context
        var provider = new EfUserRoleProvider(Db);
        var svc = new AuthorizationService(provider);

        Assert.IsTrue(await svc.CanViewAuthEventsAsync(user.Id));
        Assert.IsFalse(await svc.CanViewRoleChangesAsync(user.Id));
    }

    [Test]
    public async Task SecurityAuditor_CanViewBoth()
    {
        var role = Db.Roles.First(r => r.Name == "SecurityAuditor");
        var user = new Api.Data.User { Id = Guid.NewGuid(), ExternalId = "int-aud", Email = "aud@a.com", RoleId = role.Id };
        Db.Users.Add(user);
        Db.SaveChanges();

        var provider = new EfUserRoleProvider(Db);
        var svc = new AuthorizationService(provider);

        Assert.IsTrue(await svc.CanViewAuthEventsAsync(user.Id));
        Assert.IsTrue(await svc.CanViewRoleChangesAsync(user.Id));
    }

    [Test]
    public async Task BasicUser_CannotViewEither()
    {
        var role = Db.Roles.First(r => r.Name == "BasicUser");
        var user = new Api.Data.User { Id = Guid.NewGuid(), ExternalId = "int-basic", Email = "b@b.com", RoleId = role.Id };
        Db.Users.Add(user);
        Db.SaveChanges();

        var provider = new EfUserRoleProvider(Db);
        var svc = new AuthorizationService(provider);

        Assert.IsFalse(await svc.CanViewAuthEventsAsync(user.Id));
        Assert.IsFalse(await svc.CanViewRoleChangesAsync(user.Id));
    }
}
