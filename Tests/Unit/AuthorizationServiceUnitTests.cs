using Moq;
using Api.Auth;

namespace Tests.Unit;

[TestFixture]
public class AuthorizationServiceUnitTests
{
    [Test]
    public async Task BasicUser_Cannot_View_AuthEvents_Or_RoleChanges()
    {
        var userId = Guid.NewGuid();
        var mock = new Mock<IUserRoleProvider>();
        mock.Setup(m => m.GetRoleNameAsync(userId, default)).ReturnsAsync("BasicUser");

        var svc = new AuthorizationService(mock.Object);

        Assert.IsFalse(await svc.CanViewAuthEventsAsync(userId));
        Assert.IsFalse(await svc.CanViewRoleChangesAsync(userId));
    }

    [Test]
    public async Task AuthObserver_Can_View_AuthEvents_But_Not_RoleChanges()
    {
        var userId = Guid.NewGuid();
        var mock = new Mock<IUserRoleProvider>();
        mock.Setup(m => m.GetRoleNameAsync(userId, default)).ReturnsAsync("AuthObserver");

        var svc = new AuthorizationService(mock.Object);

        Assert.IsTrue(await svc.CanViewAuthEventsAsync(userId));
        Assert.IsFalse(await svc.CanViewRoleChangesAsync(userId));
    }

    [Test]
    public async Task SecurityAuditor_Can_View_Both()
    {
        var userId = Guid.NewGuid();
        var mock = new Mock<IUserRoleProvider>();
        mock.Setup(m => m.GetRoleNameAsync(userId, default)).ReturnsAsync("SecurityAuditor");

        var svc = new AuthorizationService(mock.Object);

        Assert.IsTrue(await svc.CanViewAuthEventsAsync(userId));
        Assert.IsTrue(await svc.CanViewRoleChangesAsync(userId));
    }
}
