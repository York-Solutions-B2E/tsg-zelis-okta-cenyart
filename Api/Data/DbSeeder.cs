using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        // Roles
        var roleBasic = new Role
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Name = "BasicUser",
            Description = "Default role"
        };
        var roleObserver = new Role
        {
            Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Name = "AuthObserver",
            Description = "Can view auth events"
        };
        var roleAuditor = new Role
        {
            Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Name = "SecurityAuditor",
            Description = "Can audit role changes"
        };

        if (!db.Roles.Any())
            db.Roles.AddRange(roleBasic, roleObserver, roleAuditor);

        // Claims
        var claimAuthEvents = new Claim
        {
            Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            Type = "permissions",
            Value = "Audit.ViewAuthEvents"
        };
        var claimRoleChanges = new Claim
        {
            Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            Type = "permissions",
            Value = "Audit.RoleChanges"
        };

        if (!db.Claims.Any())
            db.Claims.AddRange(claimAuthEvents, claimRoleChanges);

        db.SaveChanges();

        // Role ↔ Claim links
        var observer = db.Roles.Include(r => r.Claims).First(r => r.Name == "AuthObserver");
        if (observer.Claims.Count == 0)
        {
            observer.Claims.Add(db.Claims.First(c => c.Value == "Audit.ViewAuthEvents"));
        }

        var auditor = db.Roles.Include(r => r.Claims).First(r => r.Name == "SecurityAuditor");
        if (auditor.Claims.Count == 0)
        {
            auditor.Claims.Add(db.Claims.First(c => c.Value == "Audit.ViewAuthEvents"));
            auditor.Claims.Add(db.Claims.First(c => c.Value == "Audit.RoleChanges"));
        }

        // Default user
        if (!db.Users.Any())
        {
            db.Users.Add(new User
            {
                Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                ExternalId = "seed-user-sub",
                Email = "seeduser@example.com",
                RoleId = roleBasic.Id
            });
        }

        db.SaveChanges();
    }
}
