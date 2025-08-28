
namespace Api.Auth
{
    public class AuthorizationService(IUserRoleProvider roleProvider)
    {
        private readonly IUserRoleProvider _roleProvider = roleProvider ?? throw new ArgumentNullException(nameof(roleProvider));

        public async Task<bool> UserHasPermissionAsync(Guid userId, string permission, CancellationToken ct = default)
        {
            var roleName = await _roleProvider.GetRoleNameAsync(userId, ct);
            if (roleName == null) return false;

            // Basic policy mapping:
            // AuthObserver => Audit.ViewAuthEvents
            // SecurityAuditor => Audit.ViewAuthEvents + Audit.RoleChanges
            if (permission == "Audit.ViewAuthEvents")
            {
                return roleName == "AuthObserver" || roleName == "SecurityAuditor";
            }

            if (permission == "Audit.RoleChanges")
            {
                return roleName == "SecurityAuditor";
            }

            return false;
        }

        public Task<bool> CanViewAuthEventsAsync(Guid userId, CancellationToken ct = default)
            => UserHasPermissionAsync(userId, "Audit.ViewAuthEvents", ct);

        public Task<bool> CanViewRoleChangesAsync(Guid userId, CancellationToken ct = default)
            => UserHasPermissionAsync(userId, "Audit.RoleChanges", ct);
    }
}
