using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Unique indexes
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Role>()
            .HasIndex(r => r.Name)
            .IsUnique();

        // Many-to-many Role ↔ Claim
        modelBuilder.Entity<Role>()
            .HasMany(r => r.Claims)
            .WithMany(c => c.Roles);

        modelBuilder.Entity<SecurityEvent>(b =>
        {
            b.HasOne(e => e.AuthorUser)
                .WithMany()
                .HasForeignKey(e => e.AuthorUserId)
                .OnDelete(DeleteBehavior.NoAction);

            b.HasOne(e => e.AffectedUser)
                .WithMany()
                .HasForeignKey(e => e.AffectedUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // Seed data
        var roleBasic = new Role { Id = Guid.NewGuid(), Name = "BasicUser" };
        var roleObserver = new Role { Id = Guid.NewGuid(), Name = "AuthObserver" };
        var roleAuditor = new Role { Id = Guid.NewGuid(), Name = "SecurityAuditor" };

        modelBuilder.Entity<Role>().HasData(roleBasic, roleObserver, roleAuditor);

        var claimAuthEvents = new Claim { Id = Guid.NewGuid(), Type = "permissions", Value = "Audit.ViewAuthEvents" };
        var claimRoleChanges = new Claim { Id = Guid.NewGuid(), Type = "permissions", Value = "Audit.RoleChanges" };

        modelBuilder.Entity<Claim>().HasData(claimAuthEvents, claimRoleChanges);

        // Seed Role-Claim relationships
        modelBuilder.Entity<Role>()
            .HasMany(r => r.Claims)
            .WithMany(c => c.Roles)
            .UsingEntity(j =>
            {
                j.HasData(
                    new { RolesId = roleObserver.Id, ClaimsId = claimAuthEvents.Id },
                    new { RolesId = roleAuditor.Id, ClaimsId = claimAuthEvents.Id },
                    new { RolesId = roleAuditor.Id, ClaimsId = claimRoleChanges.Id }
                );
            });

        // ✅ Seed Default User
        var defaultUser = new User
        {
            Id = Guid.NewGuid(),
            ExternalId = "seed-user-sub",
            Email = "seeduser@example.com",
            RoleId = roleBasic.Id
        };

        modelBuilder.Entity<User>().HasData(defaultUser);
    }
}
