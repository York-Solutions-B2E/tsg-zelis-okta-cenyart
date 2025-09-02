using System.Text.Json;
using System.Text;

namespace Blazor.Services;

public class ProvisioningClient(HttpClient http)
{
    private readonly HttpClient _http = http;

    /// <summary>
    /// Calls backend GraphQL mutation provisionOnLogin and returns the JWT string.
    /// </summary>
    public async Task<string> ProvisionOnLoginAsync(string externalId, string email, string provider, CancellationToken ct = default)
    {
        var gql = new
        {
            query = $@"mutation {{
                provisionOnLogin(externalId: ""{Escape(externalId)}"", email: ""{Escape(email)}"", provider: ""{Escape(provider)}"")
            }}"
        };

        var req = new StringContent(JsonSerializer.Serialize(gql), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("graphql", req, ct);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // Response shape: { "data": { "provisionOnLogin": "<jwt>" } }
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("provisionOnLogin", out var tokenElem))
        {
            return tokenElem.GetString() ?? string.Empty;
        }

        // bubble up GraphQL error if present
        if (doc.RootElement.TryGetProperty("errors", out var errs))
        {
            throw new InvalidOperationException($"Provisioning error: {errs.ToString()}");
        }

        throw new InvalidOperationException("Invalid provisioning response");
    }

    private static string Escape(string s) => s.Replace("\"", "\\\"");
}
