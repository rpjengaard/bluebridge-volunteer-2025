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
    private readonly IEmailLogService _emailLogService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MemberEmailDashboardController> _logger;

    public MemberEmailDashboardController(
        IMemberService memberService,
        IContentService contentService,
        IMemberEmailService emailService,
        IEmailLogService emailLogService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MemberEmailDashboardController> logger)
    {
        _memberService = memberService;
        _contentService = contentService;
        _emailService = emailService;
        _emailLogService = emailLogService;
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
                var hasInvitationUrl = hasToken && !hasAccepted;

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
                    hasInvitationUrl,
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
            var crews = new List<(int Id, string Name)>();
            var rootContent = _contentService.GetRootContent();
            foreach (var root in rootContent)
            {
                FindCrewsRecursive(root, crews);
            }

            var result = crews.OrderBy(c => c.Name).Select(c => new { id = c.Id, name = c.Name }).ToList();
            return Ok(new { success = true, crews = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get crews for email dashboard");
            return StatusCode(500, new { success = false, message = $"Failed to get crews: {ex.Message}" });
        }
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs()
    {
        try
        {
            var logs = await _emailLogService.GetLogsAsync(50);
            return Ok(new { success = true, logs });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get email logs");
            return StatusCode(500, new { success = false, message = $"Failed to get logs: {ex.Message}" });
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

        var baseUrl = GetBaseUrl();
        var sentCount = 0;
        var errorCount = 0;
        var recipients = new List<EmailLogRecipient>();

        foreach (var memberId in request.MemberIds)
        {
            var member = _memberService.GetById(memberId);
            if (member == null || string.IsNullOrEmpty(member.Email))
            {
                recipients.Add(new EmailLogRecipient
                {
                    Email = $"ID:{memberId}",
                    FullName = "Unknown",
                    Success = false,
                    ErrorMessage = "Member not found or has no email"
                });
                errorCount++;
                continue;
            }

            var firstName = member.GetValue<string>("firstName") ?? member.Name?.Split(' ').FirstOrDefault() ?? "Frivillig";
            var lastName = member.GetValue<string>("lastName") ?? string.Empty;
            var fullName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrEmpty(fullName)) fullName = member.Name ?? member.Email;

            try
            {
                var crewIds = GetMemberCrewIds(member);
                var crewNames = ResolveCrewNames(crewIds);

                // Build invitation URL if the member has an active token
                var invitationToken = member.GetValue<string>("invitationToken");
                var hasAccepted = member.GetValue<bool>("accept2026");
                var invitationUrl = (!string.IsNullOrEmpty(invitationToken) && !hasAccepted)
                    ? $"{baseUrl.TrimEnd('/')}/umbraco/surface/InvitationSurface/AcceptInvitation?token={invitationToken}"
                    : string.Empty;

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
                    PortalUrl = baseUrl,
                    InvitationUrl = invitationUrl
                };

                await _emailService.SendCustomEmailAsync(member.Email, request.Subject, request.Body, memberData);

                recipients.Add(new EmailLogRecipient
                {
                    Email = member.Email,
                    FullName = fullName,
                    Success = true
                });
                sentCount++;
                _logger.LogInformation("Email dashboard: sent to {Email}", member.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email dashboard: failed to send to {Email}", member.Email);
                recipients.Add(new EmailLogRecipient
                {
                    Email = member.Email,
                    FullName = fullName,
                    Success = false,
                    ErrorMessage = ex.Message
                });
                errorCount++;
            }
        }

        // Persist the log entry
        var logEntry = new EmailLogEntry
        {
            SentAt = DateTime.Now,
            Subject = request.Subject,
            Body = request.Body,
            SentCount = sentCount,
            ErrorCount = errorCount,
            Recipients = recipients
        };
        await _emailLogService.AddLogAsync(logEntry);

        var message = $"Sendt {sentCount} emails, {errorCount} fejl";
        _logger.LogInformation("Email dashboard bulk send complete: {Message}", message);

        return Ok(new
        {
            success = errorCount == 0,
            message,
            sentCount,
            errorCount,
            errors = recipients.Where(r => !r.Success).Select(r => $"{r.Email}: {r.ErrorMessage}").ToList()
        });
    }

    private List<int> GetMemberCrewIds(IMember member)
    {
        var ids = new List<int>();

        var crewsValue = member.GetValue<string>("crews");
        if (!string.IsNullOrWhiteSpace(crewsValue))
            ResolveUdiToIds(crewsValue, ids);

        var wishesValue = member.GetValue<string>("crewWishes");
        if (!string.IsNullOrWhiteSpace(wishesValue))
            ResolveUdiToIds(wishesValue, ids);

        return ids.Distinct().ToList();
    }

    private void ResolveUdiToIds(string udiString, List<int> ids)
    {
        foreach (var udiPart in udiString.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = udiPart.Trim();
            if (trimmed.StartsWith("umb://document/", StringComparison.OrdinalIgnoreCase))
            {
                var guidPart = trimmed["umb://document/".Length..];
                if (Guid.TryParse(guidPart, out var contentGuid))
                {
                    var content = _contentService.GetById(contentGuid);
                    if (content != null)
                        ids.Add(content.Id);
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
                names.Add(content.Name);
        }
        return names;
    }

    private void FindCrewsRecursive(IContent content, List<(int, string)> crews)
    {
        if (content.ContentType.Alias == "bbvCrewPage")
            crews.Add((content.Id, content.Name ?? $"Crew {content.Id}"));

        var children = _contentService.GetPagedChildren(content.Id, 0, int.MaxValue, out _);
        foreach (var child in children)
            FindCrewsRecursive(child, crews);
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
