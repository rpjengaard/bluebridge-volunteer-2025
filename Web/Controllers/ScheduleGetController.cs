using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Security;

namespace Web.Controllers;

/// <summary>
/// All schedule endpoints (GET + POST) in a single plain ASP.NET Core controller
/// with explicit [Route] attributes. This avoids Umbraco Surface Controller
/// routing quirks where the conventional route uses the class name minus
/// "Controller" (e.g. ScheduleSurfaceController → /umbraco/surface/ScheduleSurface/...),
/// which did NOT match the /umbraco/surface/schedule/... URLs the JavaScript calls.
/// </summary>
[Route("umbraco/surface/schedule")]
public class ScheduleController : Controller
{
    private readonly IMemberManager _memberManager;
    private readonly ICrewService _crewService;
    private readonly IScheduleService _scheduleService;

    public ScheduleController(
        IMemberManager memberManager,
        ICrewService crewService,
        IScheduleService scheduleService)
    {
        _memberManager = memberManager;
        _crewService = crewService;
        _scheduleService = scheduleService;
    }

    // ── GET endpoints ─────────────────────────────────────────────────────────

    // GET /umbraco/surface/schedule/forCrew?crewId=X
    [HttpGet("forCrew")]
    public async Task<IActionResult> ForCrew(int crewId)
    {
        var (authorized, errorResult) = await AuthorizeCrewEditorAsync(crewId);
        if (!authorized) return errorResult!;

        var schedules = await _scheduleService.GetSchedulesForCrewAsync(crewId);
        return Json(schedules.Select(s => MapScheduleDto(s)));
    }

    // GET /umbraco/surface/schedule/crewMembers?crewId=X&sort=accepted|name|age
    [HttpGet("crewMembers")]
    public async Task<IActionResult> CrewMembers(int crewId, string sort = "accepted")
    {
        var (authorized, errorResult) = await AuthorizeCrewEditorAsync(crewId);
        if (!authorized) return errorResult!;

        var currentMember = await _memberManager.GetCurrentMemberAsync();
        var crewDetail = await _crewService.GetCrewDetailAsync(crewId, currentMember!.Email!, CrewViewMode.Admin);
        if (crewDetail == null) return Json(Array.Empty<object>());

        var members = crewDetail.Members.Select(m => new
        {
            key = m.MemberKey,
            name = m.FullName,
            email = m.Email,
            signupDate = m.SignupDate,          // member.CreateDate – always set
            acceptedDate = m.AcceptedDate,
            birthdate = m.Birthdate,
            age = m.Birthdate.HasValue
                ? (int)((DateTime.Today - m.Birthdate.Value).TotalDays / 365.25)
                : (int?)null
        }).ToList();

        var sorted = sort switch
        {
            "name" => members.OrderBy(m => m.name).ToList(),
            "age"  => members.OrderBy(m => m.birthdate ?? DateTime.MaxValue).ToList(),
            // Default: oldest signup (CreateDate) first → ascending
            _ => members.OrderBy(m => m.signupDate).ToList()
        };

        return Json(sorted);
    }

    // ── POST endpoints ────────────────────────────────────────────────────────

