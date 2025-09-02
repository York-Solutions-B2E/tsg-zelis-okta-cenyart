using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace Blazor.Services;

public class AccessTokenHandler(IHttpContextAccessor ctx) : DelegatingHandler
{
    private readonly IHttpContextAccessor _ctx = ctx;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _ctx.HttpContext;
        if (httpContext != null)
        {
            // read the access_token saved in cookie auth properties during provisioning
            var token = await httpContext.GetTokenAsync("access_token");
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
