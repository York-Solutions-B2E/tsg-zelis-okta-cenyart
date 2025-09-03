using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Linq;
using Shared; // for DTOs like UserDto, RoleDto, SecurityEventDto, AssignRoleResultDto

namespace Blazor.Services;

public class GraphQLService
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

    public GraphQLService(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <summary>
    /// Posts a GraphQL document. Returns the "data" JsonElement.
    /// Throws ApplicationException when GraphQL returns errors or response is invalid.
    /// </summary>
    private async Task<JsonElement> PostQueryAsync(string query, object? variables = null)
    {
        // Ensure the payload always has a 'variables' property (some GraphQL servers expect it)
        var payload = new Dictionary<string, object?>
        {
            ["query"] = query,
            ["variables"] = variables ?? new { }
        };

        var response = await _http.PostAsJsonAsync("/graphql", payload);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        if (doc.RootElement.TryGetProperty("errors", out var errors))
            throw new ApplicationException("GraphQL errors: " + errors.ToString());

        if (!doc.RootElement.TryGetProperty("data", out var data))
            throw new ApplicationException("GraphQL response missing 'data'.");

        return data;
    }

    // ---- Queries / Mutations (examples) ----

    public async Task<List<UserDto>> GetUsersAsync()
    {
        const string q = @"{ users { id email role { id name } } }";
        var data = await PostQueryAsync(q);
        if (!data.TryGetProperty("users", out var arr) || arr.ValueKind != JsonValueKind.Array) return new();

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
        if (!data.TryGetProperty("roles", out var arr) || arr.ValueKind != JsonValueKind.Array) return new();

        var list = arr.EnumerateArray()
            .Select(el => new RoleDto(el.GetProperty("id").GetGuid(), el.GetProperty("name").GetString() ?? ""))
            .ToList();

        return list;
    }

    public async Task<List<SecurityEventDto>> GetSecurityEventsAsync()
    {
        const string q = @"{ securityEvents { id eventType authorUserId affectedUserId occurredUtc details } }";
        var data = await PostQueryAsync(q);
        if (!data.TryGetProperty("securityEvents", out var arr) || arr.ValueKind != JsonValueKind.Array) return new();

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

        if (!data.TryGetProperty("assignUserRole", out var obj) || obj.ValueKind != JsonValueKind.Object) return null;

        if (obj.TryGetProperty("success", out var successEl))
        {
            var msg = obj.TryGetProperty("message", out var m) ? m.GetString() : null;
            return new AssignRoleResultDto(successEl.GetBoolean(), msg);
        }

        return null;
    }
}
