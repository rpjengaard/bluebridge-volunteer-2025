using Asp.Versioning;
using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Services;

namespace Web.Controllers;

[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("memberimpersonation")]
[ApiExplorerSettings(GroupName = "Member Impersonation API")]
public class MemberImpersonationController : ManagementApiControllerBase
{
    private readonly IMemberImpersonationService _impersonationService;
    private readonly IMemberService _memberService;

    public MemberImpersonationController(
        IMemberImpersonationService impersonationService,
        IMemberService memberService)
    {
        _impersonationService = impersonationService;
        _memberService = memberService;
    }

    [HttpGet("members")]
    public IActionResult GetMembers([FromQuery] string? search = null)
    {
        var members = _memberService.GetAll(0, 1000, out long totalRecords);

        // only show accepted members
        members = members.Where(m => m.GetValue<bool>("accept2026") == true).ToList();

        var filteredMembers = members
            .Where(m => string.IsNullOrEmpty(search) ||
                       (m.Username?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (m.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (m.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(50)
            .Select(m => new
            {
                email = m.Email,
                name = m.Name,
                username = m.Username,
                firstName = m.GetValue<string>("firstName"),
                lastName = m.GetValue<string>("lastName"),
                isApproved = m.IsApproved
            })
            .ToList();

        return Ok(new { members = filteredMembers, total = filteredMembers.Count });
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartImpersonation([FromBody] StartImpersonationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MemberEmail))
        {
            return BadRequest(new { success = false, message = "Member email is required" });
        }

        var member = _memberService.GetByEmail(request.MemberEmail);
        if (member == null)
        {
            return NotFound(new { success = false, message = "Member not found" });
        }

        var result = await _impersonationService.StartImpersonationAsync(request.MemberEmail);

        if (result)
        {
            return Ok(new
            {
                success = true,
                message = $"Impersonation started for {request.MemberEmail}",
                frontendUrl = "/dashboard"
            });
        }

        return StatusCode(500, new { success = false, message = "Failed to start impersonation" });
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var isImpersonating = _impersonationService.IsImpersonating();

        return Ok(new
        {
            isImpersonating,
            impersonatedMemberEmail = _impersonationService.GetImpersonatedMemberEmail(),
            originalAdminEmail = _impersonationService.GetOriginalAdminEmail()
        });
    }
}

public record StartImpersonationRequest(string MemberEmail);
