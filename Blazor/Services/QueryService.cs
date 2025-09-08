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

        var body = await resp.Content.ReadAsStringAsync(ct);

        // Log response for debugging
        Console.WriteLine($"GraphQL response: {body}");

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"GraphQL HTTP error. Status={resp.StatusCode}, Body={body}");

        using var doc = JsonDocument.Parse(body);

        if (doc.RootElement.TryGetProperty("errors", out var errors))
            throw new ApplicationException("GraphQL errors: " + errors.ToString());

        if (!doc.RootElement.TryGetProperty("data", out var data))
            throw new ApplicationException("GraphQL response missing `data`.");

        // Return a deep copy so it doesn't depend on disposed JsonDocument
        return JsonDocument.Parse(data.GetRawText()).RootElement.Clone();
    }

    private static Guid ParseGuidFromJsonElement(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => Guid.Parse(el.GetString()!),
            JsonValueKind.Null => Guid.Empty,
            _ => Guid.Parse(el.ToString())
        };

    public async Task<List<UserDto>> GetUsersAsync(CancellationToken ct = default)
    {
        const string q = @"{ users { id email role { id name } claims { type value } } }";
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

            var claims = new List<ClaimDto>();
            if (el.TryGetProperty("claims", out var claimArr) && claimArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in claimArr.EnumerateArray())
                {
                    claims.Add(new ClaimDto(
                        c.GetProperty("type").GetString() ?? "",
                        c.GetProperty("value").GetString() ?? ""));
                }
            }

            list.Add(new UserDto(id, email, role, claims));
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

        return arr.EnumerateArray()
            .Select(el => new SecurityEventDto(
                ParseGuidFromJsonElement(el.GetProperty("id")),
                el.GetProperty("eventType").GetString() ?? "",
                ParseGuidFromJsonElement(el.GetProperty("authorUserId")),
                ParseGuidFromJsonElement(el.GetProperty("affectedUserId")),
                el.GetProperty("occurredUtc").GetDateTime(),
                el.GetProperty("details").GetString() ?? ""))
            .OrderByDescending(e => e.OccurredUtc)
            .ToList();
    }
}
