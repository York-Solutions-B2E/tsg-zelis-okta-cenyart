
namespace Api.Auth
{
    /// <summary>
    /// Small abstraction to read a user's role name from persistence.
    /// Mock this in unit tests.
    /// </summary>
    public interface IUserRoleProvider
    {
        /// <summary>
        /// Returns the role name (e.g. "BasicUser", "AuthObserver", "SecurityAuditor")
        /// for the given local user id, or null if not found.
        /// </summary>
        Task<string?> GetRoleNameAsync(Guid userId, CancellationToken ct = default);
    }
}
