// [CHANGE: Billetto ordre property editor] Related: Code/Services/BillettoTicketService.cs, Web/App_Plugins/BillettoOrder/*
using Asp.Versioning;
using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;

namespace Web.Controllers;

[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("billettoorder")]
[ApiExplorerSettings(GroupName = "Billetto Order API")]
public class BillettoOrderController : ManagementApiControllerBase
{
    private readonly IBillettoTicketService _billettoTicketService;
    private readonly ILogger<BillettoOrderController> _logger;

    public BillettoOrderController(
        IBillettoTicketService billettoTicketService,
        ILogger<BillettoOrderController> logger)
    {
        _billettoTicketService = billettoTicketService;
        _logger = logger;
    }

    [HttpGet("order")]
    public async Task<IActionResult> GetOrder(
        [FromQuery] Guid memberKey,
        [FromQuery] string? billettoId = null,
        [FromQuery] string? altEmail = null,
        [FromQuery] bool refresh = false)
    {
        if (memberKey == Guid.Empty)
        {
            return BadRequest(new { success = false, message = "Ugyldigt medlem." });
        }

        try
        {
            var result = await _billettoTicketService.GetOrderForMemberAsync(memberKey, billettoId, altEmail, refresh);

            return Ok(new
            {
                success = result.ErrorMessage == null,
                configured = result.Configured,
                message = result.ErrorMessage
                    ?? (result.Found ? null : "Ingen Billetto-ordre fundet for hverken Billetto Id eller e-mail."),
                found = result.Found,
                matchedBy = result.MatchedBy,
                billettoId = result.BillettoId,
                order = result.Order
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Billetto order for member {MemberKey}", memberKey);
            return StatusCode(500, new { success = false, configured = true, message = $"Fejl: {ex.Message}" });
        }
    }
}
