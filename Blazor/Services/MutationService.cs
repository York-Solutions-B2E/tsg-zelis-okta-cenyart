using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Shared;

namespace Blazor.Services;

public class MutationService(HttpClient http, ILogger<MutationService> logger)
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly ILogger<MutationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private async Task<JsonElement> PostDocumentAsync(string document, object? variables = null, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["query"] = document,
            ["variables"] = variables ?? new { }
        };

        var resp = await _http.PostAsJsonAsync("/graphql", payload, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        // Logging for debugging
        _logger.LogDebug("GraphQL response: {Body}", body);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"GraphQL HTTP error. Status={resp.StatusCode}, Body={body}");

        using var doc = JsonDocument.Parse(body);

        if (doc.RootElement.TryGetProperty("errors", out var errors))
            throw new ApplicationException("GraphQL errors: " + errors.ToString());

        if (!doc.RootElement.TryGetProperty("data", out var data))
            throw new ApplicationException("GraphQL response missing `data`.");

        // Return deep copy
        return JsonDocument.Parse(data.GetRawText()).RootElement.Clone();
    }

    private static Guid ParseGuid(JsonElement el) =>
        el.ValueKind == JsonValueKind.String ? Guid.Parse(el.GetString()!) : Guid.Parse(el.ToString());

    /// <summary>
    /// Calls the server mutation ProvisionOnLogin and returns ProvisionPayload (containing UserDto).
    /// </summary>
    public async Task<ProvisionPayload?> ProvisionOnLoginAsync(string externalId, string email, string provider, CancellationToken ct = default)
    {
        const string mutation = @"
        mutation($externalId: String!, $email: String!, $provider: String!) {
            provisionOnLogin(externalId: $externalId, email: $email, provider: $provider) {
                id
                externalId
                provider
                email
                role { id name }
                claims { type value }
            }
        }";

        var vars = new { externalId, email, provider };
        var data = await PostDocumentAsync(mutation, vars, ct);

        if (!data.TryGetProperty("provisionOnLogin", out var el))
            return null;

        // Extract role (safely)
        RoleDto roleDto = new RoleDto(Guid.Empty, "Unassigned");
        if (el.TryGetProperty("role", out var roleEl) && roleEl.ValueKind != JsonValueKind.Null)
        {
            roleDto = new RoleDto(ParseGuid(roleEl.GetProperty("id")), roleEl.GetProperty("name").GetString() ?? "");
        }

        // Extract claims
        List<ClaimDto> claims = new();
        if (el.TryGetProperty("claims", out var claimArr) && claimArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in claimArr.EnumerateArray())
            {
                var t = c.GetProperty("type").GetString() ?? "";
                var v = c.GetProperty("value").GetString() ?? "";
                claims.Add(new ClaimDto(t, v));
            }
        }

        var user = new UserDto(
            ParseGuid(el.GetProperty("id")),
            el.TryGetProperty("externalId", out var extEl) ? extEl.GetString() ?? "" : "",
            el.TryGetProperty("provider", out var provEl) ? provEl.GetString() ?? "" : "",
            el.TryGetProperty("email", out var emailEl) ? emailEl.GetString() ?? "" : "",
            roleDto,
            claims
        );

        return new ProvisionPayload(user);
    }

    public async Task<SecurityEventPayload?> AddSecurityEventAsync(
        string eventType,
        Guid authorUserId,
        Guid affectedUserId,
        string details,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Adding SecurityEvent. EventType={EventType}, AuthorUserId={AuthorUserId}, AffectedUserId={AffectedUserId}, Details={Details}",
            eventType, authorUserId, affectedUserId, details);

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
            el.GetProperty("details").GetString() ?? ""
        );

        _logger.LogInformation("SecurityEvent created. EventId={EventId}, EventType={EventType}", dto.Id, dto.EventType);
        return new SecurityEventPayload(dto);
    }

     public async Task<AssignRolePayload> AssignRoleAsync(
    Guid userId,
    Guid roleId,
    HttpContext httpContext,
    CancellationToken ct = default)
    {
        var authResult = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = authResult.Principal;
        var authorClaim = principal?.FindFirst("uid")?.Value;
        if (string.IsNullOrEmpty(authorClaim))
            throw new InvalidOperationException("Cannot determine author user ID from claims.");

        var authorId = Guid.Parse(authorClaim);

        const string mutation = @"
        mutation($userId: UUID!, $roleId: UUID!) {
            assignUserRole(userId: $userId, roleId: $roleId) {
                success
                message
                oldRoleName
                newRoleName
            }
        }";

        var oldRoleName = "";
        var newRoleName = "";
        var vars = new { userId, roleId, oldRoleName, newRoleName };
        var data = await PostDocumentAsync(mutation, vars, ct);

        var el = data.GetProperty("assignUserRole");
        var success = el.GetProperty("success").GetBoolean();
        var message = el.GetProperty("message").GetString() ?? "";
        var oldRole = el.GetProperty("oldRoleName").GetString() ?? "Unknown";
        var newRole = el.GetProperty("newRoleName").GetString() ?? "Unknown";

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

        return new AssignRolePayload(success, message, oldRole, newRole);
    }
}
