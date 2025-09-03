using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public class RoleService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    /// <summary>
    /// Updates a user's role (no events).
    /// Returns old role name and new role name for auditing.
    /// </summary>
    public async Task<(bool Success, string Message, string OldRole, string NewRole)> UpdateUserRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken ct = default)
    {
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null)
            return (false, "User not found", "", "");

        var oldRoleName = user.Role?.Name ?? "Unknown";

        var newRole = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);
        if (newRole == null)
            return (false, "Role not found", oldRoleName, "");

        user.RoleId = newRole.Id;
        await _db.SaveChangesAsync(ct);

        return (true, $"Role changed from {oldRoleName} to {newRole.Name}", oldRoleName, newRole.Name);
    }
}
