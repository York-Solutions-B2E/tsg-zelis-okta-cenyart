using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace Blazor.Services;

// AccessTokenHandler: attaches access token from HttpContext to outgoing HttpClient requests
public class AccessTokenHandler(IHttpContextAccessor ctx) : DelegatingHandler
{
    private readonly IHttpContextAccessor _ctx = ctx;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _ctx.HttpContext;
        if (httpContext != null)
        {
            var accessToken = await httpContext.GetTokenAsync("access_token");
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
