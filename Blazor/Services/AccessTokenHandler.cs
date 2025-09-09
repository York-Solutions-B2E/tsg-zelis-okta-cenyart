using System.Net.Http.Headers;

namespace Blazor.Services;

// AccessTokenHandler: attaches API access token from HttpContext to outgoing HttpClient requests
public class AccessTokenHandler(IHttpContextAccessor ctx, ILogger<AccessTokenHandler> logger) : DelegatingHandler
{
    private readonly IHttpContextAccessor _ctx = ctx;
    private readonly ILogger<AccessTokenHandler> _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _ctx.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            var apiToken = httpContext.User.FindFirst("api_access_token")?.Value;

            if (!string.IsNullOrWhiteSpace(apiToken))
            {
                _logger.LogDebug("AccessTokenHandler: found api_access_token claim. Len={Len}", apiToken.Length);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            }
            else
            {
                _logger.LogDebug("AccessTokenHandler: no api_access_token claim found");
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
