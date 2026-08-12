// [CHANGE: SuperAdmin ticket sales page] Related: Code/Services/SuperAdminService.cs, Web/Controllers/BbvTicketSalesController.cs, Web/Views/TicketSales.cshtml
using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Security;

namespace Web.Controllers;

/// <summary>
/// Frontend JSON endpoints for the SuperAdmin ticket sales page. Mirrors the
/// backoffice BillettoSalesController but authorizes against the SuperAdmin
/// member group instead of backoffice admins. Reuses IBillettoSalesService so
/// frontend and backoffice share the same 10-minute cache and fetch progress.
/// Plain [Route] controller like ScheduleController to avoid surface-controller
/// routing quirks.
/// </summary>
[Route("umbraco/surface/ticketsales")]
public class TicketSalesApiController : Controller
{
    private readonly IBillettoSalesService _billettoSalesService;
    private readonly IMemberManager _memberManager;
    private readonly ISuperAdminService _superAdminService;
    private readonly ILogger<TicketSalesApiController> _logger;

    public TicketSalesApiController(
        IBillettoSalesService billettoSalesService,
        IMemberManager memberManager,
        ISuperAdminService superAdminService,
        ILogger<TicketSalesApiController> logger)
    {
        _billettoSalesService = billettoSalesService;
        _memberManager = memberManager;
        _superAdminService = superAdminService;
        _logger = logger;
    }

    // The page controller already gates the page, but the endpoints must
    // enforce access themselves — they are directly reachable by URL.
    // [CHANGE: review fix — load the member once and pass it on instead of three
    // member-store round-trips per request (progress is polled every second)]
    // Related: Code/Services/SuperAdminService.cs
    private async Task<IActionResult?> AuthorizeAsync()
    {
        var member = await _memberManager.GetCurrentMemberAsync();
        if (member == null)
            return Unauthorized(new { success = false, message = "Du er ikke logget ind." });

        if (!await _superAdminService.IsSuperAdminAsync(member))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { success = false, message = "Kun SuperAdmin har adgang til billetsalgstal." });

        return null;
    }

    // GET /umbraco/surface/ticketsales/progress
    [HttpGet("progress")]
    public async Task<IActionResult> GetProgress()
    {
        var denied = await AuthorizeAsync();
        if (denied != null) return denied;

        return Json(BillettoSalesPayload.Progress(_billettoSalesService.GetFetchProgress()));
    }

    // GET /umbraco/surface/ticketsales/summary — cached read only
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
        => await SummaryAsync(refresh: false);

    // POST /umbraco/surface/ticketsales/refresh — clears the cache and re-syncs
    // from Billetto. [CHANGE: review fix — a force refresh clears cache, burns
    // Billetto rate limit and rewrites the sync file, so it must not be reachable
    // via a cookie-riding cross-site GET; POST + antiforgery like the repo's other
    // state-changing endpoints] Related: Web/Views/TicketSales.cshtml
    [HttpPost("refresh")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshSummary()
        => await SummaryAsync(refresh: true);

    private async Task<IActionResult> SummaryAsync(bool refresh)
    {
        var denied = await AuthorizeAsync();
        if (denied != null) return denied;

        try
        {
            var result = await _billettoSalesService.GetSalesAsync(refresh);
            return Json(BillettoSalesPayload.Summary(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Billetto ticket sales for frontend page");
            return StatusCode(500, BillettoSalesPayload.Error(ex));
        }
    }
}
