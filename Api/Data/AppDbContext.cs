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

        // Unique per provider + email
        modelBuilder.Entity<User>()
            .HasIndex(u => new { u.Provider, u.Email })
            .IsUnique();

        // Unique per provider + externalId (safer too)
        modelBuilder.Entity<User>()
            .HasIndex(u => new { u.Provider, u.ExternalId })
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
    }
}
