using System.Text.Json;

namespace Blazor.Services;

public class ProvisioningClient(HttpClient http)
{
    private readonly HttpClient _http = http;
    private readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Calls GraphQL provisioning mutation and returns JWT string.
    /// Expects backend GraphQL mutation: provisionOnLogin(externalId, email, provider): String
    /// </summary>
    public async Task<string> ProvisionOnLoginAsync(string externalId, string email, string provider, CancellationToken ct = default)
    {
        // Minimal GraphQL payload
        var mutation = $@"mutation {{
            provisionOnLogin(externalId: ""{externalId}"", email: ""{email}"", provider: ""{provider}"")
        }}";

        var payload = new { query = mutation };

        var resp = await _http.PostAsJsonAsync("/graphql", payload, ct);
        resp.EnsureSuccessStatusCode();

        var text = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(text);
        // check for errors
        if (doc.RootElement.TryGetProperty("errors", out var errs))
            throw new ApplicationException("GraphQL errors: " + errs.ToString());

        // data.provisionOnLogin is a string containing JWT
        var data = doc.RootElement.GetProperty("data");
        if (!data.TryGetProperty("provisionOnLogin", out var tokenEl))
            throw new ApplicationException("Malformed provisioning response");

        return tokenEl.GetString() ?? throw new ApplicationException("Provisioning returned null token");
    }
}
