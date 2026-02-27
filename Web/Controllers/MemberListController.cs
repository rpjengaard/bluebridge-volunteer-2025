using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Security;
using Code.Services;
using Web.ViewModels;

namespace Web.Controllers;

public class MemberListController : Controller
{
    private readonly IMemberManager _memberManager;
    private readonly IMemberListService _memberListService;

    public MemberListController(IMemberManager memberManager, IMemberListService memberListService)
    {
        _memberManager = memberManager;
        _memberListService = memberListService;
    }

    [HttpGet("/members")]
    public async Task<IActionResult> Index()
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
            return Redirect("/login?returnUrl=/members");

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
