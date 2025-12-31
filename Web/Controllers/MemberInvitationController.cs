using Asp.Versioning;
using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;

namespace Web.Controllers;

[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("memberinvitation")]
[ApiExplorerSettings(GroupName = "Member Invitation API")]
public class MemberInvitationController : ManagementApiControllerBase
{
    private readonly IInvitationService _invitationService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MemberInvitationController(
        IInvitationService invitationService,
        IHttpContextAccessor httpContextAccessor)
    {
        _invitationService = invitationService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("members")]
    public async Task<IActionResult> GetMembers()
    {
        try
        {
            var statuses = await _invitationService.GetInvitationStatusesAsync();
            return Ok(new
            {
                success = true,
                members = statuses.OrderBy(m => m.FullName).ToList()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Failed to get members: {ex.Message}"
            });
        }
    }

    [HttpGet("crews")]
    public async Task<IActionResult> GetCrews()
    {
        try
        {
            var crews = await _invitationService.GetAvailableCrewsAsync();
            return Ok(new
            {
                success = true,
                crews = crews.OrderBy(c => c.Name).ToList()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Failed to get crews: {ex.Message}"
            });
        }
    }

    [HttpPost("invite/{memberId:int}")]
    public async Task<IActionResult> InviteMember(int memberId)
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var result = await _invitationService.SendInvitationAsync(memberId, baseUrl);

            if (result.Success)
            {
                return Ok(new
                {
                    success = true,
                    message = result.Message,
                    email = result.Email
                });
            }

            return BadRequest(new
            {
                success = false,
                message = result.Message,
                email = result.Email
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Failed to send invitation: {ex.Message}"
            });
        }
    }

    [HttpPost("invite-all")]
    public async Task<IActionResult> InviteAllMembers()
    {
        try
        {
            var baseUrl = GetBaseUrl();
            var result = await _invitationService.SendBulkInvitationsAsync(baseUrl);

            return Ok(new
            {
                success = result.Success,
                message = result.Message,
                totalMembers = result.TotalMembers,
                sentCount = result.SentCount,
                skippedCount = result.SkippedCount,
                errorCount = result.ErrorCount,
                errors = result.Errors
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = $"Failed to send bulk invitations: {ex.Message}"
            });
        }
    }

    private string GetBaseUrl()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null)
        {
            throw new InvalidOperationException("HttpContext is not available");
        }

        return $"{request.Scheme}://{request.Host}";
    }
}
