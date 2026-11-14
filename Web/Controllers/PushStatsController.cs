// [CHANGE: web push notifications — backoffice stats dashboard] Related: Code/Services/PushNotificationService.cs, Web/App_Plugins/PushStats/*
using Asp.Versioning;
using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Extensions;

namespace Web.Controllers;

[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("pushstats")]
[ApiExplorerSettings(GroupName = "Push Stats API")]
public class PushStatsController : ManagementApiControllerBase
{
    private readonly IPushNotificationService _pushService;
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;
    private readonly ILogger<PushStatsController> _logger;

    public PushStatsController(
        IPushNotificationService pushService,
        IBackOfficeSecurityAccessor backOfficeSecurityAccessor,
        ILogger<PushStatsController> logger)
    {
        _pushService = pushService;
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
        _logger = logger;
    }

    // GET /umbraco/management/api/v1/pushstats/summary — aggregated counts only, no member data
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        try
        {
            var stats = await _pushService.GetOverviewAsync();
            return Ok(new
            {
                success = true,
                configured = stats.Configured,
                isAdmin = IsCurrentUserAdmin(),
                totalDevices = stats.TotalDevices,
                totalSubscribedMembers = stats.TotalSubscribedMembers,
                devicesIos = stats.DevicesIos,
                devicesAndroid = stats.DevicesAndroid,
                devicesDesktop = stats.DevicesDesktop,
                latestSubscriptionUtc = stats.LatestSubscriptionUtc,
                crews = stats.Crews.Select(c => new
                {
                    id = c.CrewId,
                    name = c.CrewName,
                    memberCount = c.MemberCount,
                    subscribedCount = c.SubscribedMemberCount,
                    deviceCount = c.DeviceCount
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build push stats");
            return StatusCode(500, new { success = false, message = $"Fejl: {ex.Message}" });
        }
    }

    // POST /umbraco/management/api/v1/pushstats/generate-keys — admin-only setup helper.
    // Keys are generated on the fly and never stored; the admin copies them into configuration.
    [HttpPost("generate-keys")]
    public IActionResult GenerateKeys()
    {
        if (!IsCurrentUserAdmin())
            return StatusCode(StatusCodes.Status403Forbidden,
                new { success = false, message = "Kun administratorer kan generere nøgler." });

        if (_pushService.IsConfigured)
            return BadRequest(new { success = false, message = "Push er allerede sat op." });

        var (publicKey, privateKey) = PushNotificationService.GenerateVapidKeys();
        return Ok(new { success = true, publicKey, privateKey });
    }

    private bool IsCurrentUserAdmin()
    {
        var user = _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;
        return user != null && user.IsAdmin();
    }
}
