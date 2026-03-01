using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Models.ContentPublishing;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Website.Controllers;

namespace Web.Controllers;

public class CrewSurfaceController : SurfaceController
{
    private readonly IContentService _contentService;
    private readonly IContentPublishingService _contentPublishingService;
    private readonly IMemberManager _memberManager;
    private readonly IMemberService _memberService;
    private readonly ICrewService _crewService;
    private readonly ICrewMessageService _crewMessageService;
    private readonly IMemberEmailService _memberEmailService;
    private readonly ILogger<CrewSurfaceController> _logger;
    private readonly AppCaches _appCaches;

    public CrewSurfaceController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IUmbracoDatabaseFactory databaseFactory,
        ServiceContext services,
        AppCaches appCaches,
        IProfilingLogger profilingLogger,
        IPublishedUrlProvider publishedUrlProvider,
        IContentService contentService,
        IContentPublishingService contentPublishingService,
        IMemberManager memberManager,
        IMemberService memberService,
        ICrewService crewService,
        ICrewMessageService crewMessageService,
        IMemberEmailService memberEmailService,
        ILogger<CrewSurfaceController> logger)
        : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
    {
        _contentService = contentService;
        _contentPublishingService = contentPublishingService;
        _memberManager = memberManager;
        _memberService = memberService;
        _crewService = crewService;
        _crewMessageService = crewMessageService;
        _memberEmailService = memberEmailService;
        _logger = logger;
        _appCaches = appCaches;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCrewDetails(int crewId, int? ageLimit, string? description, string returnUrl)
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
        {
            TempData["CrewError"] = "Du skal være logget ind for at redigere.";
            return Redirect(returnUrl ?? "/");
        }

        // Check if user has permission (admin or scheduler)
        var viewMode = await _crewService.GetMemberCrewViewModeAsync(currentMember.Email!, crewId);
        if (viewMode == CrewViewMode.Volunteer)
        {
            TempData["CrewError"] = "Du har ikke tilladelse til at redigere dette crew.";
            return Redirect(returnUrl ?? "/");
        }

        // Get the content
        var content = _contentService.GetById(crewId);
        if (content == null)
        {
            TempData["CrewError"] = "Crew blev ikke fundet.";
            return Redirect(returnUrl ?? "/");
        }

        // Update age limit
        content.SetValue("ageLimit", ageLimit ?? 0);

        // Update description - RTE expects HTML content
        if (!string.IsNullOrWhiteSpace(description))
        {
            // Wrap plain text in paragraph tags for proper HTML formatting
            // Replace newlines with paragraph breaks
            var htmlContent = string.Join("",
                description.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                    .Select(line => $"<p>{line}</p>"));
            content.SetValue("description", htmlContent);
        }
        else
        {
            content.SetValue("description", null);
        }

        // Save the content
        _contentService.Save(content);

        var publishResult = _contentService.Publish(content, new[] { "*" }, -1);

        if (publishResult.Success)
        {
            TempData["CrewSuccess"] = "Crew detaljer er blevet opdateret.";
        }
        else
        {
            TempData["CrewError"] = "Der opstod en fejl ved opdatering af crew.";
        }

        return Redirect(returnUrl ?? "/");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptMember(int crewId, int memberId, string returnUrl)
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
        {
            TempData["CrewError"] = "Du skal være logget ind for at acceptere medlemmer.";
            return Redirect(returnUrl ?? "/");
        }

        // Check if user has permission (admin or scheduler)
        var viewMode = await _crewService.GetMemberCrewViewModeAsync(currentMember.Email!, crewId);
        if (viewMode == CrewViewMode.Volunteer)
        {
            TempData["CrewError"] = "Du har ikke tilladelse til at acceptere medlemmer.";
            return Redirect(returnUrl ?? "/");
        }

        // Get the crew content to build the UDI
        var crewContent = _contentService.GetById(crewId);
        if (crewContent == null)
        {
            TempData["CrewError"] = "Crew blev ikke fundet.";
            return Redirect(returnUrl ?? "/");
        }

        // Get the member to update
        var member = _memberService.GetById(memberId);
        if (member == null)
        {
            TempData["CrewError"] = "Medlem blev ikke fundet.";
            return Redirect(returnUrl ?? "/");
        }

        // Build the crew UDI reference
        var crewUdi = $"umb://document/{crewContent.Key:N}";

        // Get existing crews and add this one
        var existingCrews = member.GetValue<string>("crews") ?? "";

        // Check if already assigned to this crew
        if (existingCrews.Contains(crewContent.Key.ToString("N"), StringComparison.OrdinalIgnoreCase))
        {
            TempData["CrewError"] = "Medlem er allerede tildelt dette crew.";
            return Redirect(returnUrl ?? "/");
        }

        // Set the crews value (this will be the only crew since wishlist members don't have crews assigned)
        member.SetValue("crews", crewUdi);

        // Save the member
        _memberService.Save(member);

        TempData["CrewSuccess"] = $"{member.Name} er nu tildelt dette crew.";
        return Redirect(returnUrl ?? "/");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PostMessage(int crewId, string messageText, string returnUrl, bool notifyByEmail = true)
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
        {
            TempData["CrewError"] = "Du skal være logget ind for at sende beskeder.";
            return Redirect(returnUrl ?? "/");
        }

        var viewMode = await _crewService.GetMemberCrewViewModeAsync(currentMember.Email!, crewId);
        if (viewMode == CrewViewMode.Volunteer)
        {
            TempData["CrewError"] = "Du har ikke tilladelse til at sende beskeder.";
            return Redirect(returnUrl ?? "/");
        }

        try
        {
            var memberName = currentMember.Name ?? currentMember.Email ?? "Ukendt";
            var postedMessage = await _crewMessageService.PostMessageAsync(crewId, currentMember.Email!, memberName, messageText);
            TempData["CrewSuccess"] = "Beskeden er blevet sendt.";

            if (notifyByEmail)
            {
                // Capture request values before the HTTP context is disposed
                var crewUrl = $"{Request.Scheme}://{Request.Host}{returnUrl}";
                var authorEmail = currentMember.Email!;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var crewContent = _contentService.GetById(crewId);
                        var crewName = crewContent?.Name ?? "Ukendt crew";

                        var recipients = await _crewMessageService.GetCrewMemberRecipientsAsync(crewId);

                        foreach (var recipient in recipients)
                        {
                            // Skip the author
                            if (string.Equals(recipient.Email, authorEmail, StringComparison.OrdinalIgnoreCase))
                                continue;

                            try
                            {
                                await _memberEmailService.SendCrewMessageNotificationAsync(
                                    recipient.Email,
                                    recipient.FullName,
                                    memberName,
                                    crewName,
                                    postedMessage.MessageHtml,
                                    crewUrl);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to send crew message notification to {Email}", recipient.Email);
                            }
                        }

                        _logger.LogInformation("Sent crew message notifications to {Count} recipients for crew {CrewId}", recipients.Count, crewId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send crew message notifications for crew {CrewId}", crewId);
                    }
                });
            }
        }
        catch (ArgumentException ex)
        {
            TempData["CrewError"] = ex.Message;
        }

        return Redirect(returnUrl ?? "/");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DismissMember(int memberId, int crewId, string reason, string returnUrl)
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
        {
            TempData["CrewError"] = "Du skal være logget ind.";
            return Redirect(returnUrl ?? "/");
        }

        var viewMode = await _crewService.GetMemberCrewViewModeAsync(currentMember.Email!, crewId);
        if (viewMode == CrewViewMode.Volunteer)
        {
            TempData["CrewError"] = "Du har ikke tilladelse til at afvise ansøgere.";
            return Redirect(returnUrl ?? "/");
        }

        var member = _memberService.GetById(memberId);
        if (member == null)
        {
            TempData["CrewError"] = "Medlem blev ikke fundet.";
            return Redirect(returnUrl ?? "/");
        }

        var adminRecord = _memberService.GetByEmail(currentMember.Email!);
        var adminFirstName = adminRecord?.GetValue<string>("firstName") ?? string.Empty;
        var adminLastName = adminRecord?.GetValue<string>("lastName") ?? string.Empty;
        var adminName = $"{adminFirstName} {adminLastName}".Trim();
        if (string.IsNullOrEmpty(adminName))
            adminName = currentMember.Name ?? currentMember.UserName ?? currentMember.Email ?? "Ukendt";

        member.SetValue("accept2026", false);
        member.SetValue("rejected", true);
        member.SetValue("rejectedBy", adminName);
        member.SetValue("rejectionReason", reason);
        _memberService.Save(member);

        if (!string.IsNullOrWhiteSpace(reason) && !string.IsNullOrEmpty(member.Email))
        {
            var crewContent = _contentService.GetById(crewId);
            var crewName = crewContent?.Name ?? "crewet";
            var firstName = member.GetValue<string>("firstName") ?? member.Name ?? "Frivillig";
            var htmlBody = $@"<p>Kære {System.Net.WebUtility.HtmlEncode(firstName)},</p>
<p>Vi har desværre ikke mulighed for at tildele dig en vagt i {System.Net.WebUtility.HtmlEncode(crewName)}.</p>
<p><strong>Begrundelse:</strong></p>
<p>{System.Net.WebUtility.HtmlEncode(reason)}</p>
<p>Venlig hilsen,<br>Blue Bridge Festival</p>";

            try
            {
                await _memberEmailService.SendCustomEmailAsync(
                    member.Email,
                    $"Svar på din ansøgning til {crewName}",
                    htmlBody,
                    new Code.Services.MemberEmailData { FirstName = firstName, Email = member.Email });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send dismissal email to {Email}", member.Email);
            }
        }

        TempData["CrewSuccess"] = $"{member.Name} er blevet afvist og har modtaget en besked.";
        return Redirect(returnUrl ?? "/");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMessage(int messageId, int crewId, string returnUrl)
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
        {
            TempData["CrewError"] = "Du skal være logget ind.";
            return Redirect(returnUrl ?? "/");
        }

        var viewMode = await _crewService.GetMemberCrewViewModeAsync(currentMember.Email!, crewId);
        var isAdminOrScheduler = viewMode == CrewViewMode.Admin || viewMode == CrewViewMode.Scheduler;

        var deleted = await _crewMessageService.DeleteMessageAsync(messageId, currentMember.Email!, isAdminOrScheduler);
        if (deleted)
        {
            TempData["CrewSuccess"] = "Beskeden er blevet slettet.";
        }
        else
        {
            TempData["CrewError"] = "Kunne ikke slette beskeden.";
        }

        return Redirect(returnUrl ?? "/");
    }
}
