using Code.Services;
using Microsoft.AspNetCore.Mvc;
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
        ICrewService crewService)
        : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
    {
        _contentService = contentService;
        _contentPublishingService = contentPublishingService;
        _memberManager = memberManager;
        _memberService = memberService;
        _crewService = crewService;
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
}
