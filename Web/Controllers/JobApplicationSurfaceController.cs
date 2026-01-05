using Code.Services;
using Code.Services.DTOs;
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

/// <summary>
/// Surface controller for frontend job application operations
/// </summary>
public class JobApplicationSurfaceController : SurfaceController
{
    private readonly IJobService _jobService;
    private readonly IMemberManager _memberManager;

    public JobApplicationSurfaceController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IUmbracoDatabaseFactory databaseFactory,
        ServiceContext services,
        AppCaches appCaches,
        IProfilingLogger profilingLogger,
        IPublishedUrlProvider publishedUrlProvider,
        IJobService jobService,
        IMemberManager memberManager)
        : base(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
    {
        _jobService = jobService;
        _memberManager = memberManager;
    }

    /// <summary>
    /// Submit a job application
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SubmitApplication(SubmitApplicationRequest request)
    {
        // Check if user is authenticated
        if (!_memberManager.IsLoggedIn())
        {
            TempData["ErrorMessage"] = "Du skal være logget ind for at ansøge om en stilling.";
            return CurrentUmbracoPage();
        }

        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
        {
            TempData["ErrorMessage"] = "Kunne ikke finde dit medlemskab.";
            return CurrentUmbracoPage();
        }

        // Submit application
        var result = await _jobService.SubmitApplicationAsync(currentMember.Email, request);

        if (result.Success)
        {
            TempData["SuccessMessage"] = "Din ansøgning er blevet sendt! Du vil modtage en email når den er blevet behandlet.";
        }
        else
        {
            TempData["ErrorMessage"] = result.ErrorMessage ?? "Der opstod en fejl ved indsendelse af din ansøgning.";
        }

        return CurrentUmbracoPage();
    }

    /// <summary>
    /// Withdraw a job application
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> WithdrawApplication(int applicationId)
    {
        if (!_memberManager.IsLoggedIn())
        {
            TempData["ErrorMessage"] = "Du skal være logget ind.";
            return CurrentUmbracoPage();
        }

        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
        {
            TempData["ErrorMessage"] = "Kunne ikke finde dit medlemskab.";
            return CurrentUmbracoPage();
        }

        var success = await _jobService.WithdrawApplicationAsync(applicationId, currentMember.Email);

        if (success)
        {
            TempData["SuccessMessage"] = "Din ansøgning er blevet trukket tilbage.";
        }
        else
        {
            TempData["ErrorMessage"] = "Kunne ikke trække ansøgningen tilbage.";
        }

        return CurrentUmbracoPage();
    }
}
