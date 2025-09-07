using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Shared;

namespace Blazor.Services;

public class MutationService(HttpClient http)
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

    private static Guid ParseGuid(JsonElement el) =>
        el.ValueKind == JsonValueKind.String ? Guid.Parse(el.GetString()!) : Guid.Parse(el.ToString());

    public async Task<ProvisionPayload?> ProvisionOnLoginAsync(string externalId, string email, string provider, CancellationToken ct = default)
    {
        const string mutation = @"
            mutation($externalId: String!, $email: String!, $provider: String!) {
                provisionOnLogin(externalId: $externalId, email: $email, provider: $provider) {
                    user { id email role { id name } claims { type value } }
                }
            }";

        var vars = new { externalId, email, provider };
        var data = await PostDocumentAsync(mutation, vars, ct);

        if (!data.TryGetProperty("provisionOnLogin", out var el)) return null;

        var userEl = el.GetProperty("user");
        var user = new UserDto(
            ParseGuid(userEl.GetProperty("id")),
            userEl.GetProperty("email").GetString() ?? "",
            new RoleDto(ParseGuid(userEl.GetProperty("role").GetProperty("id")), userEl.GetProperty("role").GetProperty("name").GetString() ?? ""),
            userEl.TryGetProperty("claims", out var claimArr) && claimArr.ValueKind == JsonValueKind.Array
                ? claimArr.EnumerateArray().Select(c => new ClaimDto(c.GetProperty("type").GetString() ?? "", c.GetProperty("value").GetString() ?? "")).ToList()
                : new List<ClaimDto>()
        );

        return new ProvisionPayload(user);
    }

    public async Task<AssignRolePayload> AssignRoleAsync(
    Guid userId,
    Guid roleId,
    HttpContext httpContext,
    CancellationToken ct = default)
    {
        // 1. Get author ID from cookie claims
        var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = authResult.Principal;
        var authorClaim = principal?.FindFirst("uid")?.Value;
        if (string.IsNullOrEmpty(authorClaim))
            throw new InvalidOperationException("Cannot determine author user ID from claims.");

        var authorId = Guid.Parse(authorClaim);

        // 2. Call backend RoleService (via mutation) to update user's role
        const string mutation = @"
        mutation($userId: UUID!, $roleId: UUID!) {
            assignRole(userId: $userId, roleId: $roleId) {
                success
                message
                oldRole
                newRole
            }
        }";

        var vars = new { userId, roleId };
        var data = await PostDocumentAsync(mutation, vars, ct);

        var el = data.GetProperty("assignRole");
        var success = el.GetProperty("success").GetBoolean();
        var message = el.GetProperty("message").GetString() ?? "";
        var oldRole = el.GetProperty("oldRole").GetString() ?? "Unknown";
        var newRole = el.GetProperty("newRole").GetString() ?? "Unknown";

        // 3. Log RoleAssigned security event if successful
        if (success)
        {
            await AddSecurityEventAsync(
                eventType: "RoleAssigned",
                authorUserId: authorId,
                affectedUserId: userId,
                details: $"from={oldRole} to={newRole}",
                ct: ct
            );
        }

        return new AssignRolePayload(success, message);
    }

    public async Task<SecurityEventPayload?> AddSecurityEventAsync(string eventType, Guid authorUserId, Guid affectedUserId, string details, CancellationToken ct = default)
    {
        const string mutation = @"
            mutation($eventType: String!, $authorUserId: UUID!, $affectedUserId: UUID!, $details: String!) {
                addSecurityEvent(eventType: $eventType, authorUserId: $authorUserId, affectedUserId: $affectedUserId, details: $details) {
                    id eventType authorUserId affectedUserId occurredUtc details
                }
            }";

        var vars = new { eventType, authorUserId, affectedUserId, details };
        var data = await PostDocumentAsync(mutation, vars, ct);

        if (!data.TryGetProperty("addSecurityEvent", out var el)) return null;

        var dto = new SecurityEventDto(
            ParseGuid(el.GetProperty("id")),
            el.GetProperty("eventType").GetString() ?? "",
            ParseGuid(el.GetProperty("authorUserId")),
            ParseGuid(el.GetProperty("affectedUserId")),
            el.GetProperty("occurredUtc").GetDateTime(),
            el.GetProperty("details").GetString() ?? "");

        return new SecurityEventPayload(dto);
    }
}
