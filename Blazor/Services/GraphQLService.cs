using System.Text.Json;
using Shared;

namespace Blazor.Services;

public class GraphQLService(HttpClient http)
{
    private readonly HttpClient _http = http;
    private readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Posts a GraphQL document. Returns the "data" JsonElement.
    /// Throws ApplicationException when GraphQL returns errors or response is invalid.
    /// </summary>
    private async Task<JsonElement> PostQueryAsync(string query, object? variables = null)
    {
        // Always send "variables" key, even if empty
        var payload = new
        {
            query,
            variables = variables ?? new { }
        };

        var response = await _http.PostAsJsonAsync("/graphql", payload);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (doc.RootElement.TryGetProperty("errors", out var errors))
            throw new ApplicationException("GraphQL errors: " + errors.ToString());

        return doc.RootElement.GetProperty("data");
    }

    // Queries / Mutations ------------------------------------------------
    public async Task<List<UserDto>> GetUsersAsync()
    {
        const string q = @"{ users { id email role { id name } } }";
        var data = await PostQueryAsync(q);
        if (!data.TryGetProperty("users", out var arr)) return new();

        var list = new List<UserDto>();
        foreach (var el in arr.EnumerateArray())
        {
            var id = el.GetProperty("id").GetGuid();
            var email = el.GetProperty("email").GetString() ?? "";
            var roleEl = el.GetProperty("role");
            var role = new RoleDto(roleEl.GetProperty("id").GetGuid(), roleEl.GetProperty("name").GetString() ?? "");
            list.Add(new UserDto(id, email, role));
        }
        return list;
    }

    public async Task<List<RoleDto>> GetRolesAsync()
    {
        const string q = @"{ roles { id name } }";
        var data = await PostQueryAsync(q);
        if (!data.TryGetProperty("roles", out var arr)) return new();

        var list = new List<RoleDto>();
        foreach (var el in arr.EnumerateArray())
        {
            list.Add(new RoleDto(el.GetProperty("id").GetGuid(), el.GetProperty("name").GetString() ?? ""));
        }
        return list;
    }

    public async Task<List<SecurityEventDto>> GetSecurityEventsAsync()
    {
        const string q = @"{ securityEvents { id eventType authorUserId affectedUserId occurredUtc details } }";
        var data = await PostQueryAsync(q);
        if (!data.TryGetProperty("securityEvents", out var arr)) return new();

        var list = new List<SecurityEventDto>();
        foreach (var el in arr.EnumerateArray())
        {
            var dto = new SecurityEventDto(
                el.GetProperty("id").GetGuid(),
                el.GetProperty("eventType").GetString() ?? "",
                el.GetProperty("authorUserId").GetGuid(),
                el.GetProperty("affectedUserId").GetGuid(),
                el.GetProperty("occurredUtc").GetDateTime(),
                el.TryGetProperty("details", out var d) ? d.GetString() : null
            );
            list.Add(dto);
        }
        return list.OrderByDescending(s => s.OccurredUtc).ToList();
    }

    /// <summary>
    /// Queries that return boolean guards. Assumes server implements guard using JWT or caller context.
    /// </summary>
    public async Task<bool> CanViewAuthEventsAsync()
    {
        const string q = @"{ canViewAuthEvents }";
        var data = await PostQueryAsync(q);
        return data.GetProperty("canViewAuthEvents").GetBoolean();
    }

    public async Task<bool> CanViewRoleChangesAsync()
    {
        const string q = @"{ canViewRoleChanges }";
        var data = await PostQueryAsync(q);
        return data.GetProperty("canViewRoleChanges").GetBoolean();
    }

    /// <summary>
    /// assignUserRole expects variables: userId, roleId. Returns success + message if your GraphQL returns that shape.
    /// </summary>
    public async Task<AssignRoleResultDto?> AssignUserRoleAsync(Guid userId, Guid roleId)
    {
        var q = @"
            mutation ($userId: ID!, $roleId: ID!) {
              assignUserRole(userId: $userId, roleId: $roleId) {
                success
                message
              }
            }";
        var vars = new { userId = userId.ToString(), roleId = roleId.ToString() };

        var data = await PostQueryAsync(q, vars);

        if (!data.TryGetProperty("assignUserRole", out var obj)) return null;

        // handle both shapes: either { success, message } or nested user
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("success", out var successEl))
        {
            var msg = obj.TryGetProperty("message", out var m) ? m.GetString() : null;
            return new AssignRoleResultDto(successEl.GetBoolean(), msg);
        }

        return null;
    }
}
