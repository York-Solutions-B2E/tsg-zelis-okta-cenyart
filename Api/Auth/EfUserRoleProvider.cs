using Microsoft.EntityFrameworkCore;
using Api.Data;

namespace Api.Auth
{
    public class EfUserRoleProvider(AppDbContext db) : IUserRoleProvider
    {
        private readonly AppDbContext _db = db;

        public async Task<string?> GetRoleNameAsync(Guid userId, CancellationToken ct = default)
        {
            var user = await _db.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == userId, ct);
            return user?.Role?.Name;
        }
    }
}
