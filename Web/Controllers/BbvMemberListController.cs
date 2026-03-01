using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Code.Services;
using Web.ViewModels;

namespace Web.Controllers;

public class BbvMemberListController : RenderController
{
    private readonly IMemberManager _memberManager;
    private readonly IMemberListService _memberListService;

    public BbvMemberListController(
        ILogger<BbvMemberListController> logger,
        ICompositeViewEngine compositeViewEngine,
        IUmbracoContextAccessor umbracoContextAccessor,
        IMemberManager memberManager,
        IMemberListService memberListService)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
        _memberManager = memberManager;
        _memberListService = memberListService;
    }

    public override IActionResult Index()
    {
        return IndexAsync().GetAwaiter().GetResult();
    }

    private async Task<IActionResult> IndexAsync()
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
            return Redirect($"/login?returnUrl={HttpContext.Request.Path}");

        var data = await _memberListService.GetAllMembersAsync(currentMember.Email!);
        if (data == null)
            return Redirect("/dashboard");

        var viewModel = new MemberListViewModel
        {
            Members = data.Members.Select(m => new MemberListItemViewModel
            {
                MemberKey = m.MemberKey,
                FullName = m.FullName,
                Email = m.Email,
                SignupDate = m.SignupDate,
                Crews = m.CrewNames,
                Groups = m.MemberGroups
            }).ToList(),
            AllCrews = data.AllCrewNames,
            AllGroups = data.AllGroupNames
        };

        return View("~/Views/MemberList.cshtml", viewModel);
    }
}
