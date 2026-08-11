// [CHANGE: Billetto ticket status dashboard] Related: Code/Services/BillettoTicketService.cs, Web/App_Plugins/BillettoTicketStatus/*, Web/Program.cs
using Asp.Versioning;
using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;

namespace Web.Controllers;

[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("billettoticketstatus")]
[ApiExplorerSettings(GroupName = "Billetto Ticket Status API")]
public class BillettoTicketStatusController : ManagementApiControllerBase
{
    private readonly IBillettoTicketService _billettoTicketService;
    private readonly ILogger<BillettoTicketStatusController> _logger;

    public BillettoTicketStatusController(
        IBillettoTicketService billettoTicketService,
        ILogger<BillettoTicketStatusController> logger)
    {
        _billettoTicketService = billettoTicketService;
        _logger = logger;
    }

    [HttpGet("progress")]
    public IActionResult GetProgress()
    {
        var p = _billettoTicketService.GetFetchProgress();
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

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus([FromQuery] bool refresh = false)
    {
        try
        {
            var result = await _billettoTicketService.GetStatusAsync(refresh);

            return Ok(new
            {
                success = result.ErrorMessage == null,
                configured = result.Configured,
                message = result.ErrorMessage,
                fetchedAt = result.FetchedAt,
                totalChecked = result.TotalChecked,
                withTicket = result.WithTicket,
                exemptCount = result.ExemptCount,
                missingCount = result.MissingMembers.Count,
                missingMembers = result.MissingMembers.Select(m => new
                {
                    memberId = m.MemberId,
                    memberKey = m.MemberKey,
                    fullName = m.FullName,
                    email = m.Email,
                    usesAltEmail = m.UsesAltEmail,
                    hasShift = m.HasShift,
                    crewNames = m.CrewNames
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Billetto ticket status");
            return StatusCode(500, new { success = false, configured = true, message = $"Fejl: {ex.Message}" });
        }
    }
}
