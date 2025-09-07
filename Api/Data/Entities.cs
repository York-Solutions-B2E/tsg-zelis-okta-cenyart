using System.ComponentModel.DataAnnotations;

namespace Api.Data;

public class User
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(200)]
    public string ExternalId { get; set; } = null!;

    [Required, MaxLength(320)]
    public string Email { get; set; } = null!;

    [Required]
    public Guid RoleId { get; set; }

    public Role Role { get; set; } = null!;
}

public class Role
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(200)]
    public string? Description { get; set; }

    public ICollection<User> Users { get; set; } = [];
    public ICollection<Claim> Claims { get; set; } = [];
}

public class Claim
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(100)]
    public string Type { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Value { get; set; } = null!;

    public ICollection<Role> Roles { get; set; } = [];
}

public class SecurityEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string EventType { get; set; } = null!;

    [Required]
    public Guid AuthorUserId { get; set; }
    public User AuthorUser { get; set; } = null!;

    [Required]
    public Guid AffectedUserId { get; set; }
    public User AffectedUser { get; set; } = null!;

    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(400)]
    public required string Details { get; set; }
}
