using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Shared;

namespace Blazor.Services;

public class MutationService(HttpClient http, ILogger<TokenValidatedHandler> logger)
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly ILogger<TokenValidatedHandler> _logger = logger;

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

    private static Guid ParseGuid(JsonElement el) =>
        el.ValueKind == JsonValueKind.String ? Guid.Parse(el.GetString()!) : Guid.Parse(el.ToString());

    public async Task<ProvisionPayload?> ProvisionOnLoginAsync(string externalId, string email, string provider, CancellationToken ct = default)
    {
        const string mutation = @"
        mutation($externalId: String!, $email: String!, $provider: String!) {
            provisionOnLogin(externalId: $externalId, email: $email, provider: $provider) {
                id
                email
                role { id name }
                claims { type value }
            }
        }";

        var vars = new { externalId, email, provider };
        var data = await PostDocumentAsync(mutation, vars, ct);

        if (!data.TryGetProperty("provisionOnLogin", out var el))
            return null;

        // Extract role
        var roleEl = el.GetProperty("role");
        var roleDto = new RoleDto(
            ParseGuid(roleEl.GetProperty("id")),
            roleEl.GetProperty("name").GetString() ?? ""
        );

        // Extract claims safely
        List<ClaimDto> claims = new();
        if (el.TryGetProperty("claims", out var claimArr) && claimArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in claimArr.EnumerateArray())
            {
                var type = c.GetProperty("type").GetString() ?? "";
                var value = c.GetProperty("value").GetString() ?? "";
                claims.Add(new ClaimDto(type, value));
            }
        }

        // Extract user
        var user = new UserDto(
            ParseGuid(el.GetProperty("id")),
            el.GetProperty("email").GetString() ?? "",
            roleDto,
            claims
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
