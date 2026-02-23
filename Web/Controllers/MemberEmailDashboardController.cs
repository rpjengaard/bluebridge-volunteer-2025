using Asp.Versioning;
using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace Web.Controllers;

[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("memberemaildashboard")]
[ApiExplorerSettings(GroupName = "Member Email Dashboard API")]
public class MemberEmailDashboardController : ManagementApiControllerBase
{
    private readonly IMemberService _memberService;
    private readonly IContentService _contentService;
    private readonly IMemberEmailService _emailService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MemberEmailDashboardController> _logger;

    public MemberEmailDashboardController(
        IMemberService memberService,
        IContentService contentService,
        IMemberEmailService emailService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MemberEmailDashboardController> logger)
    {
        _memberService = memberService;
        _contentService = contentService;
        _emailService = emailService;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    [HttpGet("members")]
    public IActionResult GetMembers()
    {
        try
        {
            var members = _memberService.GetAllMembers().ToList();
            var memberDtos = members.Select(m =>
            {
                var hasToken = !string.IsNullOrEmpty(m.GetValue<string>("invitationToken"));
                var hasAccepted = m.GetValue<bool>("accept2026");

                string status;
                if (hasAccepted)
                    status = "Accepted";
                else if (hasToken)
                    status = "Invited";
                else
                    status = "NotInvited";

                var firstName = m.GetValue<string>("firstName") ?? string.Empty;
                var lastName = m.GetValue<string>("lastName") ?? string.Empty;
                var fullName = $"{firstName} {lastName}".Trim();
                if (string.IsNullOrEmpty(fullName))
                    fullName = m.Name ?? m.Email ?? "Unknown";

                var crewIds = GetMemberCrewIds(m);
                var crewNames = ResolveCrewNames(crewIds);

                return new
                {
                    memberId = m.Id,
                    memberKey = m.Key,
                    email = m.Email ?? string.Empty,
                    fullName,
                    firstName,
                    lastName,
                    status,
                    crewIds,
                    crewNames,
                    invitationSentDate = m.GetValue<DateTime?>("invitationSentDate"),
                    acceptedDate = m.GetValue<DateTime?>("acceptedDate")
                };
            }).OrderBy(m => m.fullName).ToList();

            return Ok(new { success = true, members = memberDtos });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get members for email dashboard");
            return StatusCode(500, new { success = false, message = $"Failed to get members: {ex.Message}" });
        }
    }

    [HttpGet("crews")]
    public IActionResult GetCrews()
    {
        try
        {
            var crews = new List<object>();
            var rootContent = _contentService.GetRootContent();
            foreach (var root in rootContent)
            {
                FindCrewsRecursive(root, crews);
            }

            var ordered = crews
                .Select(c => (dynamic)c)
                .OrderBy(c => (string)c.name)
                .Select(c => new { id = (int)c.id, name = (string)c.name })
                .ToList();

            return Ok(new { success = true, crews = ordered });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get crews for email dashboard");
            return StatusCode(500, new { success = false, message = $"Failed to get crews: {ex.Message}" });
        }
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendEmails([FromBody] SendEmailRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { success = false, message = "Subject and body are required" });
        }

        if (request.MemberIds == null || request.MemberIds.Count == 0)
        {
            return BadRequest(new { success = false, message = "No members selected" });
        }

        var sentCount = 0;
        var errorCount = 0;
        var errors = new List<string>();

        foreach (var memberId in request.MemberIds)
        {
            var member = _memberService.GetById(memberId);
            if (member == null || string.IsNullOrEmpty(member.Email))
            {
                errors.Add($"Member ID {memberId}: not found or has no email");
                errorCount++;
                continue;
            }

            try
            {
                var firstName = member.GetValue<string>("firstName") ?? member.Name?.Split(' ').FirstOrDefault() ?? "Frivillig";
                var lastName = member.GetValue<string>("lastName") ?? string.Empty;
                var crewIds = GetMemberCrewIds(member);
                var crewNames = ResolveCrewNames(crewIds);

                var memberData = new MemberEmailData
                {
                    Email = member.Email,
                    Username = member.Username ?? member.Email,
                    FirstName = firstName,
                    LastName = lastName,
                    Phone = member.GetValue<string>("phone") ?? string.Empty,
                    Zipcode = member.GetValue<string>("zipcode") ?? string.Empty,
                    TidligereArbejdssteder = member.GetValue<string>("tidligereArbejdssteder") ?? string.Empty,
                    SelectedCrews = string.Join(", ", crewNames),
                    MemberWish = member.GetValue<string>("memberWish") ?? string.Empty,
                    PortalUrl = GetBaseUrl()
                };

                await _emailService.SendCustomEmailAsync(member.Email, request.Subject, request.Body, memberData);
                sentCount++;
                _logger.LogInformation("Email dashboard: sent email to {Email}", member.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email dashboard: failed to send to member {MemberId} ({Email})", memberId, member.Email);
                errors.Add($"{member.Email}: {ex.Message}");
                errorCount++;
            }
        }

        var message = $"Sendt {sentCount} emails, {errorCount} fejl";
        _logger.LogInformation("Email dashboard bulk send complete: {Message}", message);

        return Ok(new
        {
            success = errorCount == 0,
            message,
            sentCount,
            errorCount,
            errors
        });
    }

    private List<int> GetMemberCrewIds(IMember member)
    {
        var ids = new List<int>();

        // Assigned crews (set by admin)
        var crewsValue = member.GetValue<string>("crews");
        if (!string.IsNullOrWhiteSpace(crewsValue))
        {
            ResolveUdiToIds(crewsValue, ids);
        }

        // Crew wishes (set when accepting invitation)
        var wishesValue = member.GetValue<string>("crewWishes");
        if (!string.IsNullOrWhiteSpace(wishesValue))
        {
            ResolveUdiToIds(wishesValue, ids);
        }

        return ids.Distinct().ToList();
    }

    private void ResolveUdiToIds(string udiString, List<int> ids)
    {
        var udiParts = udiString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var udiPart in udiParts)
        {
            var trimmed = udiPart.Trim();
            if (trimmed.StartsWith("umb://document/", StringComparison.OrdinalIgnoreCase))
            {
                var guidPart = trimmed["umb://document/".Length..];
                if (Guid.TryParse(guidPart, out var contentGuid))
                {
                    var content = _contentService.GetById(contentGuid);
                    if (content != null)
                    {
                        ids.Add(content.Id);
                    }
                }
            }
        }
    }

    private List<string> ResolveCrewNames(List<int> crewIds)
    {
        var names = new List<string>();

        if (crewIds == null || crewIds.Count == 0)
        {
            return names;
        }

        var contents = _contentService.GetByIds(crewIds);
        foreach (var content in contents)
        {
            if (content != null && !string.IsNullOrEmpty(content.Name))
            {
                names.Add(content.Name);
            }
        }
        return names;
    }

    private void FindCrewsRecursive(IContent content, List<object> crews)
    {
        if (content.ContentType.Alias == "bbvCrewPage")
        {
            crews.Add(new { id = content.Id, name = content.Name ?? $"Crew {content.Id}" });
        }

        var children = _contentService.GetPagedChildren(content.Id, 0, int.MaxValue, out _);
        foreach (var child in children)
        {
            FindCrewsRecursive(child, crews);
        }
    }

    private string GetBaseUrl()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null) return string.Empty;
        return $"{request.Scheme}://{request.Host}";
    }
}

public class SendEmailRequest
{
    public List<int> MemberIds { get; set; } = new();
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
