using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace Blazor.Services;

/// <summary>
/// DelegatingHandler that attaches the JWT stored in the cookie authentication tokens
/// as the Authorization: Bearer {token} header to outgoing requests.
/// </summary>
public class AccessTokenHandler(IHttpContextAccessor ctx) : DelegatingHandler
{
    private readonly IHttpContextAccessor _ctx = ctx;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _ctx.HttpContext;
        if (httpContext != null)
        {
            // read tokens stored on cookie auth properties
            var authResult = await httpContext.AuthenticateAsync();
            var props = authResult.Properties;
            if (props != null)
            {
                var tokens = props.GetTokens();
                var access = tokens?.FirstOrDefault(t => string.Equals(t.Name, "access_token", StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrWhiteSpace(access))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
