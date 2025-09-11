
namespace Shared;

public record RoleDto(Guid Id, string Name);
public record ClaimDto(string Type, string Value);
public record UserDto(
    Guid Id,
    string ExternalId,
    string Provider,
    string Email,
    RoleDto Role,
    IEnumerable<ClaimDto>? Claims = null
);
public record SecurityEventDto(
    Guid Id,
    string EventType,
    Guid AuthorUserId,
    Guid AffectedUserId,
    DateTime OccurredUtc,
    string Details
);

// Mutation payloads
public record ProvisionPayload(UserDto User);
public record AssignRolePayload(bool Success, string Message, string OldRoleName, string NewRoleName);
public record SecurityEventPayload(SecurityEventDto Event);
