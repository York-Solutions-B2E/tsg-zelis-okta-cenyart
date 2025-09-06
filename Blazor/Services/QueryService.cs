using System.Text.Json;
using Shared;

namespace Blazor.Services;

public class QueryService(HttpClient http)
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    private async Task<JsonElement> PostDocumentAsync(string document, object? variables = null, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["query"] = document,
            ["variables"] = variables ?? new { }
        };

        var resp = await _http.PostAsJsonAsync("/graphql", payload, ct);
        resp.EnsureSuccessStatusCode();

        var text = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("errors", out var errors))
            throw new ApplicationException("GraphQL errors: " + errors.ToString());

        if (!doc.RootElement.TryGetProperty("data", out var data))
            throw new ApplicationException("GraphQL response missing `data`.");

        return data;
    }

    private static Guid ParseGuidFromJsonElement(JsonElement el)
    {
        // GraphQL often returns IDs as strings — handle both string and GUID types
        return el.ValueKind switch
        {
            JsonValueKind.String => Guid.Parse(el.GetString()!),
            JsonValueKind.Number => throw new ApplicationException("Unexpected numeric id in GraphQL result."),
            JsonValueKind.Null => Guid.Empty,
            _ => Guid.Parse(el.ToString())
        };
    }

    public async Task<List<UserDto>> GetUsersAsync(CancellationToken ct = default)
    {
        const string q = @"{ users { id email role { id name } } }";
        var data = await PostDocumentAsync(q, null, ct);

        if (!data.TryGetProperty("users", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new List<UserDto>();

        var list = new List<UserDto>();
        foreach (var el in arr.EnumerateArray())
        {
            var id = ParseGuidFromJsonElement(el.GetProperty("id"));
            var email = el.GetProperty("email").GetString() ?? "";
            var roleEl = el.GetProperty("role");
            var role = new RoleDto(ParseGuidFromJsonElement(roleEl.GetProperty("id")), roleEl.GetProperty("name").GetString() ?? "");
            list.Add(new UserDto(id, email, role));
        }
        return list;
    }

    public async Task<List<RoleDto>> GetRolesAsync(CancellationToken ct = default)
    {
        const string q = @"{ roles { id name } }";
        var data = await PostDocumentAsync(q, null, ct);

        if (!data.TryGetProperty("roles", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new List<RoleDto>();

        return arr.EnumerateArray()
            .Select(el => new RoleDto(ParseGuidFromJsonElement(el.GetProperty("id")), el.GetProperty("name").GetString() ?? ""))
            .ToList();
    }

    public async Task<List<SecurityEventDto>> GetSecurityEventsAsync(CancellationToken ct = default)
    {
        const string q = @"{ securityEvents { id eventType authorUserId affectedUserId occurredUtc details } }";
        var data = await PostDocumentAsync(q, null, ct);

        if (!data.TryGetProperty("securityEvents", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new List<SecurityEventDto>();

        var outList = new List<SecurityEventDto>();
        foreach (var el in arr.EnumerateArray())
        {
            var id = ParseGuidFromJsonElement(el.GetProperty("id"));
            var eventType = el.GetProperty("eventType").GetString() ?? "";
            var authorUserId = ParseGuidFromJsonElement(el.GetProperty("authorUserId"));
            var affectedUserId = ParseGuidFromJsonElement(el.GetProperty("affectedUserId"));
            var occurredUtc = el.GetProperty("occurredUtc").GetDateTime();
            var details = el.GetProperty("details").GetString() ?? string.Empty;

            outList.Add(new SecurityEventDto(id, eventType, authorUserId, affectedUserId, occurredUtc, details));
        }

        return outList.OrderByDescending(e => e.OccurredUtc).ToList();
    }
}
