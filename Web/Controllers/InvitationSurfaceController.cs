using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Website.Controllers;
using Web.ViewModels;

namespace Web.Controllers;

public class InvitationSurfaceController : SurfaceController
{
    private readonly IInvitationService _invitationService;

    public InvitationSurfaceController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IUmbracoDatabaseFactory databaseFactory,
        ServiceContext services,
        AppCaches appCaches,
        IProfilingLogger profilingLogger,
        IPublishedUrlProvider publishedUrlProvider,
        IInvitationService invitationService)
        : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
    {
        _invitationService = invitationService;
    }

    [HttpGet]
    public async Task<IActionResult> AcceptInvitation(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            TempData["InvitationError"] = "Ugyldigt invitationslink.";
            return View("~/Views/AcceptInvitation.cshtml", new AcceptInvitationViewModel());
        }

        var memberInfo = await _invitationService.GetMemberByTokenAsync(token);
        if (memberInfo == null)
        {
            TempData["InvitationError"] = "Ugyldigt eller brugt invitationslink.";
            return View("~/Views/AcceptInvitation.cshtml", new AcceptInvitationViewModel());
        }

        var crews = await _invitationService.GetAvailableCrewsAsync();
        var model = new AcceptInvitationViewModel
        {
            Token = token,
            MemberName = memberInfo.FullName,
            FirstName = memberInfo.FirstName,
            Email = memberInfo.Email,
            CurrentBirthdate = memberInfo.Birthdate,
            Birthdate = memberInfo.Birthdate,
            AvailableCrews = crews.Select(c => new CrewSelectionItem
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                AgeLimit = c.AgeLimit
            }).OrderBy(c => c.Name).ToList()
        };

        return View("~/Views/AcceptInvitation.cshtml", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HandleAcceptInvitation(AcceptInvitationViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Token))
        {
            TempData["InvitationError"] = "Ugyldigt invitationslink.";
            return RedirectToAction("AcceptInvitation", new { token = model.Token });
        }

        if (model.SelectedCrewIds == null || !model.SelectedCrewIds.Any())
        {
            TempData["InvitationError"] = "Vælg mindst ét crew-ønske.";
            return RedirectToAction("AcceptInvitation", new { token = model.Token });
        }

        if (!model.Birthdate.HasValue)
        {
            TempData["InvitationError"] = "Fødselsdato er påkrævet.";
            return RedirectToAction("AcceptInvitation", new { token = model.Token });
        }

        if (string.IsNullOrWhiteSpace(model.Password))
        {
            TempData["InvitationError"] = "Adgangskode er påkrævet.";
            return RedirectToAction("AcceptInvitation", new { token = model.Token });
        }

        if (model.Password != model.ConfirmPassword)
        {
            TempData["InvitationError"] = "Adgangskoderne matcher ikke.";
            return RedirectToAction("AcceptInvitation", new { token = model.Token });
        }

        if (model.Password.Length < 10)
        {
            TempData["InvitationError"] = "Adgangskoden skal være mindst 10 tegn.";
            return RedirectToAction("AcceptInvitation", new { token = model.Token });
        }

        var portalUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await _invitationService.AcceptInvitationAsync(
            model.Token,
            model.SelectedCrewIds,
            model.Birthdate.Value,
            model.Password,
            portalUrl);

        if (!result.Success)
        {
            TempData["InvitationError"] = result.Message;
            return RedirectToAction("AcceptInvitation", new { token = model.Token });
        }

        TempData["MemberName"] = result.MemberName;
        TempData["SelectedCrews"] = result.SelectedCrewNames != null
            ? string.Join(", ", result.SelectedCrewNames)
            : string.Empty;

        return RedirectToAction("InvitationConfirmation");
    }

    [HttpGet]
    public IActionResult InvitationConfirmation()
    {
        return View("~/Views/InvitationConfirmation.cshtml");
    }
}
