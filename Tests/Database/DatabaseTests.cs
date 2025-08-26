using NUnit.Framework;
using Microsoft.EntityFrameworkCore;

namespace Tests.Database;

[TestFixture]
public class DatabaseTests : TestBase
{
    [Test]
    public void Should_Have_Roles_Seeded()
    {
        var roles = Db.Roles.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(roles.Any(r => r.Name == "BasicUser"));
            Assert.That(roles.Any(r => r.Name == "AuthObserver"));
            Assert.That(roles.Any(r => r.Name == "SecurityAuditor"));
        });

    }

    [Test]
    public void Should_Have_Claims_Seeded()
    {
        var claims = Db.Claims.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(claims.Any(c => c.Value == "Audit.ViewAuthEvents"));
            Assert.That(claims.Any(c => c.Value == "Audit.RoleChanges"));
        });

    }

    [Test]
    public void Should_Link_Roles_To_Claims()
    {
        var auditor = Db.Roles
            .Include(r => r.Claims)
            .First(r => r.Name == "SecurityAuditor");

        Assert.Multiple(() =>
        {
            Assert.That(auditor.Claims.Any(c => c.Value == "Audit.ViewAuthEvents"));
            Assert.That(auditor.Claims.Any(c => c.Value == "Audit.RoleChanges"));
        });

    }

    [Test]
    public async Task Should_Create_User_With_Role()
    {
        var role = Db.Roles.First(r => r.Name == "BasicUser");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            ExternalId = "oidc-123",
            Role = role
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        var fromDb = Db.Users.Include(u => u.Role).First();
        Assert.That(fromDb.Role.Name, Is.EqualTo("BasicUser"));
    }

    [Test]
    public async Task Should_Record_SecurityEvent()
    {
        var role = Db.Roles.First(r => r.Name == "BasicUser");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "jane@doe.com",
            ExternalId = "oidc-456",
            Role = role
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        var ev = new SecurityEvent
        {
            EventType = "LoginSuccess",
            AuthorUserId = user.Id,
            AffectedUserId = user.Id,
            Details = "provider=Okta"
        };
        Db.SecurityEvents.Add(ev);
        await Db.SaveChangesAsync();

        var fromDb = Db.SecurityEvents.First();
        Assert.That(fromDb.EventType, Is.EqualTo("LoginSuccess"));
        Assert.That(fromDb.Details, Does.Contain("provider=Okta"));
    }
}
