using System.Text.Json;
using Shared;

namespace Blazor.Services
{
    /// <summary>
    /// Mutations and write operations. ProvisionOnLoginAsync returns the JWT string.
    /// Also contains AssignUserRoleAsync and AddSecurityEventAsync (mutations).
    /// </summary>
    public class MutationService(HttpClient http, ILogger<MutationService> logger)
    {
        private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
        private readonly ILogger<MutationService> _log = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

        private async Task<JsonElement> PostDocumentAsync(string document, object? variables = null, CancellationToken ct = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["query"] = document,
                ["variables"] = variables ?? new { }
            };

            var requestJson = JsonSerializer.Serialize(payload);
            _log.LogInformation("GraphQL POST (provision): url={Url}, payload={Payload}", _http.BaseAddress, requestJson);

            HttpResponseMessage resp;
            try
            {
                resp = await _http.PostAsJsonAsync("/graphql", payload, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "HTTP POST to GraphQL failed (exception) url={Url}", _http.BaseAddress);
                throw;
            }

            string body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogError("GraphQL POST returned {StatusCode}. Response body: {Body}", (int)resp.StatusCode, body);
                resp.EnsureSuccessStatusCode(); // rethrow as HttpRequestException
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors))
            {
                _log.LogError("GraphQL returned errors: {Errors}", errors.ToString());
                throw new ApplicationException("GraphQL errors: " + errors.ToString());
            }

            if (!doc.RootElement.TryGetProperty("data", out var data))
            {
                _log.LogError("GraphQL response missing data. Full body: {Body}", body);
                throw new ApplicationException("GraphQL response missing `data`.");
            }

            _log.LogDebug("GraphQL response data: {Data}", data.ToString());
            return data.Clone();
        }

        /// <summary>
        /// Calls the server mutation ProvisionOnLoginAsync(...) and returns the JWT token string.
        /// </summary>
        public async Task<string> ProvisionOnLoginAsync(string externalId, string email, string provider, CancellationToken ct = default)
        {
            var mutation = @"
                mutation ($externalId: String!, $email: String!, $provider: String!) {
                    provisionOnLogin(externalId: $externalId, email: $email, provider: $provider)
                }";

            var vars = new { externalId, email, provider };

            try
            {
                var data = await PostDocumentAsync(mutation, vars, ct);

                if (!data.TryGetProperty("provisionOnLogin", out var tokEl) || tokEl.ValueKind != JsonValueKind.String)
                {
                    var msg = $"Provisioning did not return a token. Raw data: {data.ToString()}";
                    _log.LogWarning(msg);
                    throw new ApplicationException(msg);
                }

                var token = tokEl.GetString()!;
                _log.LogInformation("Provisioning succeeded for externalId={ExternalId}, provider={Provider}", externalId, provider);
                return token;
            }
            catch (HttpRequestException httpEx)
            {
                // This typically contains status + body from PostDocumentAsync
                _log.LogError(httpEx, "Provisioning HTTP failure for externalId={ExternalId}, provider={Provider}: {Message}", externalId, provider, httpEx.Message);
                throw;
            }
            catch (ApplicationException appEx)
            {
                _log.LogError(appEx, "Provisioning GraphQL error for externalId={ExternalId}, provider={Provider}: {Message}", externalId, provider, appEx.Message);
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected error while provisioning user {ExternalId} (provider={Provider})", externalId, provider);
                throw;
            }
        }

        /// <summary>
        /// Assigns a role to a user using the assignUserRole mutation. Expects server to return AssignRoleResultDto shape.
        /// </summary>
        public async Task<AssignRoleResultDto> AssignUserRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default)
        {
            var mutation = @"
                mutation ($userId: ID!, $roleId: ID!) {
                    assignUserRole(userId: $userId, roleId: $roleId) {
                        success
                        message
                    }
                }";

            var vars = new { userId = userId.ToString(), roleId = roleId.ToString() };
            var data = await PostDocumentAsync(mutation, vars, ct);

            if (!data.TryGetProperty("assignUserRole", out var obj) || obj.ValueKind != JsonValueKind.Object)
                throw new ApplicationException("assignUserRole returned unexpected payload");

            var success = obj.GetProperty("success").GetBoolean();
            var message = obj.GetProperty("message").GetString() ?? string.Empty;
            return new AssignRoleResultDto(success, message);
        }

        /// <summary>
        /// Adds a security event. Requires the caller's JWT to be attached to the HttpClient.
        /// </summary>
        public async Task<SecurityEventDto> AddSecurityEventAsync(string eventType, Guid affectedUserId, string? details = null, CancellationToken ct = default)
        {
            var mutation = @"
                mutation ($eventType: String!, $affectedUserId: ID!, $details: String) {
                    addSecurityEvent(eventType: $eventType, affectedUserId: $affectedUserId, details: $details) {
                        id eventType authorUserId affectedUserId occurredUtc details
                    }
                }";

            var vars = new { eventType, affectedUserId = affectedUserId.ToString(), details };
            var data = await PostDocumentAsync(mutation, vars, ct);

            if (!data.TryGetProperty("addSecurityEvent", out var obj) || obj.ValueKind != JsonValueKind.Object)
                throw new ApplicationException("addSecurityEvent returned unexpected payload");

            var id = Guid.Parse(obj.GetProperty("id").GetString()!);
            var dto = new SecurityEventDto(
                id,
                obj.GetProperty("eventType").GetString() ?? string.Empty,
                Guid.Parse(obj.GetProperty("authorUserId").GetString()!),
                Guid.Parse(obj.GetProperty("affectedUserId").GetString()!),
                obj.GetProperty("occurredUtc").GetDateTime(),
                obj.TryGetProperty("details", out var d) && d.ValueKind != JsonValueKind.Null ? d.GetString() : null
            );

            return dto;
        }
    }
}
