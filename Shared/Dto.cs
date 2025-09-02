
namespace Shared;

public record RoleDto(Guid Id, string Name);
public record UserDto(Guid Id, string Email, RoleDto Role);
public record ProvisionResultDto(bool Success, string Message, UserDto? User, IEnumerable<string> Claims);
public record AssignRoleResultDto(bool Success, string? Message);
public record SecurityEventDto(Guid Id, string EventType, Guid AuthorUserId, Guid AffectedUserId, DateTime OccurredUtc, string? Details);