    // POST /umbraco/surface/schedule/create
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateScheduleRequest req)
    {
        var (authorized, errorResult) = await AuthorizeCrewEditorAsync(req.CrewId);
        if (!authorized) return errorResult!;

        if (string.IsNullOrWhiteSpace(req.Name))
            return Json(new { success = false, error = "Navn er påkrævet." });

        if (!DateTime.TryParse(req.Date, out var date))
            return Json(new { success = false, error = "Ugyldig dato." });

        if (!Guid.TryParse(req.CrewKey, out var crewKey))
            return Json(new { success = false, error = "Ugyldig crew-nøgle." });

        var scheduleId = await _scheduleService.CreateScheduleAsync(req.CrewId, crewKey, req.Name, date);
        var schedule = await _scheduleService.GetScheduleAsync(scheduleId);

        return Json(new { success = true, schedule = MapScheduleDto(schedule!) });
    }

    // POST /umbraco/surface/schedule/deleteSchedule
    [HttpPost("deleteSchedule")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSchedule([FromBody] IdRequest req)
    {
        var schedule = await _scheduleService.GetScheduleAsync(req.Id);
        if (schedule == null) return Json(new { success = false, error = "Vagtplan ikke fundet." });

        var (authorized, errorResult) = await AuthorizeCrewEditorAsync(schedule.CrewId);
        if (!authorized) return errorResult!;

        await _scheduleService.DeleteScheduleAsync(req.Id);
        return Json(new { success = true });
    }

    // POST /umbraco/surface/schedule/addSingleShift
    [HttpPost("addSingleShift")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSingleShift([FromBody] AddSingleShiftRequest req)
    {
        var schedule = await _scheduleService.GetScheduleAsync(req.ScheduleId);
        if (schedule == null) return Json(new { success = false, error = "Vagtplan ikke fundet." });

        var (authorized, errorResult) = await AuthorizeCrewEditorAsync(schedule.CrewId);
        if (!authorized) return errorResult!;

        if (req.Count <= 0 || req.Count > 100)
            return Json(new { success = false, error = "Antal skal være mellem 1 og 100." });

        await _scheduleService.AddSingleShiftAsync(req.ScheduleId, req.StartTime, req.EndTime, req.Count);

        var updated = await _scheduleService.GetScheduleAsync(req.ScheduleId);
        return Json(new { success = true, shifts = updated!.Shifts.Select(MapShiftDto) });
    }

    // POST /umbraco/surface/schedule/addSmartShift
    [HttpPost("addSmartShift")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSmartShift([FromBody] AddSmartShiftRequest req)
    {
        var schedule = await _scheduleService.GetScheduleAsync(req.ScheduleId);
        if (schedule == null) return Json(new { success = false, error = "Vagtplan ikke fundet." });

        var (authorized, errorResult) = await AuthorizeCrewEditorAsync(schedule.CrewId);
        if (!authorized) return errorResult!;

        if (req.SlotMinutes <= 0)
            return Json(new { success = false, error = "Slotlængde skal være større end 0." });

        if (req.ShiftsPerSlot <= 0 || req.ShiftsPerSlot > 50)
            return Json(new { success = false, error = "Vagter pr. slot skal være mellem 1 og 50." });

        await _scheduleService.AddSmartShiftsAsync(
            req.ScheduleId, req.FirstStart, req.LastEnd, req.SlotMinutes, req.ShiftsPerSlot);

        var updated = await _scheduleService.GetScheduleAsync(req.ScheduleId);
        return Json(new { success = true, shifts = updated!.Shifts.Select(MapShiftDto) });
    }

    // POST /umbraco/surface/schedule/deleteShifts
    [HttpPost("deleteShifts")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteShifts([FromBody] DeleteShiftsRequest req)
    {
        if (req.ShiftIds == null || !req.ShiftIds.Any())
            return Json(new { success = false, error = "Ingen vagter valgt." });

        var schedule = await _scheduleService.GetScheduleAsync(req.ScheduleId);
        if (schedule == null) return Json(new { success = false, error = "Vagtplan ikke fundet." });

        var (authorized, errorResult) = await AuthorizeCrewEditorAsync(schedule.CrewId);
        if (!authorized) return errorResult!;

        await _scheduleService.DeleteShiftsAsync(req.ShiftIds);

        var updated = await _scheduleService.GetScheduleAsync(req.ScheduleId);
        return Json(new { success = true, shifts = updated!.Shifts.Select(MapShiftDto) });
    }

    // POST /umbraco/surface/schedule/assign
    [HttpPost("assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign([FromBody] AssignRequest req)
    {
        var schedule = await _scheduleService.GetScheduleAsync(req.ScheduleId);
        if (schedule == null) return Json(new { success = false, error = "Vagtplan ikke fundet." });

        var (authorized, errorResult) = await AuthorizeCrewEditorAsync(schedule.CrewId);
        if (!authorized) return errorResult!;

        if (!Guid.TryParse(req.MemberKey, out var memberKey))
            return Json(new { success = false, error = "Ugyldig member-nøgle." });

        var assigned = await _scheduleService.AssignMemberToShiftAsync(req.ShiftId, memberKey, req.MemberName);
        if (!assigned)
            return Json(new { success = false, error = "Vagten er allerede booket eller findes ikke." });

        return Json(new { success = true });
    }

    // POST /umbraco/surface/schedule/unassign
    [HttpPost("unassign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unassign([FromBody] UnassignRequest req)
    {
        var schedule = await _scheduleService.GetScheduleAsync(req.ScheduleId);
        if (schedule == null) return Json(new { success = false, error = "Vagtplan ikke fundet." });

        var (authorized, errorResult) = await AuthorizeCrewEditorAsync(schedule.CrewId);
        if (!authorized) return errorResult!;

        await _scheduleService.UnassignMemberFromShiftAsync(req.ShiftId);
        return Json(new { success = true });
    }

    // POST /umbraco/surface/schedule/publish
    [HttpPost("publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish([FromBody] IdRequest req)
    {
        var schedule = await _scheduleService.GetScheduleAsync(req.Id);
        if (schedule == null) return Json(new { success = false, error = "Vagtplan ikke fundet." });

        var (authorized, errorResult) = await AuthorizeCrewEditorAsync(schedule.CrewId);
        if (!authorized) return errorResult!;

        await _scheduleService.PublishScheduleAsync(req.Id);
        return Json(new { success = true });
    }

    // POST /umbraco/surface/schedule/unpublish
    [HttpPost("unpublish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish([FromBody] IdRequest req)
    {
        var schedule = await _scheduleService.GetScheduleAsync(req.Id);
        if (schedule == null) return Json(new { success = false, error = "Vagtplan ikke fundet." });

        var (authorized, errorResult) = await AuthorizeCrewEditorAsync(schedule.CrewId);
        if (!authorized) return errorResult!;

        await _scheduleService.UnpublishScheduleAsync(req.Id);
        return Json(new { success = true });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(bool authorized, IActionResult? errorResult)> AuthorizeCrewEditorAsync(int crewId)
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
            return (false, Json(new { success = false, error = "Ikke logget ind." }));

        var viewMode = await _crewService.GetMemberCrewViewModeAsync(currentMember.Email!, crewId);
        if (viewMode == CrewViewMode.Volunteer)
            return (false, Json(new { success = false, error = "Ingen adgang." }));

        return (true, null);
    }

    private static object MapScheduleDto(ScheduleData s) => new
    {
        id = s.Id,
        crewId = s.CrewId,
        crewKey = s.CrewKey,
        name = s.Name,
        scheduleDate = s.ScheduleDate.ToString("yyyy-MM-dd"),
        scheduleDateFormatted = s.ScheduleDate.ToString("d. MMMM yyyy", new System.Globalization.CultureInfo("da-DK")),
        isPublished = s.IsPublished,
        shifts = s.Shifts.Select(MapShiftDto)
    };

    private static object MapShiftDto(ScheduleShiftData sh) => new
    {
        id = sh.Id,
        scheduleId = sh.ScheduleId,
        startTime = sh.StartTime,
        endTime = sh.EndTime,
        assignedMemberKey = sh.AssignedMemberKey,
        assignedMemberName = sh.AssignedMemberName,
        isAvailable = sh.IsAvailable,
        formattedTime = sh.FormattedTime,
        duration = sh.Duration
    };
}

// ── Request models ────────────────────────────────────────────────────────────

public record CreateScheduleRequest(int CrewId, string CrewKey, string Name, string Date);
public record AddSingleShiftRequest(int ScheduleId, string StartTime, string EndTime, int Count);
public record AddSmartShiftRequest(int ScheduleId, string FirstStart, string LastEnd, int SlotMinutes, int ShiftsPerSlot);
public record DeleteShiftsRequest(int ScheduleId, IEnumerable<int> ShiftIds);
public record AssignRequest(int ScheduleId, int ShiftId, string MemberKey, string MemberName);
public record UnassignRequest(int ScheduleId, int ShiftId);
public record IdRequest(int Id);
