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
using Web.ViewModels;

namespace Web.Controllers;

// [CHANGE: crew invitation feature - shiftadmins invite new volunteers while signup is closed]
// Related: Code/Services/CrewInvitationService.cs, Web/Views/AcceptCrewInvitation.cshtml, Web/Views/Crew.cshtml

public class CrewInvitationSurfaceController : SurfaceController
{
    private readonly ICrewInvitationService _crewInvitationService;
    private readonly ICrewService _crewService;
    private readonly IMemberManager _memberManager;

    public CrewInvitationSurfaceController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IUmbracoDatabaseFactory databaseFactory,
        ServiceContext services,
        AppCaches appCaches,
        IProfilingLogger profilingLogger,
        IPublishedUrlProvider publishedUrlProvider,
        ICrewInvitationService crewInvitationService,
        ICrewService crewService,
        IMemberManager memberManager)
        : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
    {
        _crewInvitationService = crewInvitationService;
        _crewService = crewService;
        _memberManager = memberManager;
    }

    private async Task<bool> CanManageCrewAsync(int crewId)
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
            return false;

        var viewMode = await _crewService.GetMemberCrewViewModeAsync(currentMember.Email!, crewId);
        return viewMode != CrewViewMode.Volunteer;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendInvitation(int crewId, string email, string firstName, string lastName, string returnUrl)
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null || !await CanManageCrewAsync(crewId))
        {
            TempData["CrewError"] = "Du har ikke tilladelse til at invitere til dette crew.";
            return Redirect(returnUrl ?? "/");
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(firstName))
        {
            TempData["CrewError"] = "Email og fornavn er påkrævet.";
            return Redirect(returnUrl ?? "/");
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await _crewInvitationService.SendInvitationAsync(
            crewId, email.Trim(), firstName.Trim(), lastName?.Trim() ?? string.Empty, currentMember.Email!, baseUrl);

        if (result.Success)
            TempData["CrewSuccess"] = result.Message;
        else
            TempData["CrewError"] = result.Message;

        return Redirect(returnUrl ?? "/");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendInvitation(int invitationId, int crewId, string returnUrl)
    {
        if (!await CanManageCrewAsync(crewId))
        {
            TempData["CrewError"] = "Du har ikke tilladelse til at gensende invitationer.";
            return Redirect(returnUrl ?? "/");
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await _crewInvitationService.ResendInvitationAsync(invitationId, baseUrl);

        if (result.Success)
            TempData["CrewSuccess"] = result.Message;
        else
            TempData["CrewError"] = result.Message;

        return Redirect(returnUrl ?? "/");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelInvitation(int invitationId, int crewId, string returnUrl)
    {
        if (!await CanManageCrewAsync(crewId))
        {
            TempData["CrewError"] = "Du har ikke tilladelse til at annullere invitationer.";
            return Redirect(returnUrl ?? "/");
        }

        var canceled = await _crewInvitationService.CancelInvitationAsync(invitationId);
        if (canceled)
            TempData["CrewSuccess"] = "Invitationen er annulleret.";
        else
            TempData["CrewError"] = "Invitationen kunne ikke annulleres.";

        return Redirect(returnUrl ?? "/");
    }

    [HttpGet]
    public async Task<IActionResult> Accept(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            TempData["CrewInvitationError"] = "Ugyldigt invitationslink.";
            return View("~/Views/AcceptCrewInvitation.cshtml", new AcceptCrewInvitationViewModel());
        }

        var info = await _crewInvitationService.GetByTokenAsync(token);
        if (info == null)
        {
            TempData["CrewInvitationError"] = "Ugyldigt eller udløbet invitationslink. Kontakt den, der inviterede dig, for at få et nyt.";
            return View("~/Views/AcceptCrewInvitation.cshtml", new AcceptCrewInvitationViewModel());
        }

        var model = new AcceptCrewInvitationViewModel
        {
            Token = token,
            Email = info.Email,
            CrewName = info.CrewName,
            CrewAgeLimit = info.CrewAgeLimit,
            FirstName = info.FirstName,
            LastName = info.LastName
        };

        return View("~/Views/AcceptCrewInvitation.cshtml", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HandleAccept(AcceptCrewInvitationViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Token))
        {
            TempData["CrewInvitationError"] = "Ugyldigt invitationslink.";
            return RedirectToAction("Accept", new { token = model.Token });
        }

        if (string.IsNullOrWhiteSpace(model.FirstName))
        {
            TempData["CrewInvitationError"] = "Fornavn er påkrævet.";
            return RedirectToAction("Accept", new { token = model.Token });
        }

        if (!model.Birthdate.HasValue)
        {
            TempData["CrewInvitationError"] = "Fødselsdato er påkrævet.";
            return RedirectToAction("Accept", new { token = model.Token });
        }

        if (string.IsNullOrWhiteSpace(model.Password))
        {
            TempData["CrewInvitationError"] = "Adgangskode er påkrævet.";
            return RedirectToAction("Accept", new { token = model.Token });
        }

        if (model.Password != model.ConfirmPassword)
        {
            TempData["CrewInvitationError"] = "Adgangskoderne matcher ikke.";
            return RedirectToAction("Accept", new { token = model.Token });
        }

        if (model.Password.Length < 10)
        {
            TempData["CrewInvitationError"] = "Adgangskoden skal være mindst 10 tegn.";
            return RedirectToAction("Accept", new { token = model.Token });
        }

        var portalUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await _crewInvitationService.AcceptInvitationAsync(new CrewInvitationAcceptRequest
        {
            Token = model.Token,
            FirstName = model.FirstName.Trim(),
            LastName = model.LastName?.Trim() ?? string.Empty,
            Birthdate = model.Birthdate.Value,
            Phone = model.Phone?.Trim(),
            Zipcode = model.Zipcode?.Trim(),
            Password = model.Password,
            PortalUrl = portalUrl
        });

        if (!result.Success)
        {
            TempData["CrewInvitationError"] = result.Message;
            return RedirectToAction("Accept", new { token = model.Token });
        }

        TempData["MemberName"] = result.MemberName;
        TempData["CrewName"] = result.CrewName;
        return RedirectToAction("Confirmation");
    }

    [HttpGet]
    public IActionResult Confirmation()
    {
        return View("~/Views/CrewInvitationConfirmation.cshtml");
    }
}
