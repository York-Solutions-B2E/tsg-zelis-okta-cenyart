using Api.Auth;
using Microsoft.EntityFrameworkCore;

namespace Tests.Integration;

[TestFixture]
public class ProvisioningServiceTests : TestInMemory
{
    [Test]
    public async Task FirstLogin_CreatesUser_WithBasicUser_And_WritesLoginSuccess()
    {
        var svc = new ProvisioningService(Db);

        var externalId = "okta|int-1";
        var email = "int-new@example.com";
        var provider = "Okta";

        var user = await svc.ProvisionOnLoginAsync(externalId, email, provider);

        var fromDb = Db.Users.Include(u => u.Role).FirstOrDefault(u => u.ExternalId == externalId);
        Assert.Multiple(() =>
        {
            Assert.That(fromDb, Is.Not.Null);
            Assert.That(fromDb!.Email, Is.EqualTo(email));
            Assert.That(fromDb.Role!.Name, Is.EqualTo("BasicUser"));
        });

        var ev = Db.SecurityEvents.FirstOrDefault(e => e.EventType == "LoginSuccess" && e.AffectedUserId == fromDb.Id);
        Assert.That(ev, Is.Not.Null);
        Assert.That(ev!.AuthorUserId, Is.EqualTo(fromDb.Id));
    }

    [Test]
    public async Task SubsequentLogin_DoesNotDuplicateUser_But_WritesEvent()
    {
        var svc = new ProvisioningService(Db);

        var externalId = "okta|int-2";
        var email = "int-exists@example.com";
        var provider = "Okta";

        var first = await svc.ProvisionOnLoginAsync(externalId, email, provider);
        var second = await svc.ProvisionOnLoginAsync(externalId, email, provider);

        Assert.That(first.Id, Is.EqualTo(second.Id));

        var evCount = Db.SecurityEvents.Count(e => e.EventType == "LoginSuccess" && e.AffectedUserId == first.Id);
        Assert.That(evCount, Is.EqualTo(2));
    }
}
