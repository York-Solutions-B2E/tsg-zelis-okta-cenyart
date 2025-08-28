
namespace Api.GraphQL;

public record RoleDto(Guid Id, string Name);
public record UserDto(Guid Id, string Email, RoleDto Role);
public record ProvisionResult(bool Success, string Message, UserDto? User);
