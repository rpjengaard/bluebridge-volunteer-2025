// [CHANGE: crew email export button on crew list] Related: Code/Services/ICrewService.cs, Code/Services/CrewService.cs, Web/Views/CrewListe.cshtml
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

public class CrewEmailSurfaceController : SurfaceController
{
    private readonly IMemberManager _memberManager;
    private readonly ICrewService _crewService;

    public CrewEmailSurfaceController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IUmbracoDatabaseFactory databaseFactory,
        ServiceContext services,
        AppCaches appCaches,
        IProfilingLogger profilingLogger,
        IPublishedUrlProvider publishedUrlProvider,
        IMemberManager memberManager,
        ICrewService crewService)
        : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
    {
        _memberManager = memberManager;
        _crewService = crewService;
    }

    [HttpGet]
    public async Task<IActionResult> GetEmails(int crewId)
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember?.Email == null)
        {
            return Unauthorized();
        }

        var data = await _crewService.GetCrewMemberEmailsAsync(crewId, currentMember.Email);
        if (data == null)
        {
            return StatusCode(403);
        }

        return Json(new { crewName = data.CrewName, emails = data.Emails });
    }
}
