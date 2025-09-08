using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace Blazor.Services;

// AccessTokenHandler: attaches API access token from HttpContext to outgoing HttpClient requests
public class AccessTokenHandler(IHttpContextAccessor ctx) : DelegatingHandler
{
    private readonly IHttpContextAccessor _ctx = ctx;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _ctx.HttpContext;
        if (httpContext != null)
        {
            // Pull your API access token (custom JWT) instead of Okta's access_token
            var apiToken = await httpContext.GetTokenAsync("api_access_token");

            if (!string.IsNullOrWhiteSpace(apiToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
