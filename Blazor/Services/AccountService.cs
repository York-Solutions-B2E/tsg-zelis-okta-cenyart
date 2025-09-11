using System.Security.Claims;

namespace Blazor.Services;

public class AccountService(MutationService mutationService, ILogger<TokenValidatedHandler> logger, IHttpContextAccessor httpContextAccessor)
{
    private readonly MutationService _mutationService = mutationService ?? throw new ArgumentNullException(nameof(mutationService));
    private readonly ILogger<TokenValidatedHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    // -------------------
    // Event for LoginSuccess
    // -------------------
    public async Task LoginSuccessEvent(ClaimsPrincipal? principal, CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("LoginSuccessEvent: starting");

            if (principal == null || principal.Identity?.IsAuthenticated != true)
            {
                _logger.LogWarning("LoginSuccessEvent: principal is null or not authenticated.");
                return;
            }

            _logger.LogDebug("LoginSuccessEvent: principal found with {ClaimCount} claims", principal.Claims.Count());

            var uidClaim = principal.FindFirst("uid")?.Value;
            var providerClaim = principal.FindFirst("provider")?.Value ?? "Unknown";

            if (string.IsNullOrEmpty(uidClaim))
            {
                _logger.LogWarning("LoginSuccessEvent: 'uid' claim not found.");
                return;
            }

            _logger.LogDebug("LoginSuccessEvent: creating security event for UID={Uid}, provider={Provider}", uidClaim, providerClaim);

            await _mutationService.AddSecurityEventAsync(
                eventType: "LoginSuccess",
                authorUserId: Guid.Parse(uidClaim),
                affectedUserId: Guid.Parse(uidClaim),
                details: $"provider={providerClaim}",
                ct: ct
            );

            _logger.LogInformation("LoginSuccessEvent: successfully created LoginSuccess event for UID={Uid}", uidClaim);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoginSuccessEvent: failed to log LoginSuccess event");
        }
    }

    // -------------------
    // Event for Logout
    // -------------------
    public async Task LogoutEvent(Guid? uid)
    {
        if (!uid.HasValue)
        {
            throw new ApplicationException("LogoutEvent uid missing");
        }

        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? default;

        try
        {
            await _mutationService.AddSecurityEventAsync(
                eventType: "Logout",
                authorUserId: uid.Value,
                affectedUserId: uid.Value,
                details: "local sign-out",
                ct: ct
            );
            _logger.LogInformation("Logout event logged for UID {Uid}", uid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log logout event for UID {Uid}", uid);
        }
    }
}
