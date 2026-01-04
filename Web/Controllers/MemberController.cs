using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Security;
using Code.Services;

namespace Web.Controllers;

public class MemberController : Controller
{
    private readonly IMemberManager _memberManager;
    private readonly ICrewService _crewService;

    public MemberController(IMemberManager memberManager, ICrewService crewService)
    {
        _memberManager = memberManager;
        _crewService = crewService;
    }

    [HttpGet("/member")]
    public IActionResult Index()
    {
        return View("~/Views/Member.cshtml");
    }

    [HttpGet("/api/member/{memberKey}")]
    public async Task<IActionResult> GetMemberData(Guid memberKey)
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
        {
            return Unauthorized(new { error = "Not logged in" });
        }

        var memberData = await _crewService.GetMemberByKeyAsync(memberKey, currentMember.Email!);
        if (memberData == null)
        {
            return NotFound(new { error = "Member not found or access denied" });
        }

        // Calculate age if birthdate exists
        int? age = null;
        if (memberData.Birthdate.HasValue)
        {
            var today = DateTime.Today;
            age = today.Year - memberData.Birthdate.Value.Year;
            if (memberData.Birthdate.Value.Date > today.AddYears(-age.Value)) age--;
        }

        return Ok(new
        {
            memberData.MemberId,
            memberData.MemberKey,
            memberData.FirstName,
            memberData.LastName,
            memberData.FullName,
            memberData.Email,
            memberData.Phone,
            Birthdate = memberData.Birthdate?.ToString("dd.MM.yyyy"),
            Age = age,
            memberData.TidligereArbejdssteder,
            memberData.Accept2026,
            AcceptedDate = memberData.AcceptedDate?.ToString("dd/MM/yyyy"),
            InvitationSentDate = memberData.InvitationSentDate?.ToString("dd/MM/yyyy"),
            memberData.MemberGroups,
            AssignedCrews = memberData.AssignedCrews.Select(c => new
            {
                c.Id,
                c.Key,
                c.Name,
                c.Description,
                c.AgeLimit,
                c.Url
            }),
            CrewWishes = memberData.CrewWishes.Select(c => new
            {
                c.Id,
                c.Key,
                c.Name,
                c.Description,
                c.AgeLimit,
                c.Url
            })
        });
    }
}
