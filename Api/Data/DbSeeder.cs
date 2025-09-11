using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        // Deterministic Role IDs
        var roleBasicId    = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var roleObserverId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var roleAuditorId  = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        // Deterministic Claim IDs
        var claimAuthEventsId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var claimRoleChangesId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        // Seed Roles
        if (!db.Roles.Any(r => r.Name == "BasicUser"))
            db.Roles.Add(new Role { Id = roleBasicId, Name = "BasicUser", Description = "Default role" });

        if (!db.Roles.Any(r => r.Name == "AuthObserver"))
            db.Roles.Add(new Role { Id = roleObserverId, Name = "AuthObserver", Description = "Can view auth events" });

        if (!db.Roles.Any(r => r.Name == "SecurityAuditor"))
            db.Roles.Add(new Role { Id = roleAuditorId, Name = "SecurityAuditor", Description = "Can audit role changes" });

        // Seed Claims
        if (!db.Claims.Any(c => c.Value == "Audit.ViewAuthEvents"))
            db.Claims.Add(new Claim { Id = claimAuthEventsId, Type = "permissions", Value = "Audit.ViewAuthEvents" });

        if (!db.Claims.Any(c => c.Value == "Audit.RoleChanges"))
            db.Claims.Add(new Claim { Id = claimRoleChangesId, Type = "permissions", Value = "Audit.RoleChanges" });

        db.SaveChanges();

        // Attach Claims to Roles
        var observer = db.Roles.Include(r => r.Claims).First(r => r.Name == "AuthObserver");
        var auditor = db.Roles.Include(r => r.Claims).First(r => r.Name == "SecurityAuditor");

        var claimAuth = db.Claims.First(c => c.Value == "Audit.ViewAuthEvents");
        var claimRole = db.Claims.First(c => c.Value == "Audit.RoleChanges");

        if (!observer.Claims.Any(c => c.Value == claimAuth.Value))
            observer.Claims.Add(claimAuth);

        if (!auditor.Claims.Any(c => c.Value == claimAuth.Value))
            auditor.Claims.Add(claimAuth);

        if (!auditor.Claims.Any(c => c.Value == claimRole.Value))
            auditor.Claims.Add(claimRole);

        db.SaveChanges();

        // Seed Default User
        if (!db.Users.Any(u => u.ExternalId == "seed-user-sub"))
        {
            var basic = db.Roles.First(r => r.Name == "BasicUser");
            db.Users.Add(new User
            {
                Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                ExternalId = "seed-user-sub",
                Provider = "Okta",
                Email = "seeduser@example.com",
                RoleId = basic.Id,
            });

            db.SaveChanges();
        }
    }
}
