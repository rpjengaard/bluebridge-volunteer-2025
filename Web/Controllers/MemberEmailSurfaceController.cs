using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Website.Controllers;

namespace Web.Controllers;

public class MemberEmailSurfaceController : SurfaceController
{
    private static readonly Guid AdminGroupKey = Guid.Parse("99e1edbb-8181-421d-a74b-e66a2f1e1148");

    private readonly IMemberManager _memberManager;
    private readonly IMemberService _memberService;
    private readonly IMemberGroupService _memberGroupService;
    private readonly IMemberEmailService _emailService;
    private readonly IContentService _contentService;

    public MemberEmailSurfaceController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IUmbracoDatabaseFactory databaseFactory,
        ServiceContext services,
        AppCaches appCaches,
        IProfilingLogger profilingLogger,
        IPublishedUrlProvider publishedUrlProvider,
        IMemberManager memberManager,
        IMemberService memberService,
        IMemberGroupService memberGroupService,
        IMemberEmailService emailService,
        IContentService contentService)
        : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
    {
        _memberManager = memberManager;
        _memberService = memberService;
        _memberGroupService = memberGroupService;
        _emailService = emailService;
        _contentService = contentService;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendBulk([FromBody] SendBulkEmailRequest request)
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
            return Unauthorized();

        var adminGroup = _memberGroupService.GetById(AdminGroupKey);
        if (adminGroup == null)
            return StatusCode(403, new { error = "Admin group not found" });

        var memberGroups = _memberService.GetAllRoles(_memberService.GetByEmail(currentMember.Email!)?.Id ?? 0);
        if (!memberGroups.Contains(adminGroup.Name))
            return StatusCode(403, new { error = "Kun administratorer kan sende bulk-emails" });

        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(new { error = "Emne og besked må ikke være tomme" });

        var portalUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
        var crewNameCache = new Dictionary<Guid, string>();

        int sentCount = 0;
        int errorCount = 0;
        var errors = new List<string>();

        foreach (var email in request.Emails.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct())
        {
            var member = _memberService.GetByEmail(email);
            if (member == null)
            {
                errorCount++;
                errors.Add($"{email}: Medlem ikke fundet");
                continue;
            }

            var crewsValue = member.GetValue<string>("crews");
            var crewNames = ResolveCrewNames(crewsValue, crewNameCache);

            var memberData = new MemberEmailData
            {
                Email = member.Email ?? email,
                Username = member.Username ?? string.Empty,
                FirstName = member.GetValue<string>("firstName") ?? string.Empty,
                LastName = member.GetValue<string>("lastName") ?? string.Empty,
                Phone = member.GetValue<string>("phone") ?? string.Empty,
                Zipcode = member.GetValue<string>("zipcode") ?? string.Empty,
                TidligereArbejdssteder = member.GetValue<string>("tidligereArbejdssteder") ?? string.Empty,
                SelectedCrews = string.Join(", ", crewNames),
                CurrentCrews = string.Join(", ", crewNames),
                PortalUrl = portalUrl
            };

            try
            {
                await _emailService.SendCustomEmailAsync(email, request.Subject, request.Body, memberData);
                sentCount++;
            }
            catch (Exception ex)
            {
                errorCount++;
                errors.Add($"{email}: {ex.Message}");
            }
        }

        return Json(new { success = errorCount == 0, sentCount, errorCount, errors });
    }

    private List<string> ResolveCrewNames(string? udiString, Dictionary<Guid, string> cache)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(udiString))
            return names;

        foreach (var udiPart in udiString.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = udiPart.Trim();
            if (!trimmed.StartsWith("umb://document/", StringComparison.OrdinalIgnoreCase))
                continue;

            var guidPart = trimmed["umb://document/".Length..];
            if (!Guid.TryParse(guidPart, out var contentGuid))
                continue;

            if (cache.TryGetValue(contentGuid, out var cachedName))
            {
                names.Add(cachedName);
            }
            else
            {
                var content = _contentService.GetById(contentGuid);
                if (content != null)
                {
                    var name = content.Name ?? $"Crew {content.Id}";
                    cache[contentGuid] = name;
                    names.Add(name);
                }
            }
        }

        return names;
    }
}

public class SendBulkEmailRequest
{
    public List<string> Emails { get; set; } = new();
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
