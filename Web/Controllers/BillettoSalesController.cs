// [CHANGE: Billetto sales dashboard] Related: Code/Services/BillettoSalesService.cs, Web/App_Plugins/BillettoSales/*, Web/Program.cs
using Asp.Versioning;
using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Extensions;

namespace Web.Controllers;

[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("billettosales")]
[ApiExplorerSettings(GroupName = "Billetto Sales API")]
public class BillettoSalesController : ManagementApiControllerBase
{
    private readonly IBillettoSalesService _billettoSalesService;
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;
    private readonly ILogger<BillettoSalesController> _logger;

    public BillettoSalesController(
        IBillettoSalesService billettoSalesService,
        IBackOfficeSecurityAccessor backOfficeSecurityAccessor,
        ILogger<BillettoSalesController> logger)
    {
        _billettoSalesService = billettoSalesService;
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
        _logger = logger;
    }

    // Sales numbers are admin-only; the dashboard's IsAdmin condition only hides
    // the UI, so the API must enforce it server-side too
    private bool IsCurrentUserAdmin()
    {
        var user = _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser;
        return user != null && user.IsAdmin();
    }

    private IActionResult ForbiddenResult() =>
        StatusCode(StatusCodes.Status403Forbidden,
            new { success = false, message = "Kun administratorer har adgang til dette dashboard." });

    [HttpGet("progress")]
    public IActionResult GetProgress()
    {
        if (!IsCurrentUserAdmin()) return ForbiddenResult();

        var p = _billettoSalesService.GetFetchProgress();
        return Ok(new
        {
            active = p.Active,
            pagesFetched = p.PagesFetched,
            attendeesFetched = p.AttendeesFetched,
            ratelimitRemaining = p.RatelimitRemaining,
            ratelimitLimit = p.RatelimitLimit,
            throttledWaitSeconds = p.ThrottledWaitSeconds
        });
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] bool refresh = false)
    {
        if (!IsCurrentUserAdmin()) return ForbiddenResult();

        try
        {
            var result = await _billettoSalesService.GetSalesAsync(refresh);

            return Ok(new
            {
                success = result.ErrorMessage == null,
                configured = result.Configured,
                message = result.ErrorMessage,
                fetchedAt = result.FetchedAt,
                totalSold = result.TotalSold,
                totalCheckedIn = result.TotalCheckedIn,
                checkInDataAvailable = result.CheckInDataAvailable,
                cancelledCount = result.CancelledCount,
                ticketTypes = result.TicketTypes.Select(t => new
                {
                    id = t.Id,
                    name = t.Name,
                    sold = t.Sold,
                    checkedIn = t.CheckedIn
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Billetto ticket sales");
            return StatusCode(500, new { success = false, configured = true, message = $"Fejl: {ex.Message}" });
        }
    }
}
