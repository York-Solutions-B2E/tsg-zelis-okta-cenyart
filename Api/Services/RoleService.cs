using Microsoft.EntityFrameworkCore;
using Api.Data;

namespace Api.Services;

public class RoleService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    /// <summary>
    /// Updates a user's role. Returns (success, message, oldRoleName, newRoleName).
    /// </summary>
    public async Task<(bool success, string message, string oldRole, string newRole)> UpdateUserRoleAsync(
        Guid userId, 
        Guid newRoleId, 
        CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null) 
            return (false, "User not found", "", "");

        var newRole = await _db.Roles.FirstOrDefaultAsync(r => r.Id == newRoleId, ct);
        if (newRole == null) 
            return (false, "Role not found", user.Role?.Name ?? "", "");

        var oldRoleName = user.Role?.Name ?? "Unknown";
        user.RoleId = newRoleId;
        await _db.SaveChangesAsync(ct);

        return (true, $"Role changed from {oldRoleName} to {newRole.Name}", oldRoleName, newRole.Name);
    }
}
