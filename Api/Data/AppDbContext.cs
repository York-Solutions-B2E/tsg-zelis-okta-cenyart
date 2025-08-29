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
                .HasForeignKey(e => e.AuthorUserId);

            b.HasOne(e => e.AffectedUser)
                .WithMany()
                .HasForeignKey(e => e.AffectedUserId);
        });
    }
}
