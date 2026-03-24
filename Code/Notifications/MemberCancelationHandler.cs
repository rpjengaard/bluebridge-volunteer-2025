using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Code.Services;

namespace Code.Notifications;

public class MemberCancelationHandler : INotificationAsyncHandler<MemberSavedNotification>
{
    private readonly IScheduleService _scheduleService;
    private readonly IMemberEmailService _emailService;
    private readonly IContentService _contentService;
    private readonly IMemberService _memberService;
    private readonly ILogger<MemberCancelationHandler> _logger;

    public MemberCancelationHandler(
        IScheduleService scheduleService,
        IMemberEmailService emailService,
        IContentService contentService,
        IMemberService memberService,
        ILogger<MemberCancelationHandler> logger)
    {
        _scheduleService = scheduleService;
        _emailService = emailService;
        _contentService = contentService;
        _memberService = memberService;
        _logger = logger;
    }

    public async Task HandleAsync(MemberSavedNotification notification, CancellationToken cancellationToken)
    {
        foreach (var member in notification.SavedEntities)
        {
            // Skip non-canceled members entirely
            if (!member.GetValue<bool>("cancelation"))
                continue;

            var firstName = member.GetValue<string>("firstName") ?? string.Empty;
            var lastName = member.GetValue<string>("lastName") ?? string.Empty;
            var fullName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrEmpty(fullName))
                fullName = member.Name ?? member.Email ?? "Unknown";

            // Always clean up shifts for canceled members — handles re-saves and edge cases
            var shifts = await _scheduleService.GetAllShiftsForMemberAsync(member.Key);
            if (!shifts.Any())
                continue;

            _logger.LogInformation("Member {FullName} ({Email}) is canceled and has {Count} shift(s) — removing.", fullName, member.Email, shifts.Count);

            // Only send notification emails when cancelation was just toggled on
            var justCanceled = member.WasPropertyDirty("cancelation");

            // Resolve CrewId for each unique ScheduleId
            var crewIdByScheduleId = new Dictionary<int, int>();
            foreach (var scheduleId in shifts.Select(s => s.ScheduleId).Distinct())
            {
                var schedule = await _scheduleService.GetScheduleAsync(scheduleId);
                if (schedule != null)
                    crewIdByScheduleId[scheduleId] = schedule.CrewId;
            }

            // Unassign all shifts first
            foreach (var shift in shifts)
            {
                try
                {
                    await _scheduleService.UnassignMemberFromShiftAsync(shift.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to unassign shift {ShiftId} for canceled member {FullName}.", shift.Id, fullName);
                }
            }

            _logger.LogInformation("Removed {Count} shift(s) for canceled member {FullName}.", shifts.Count, fullName);

            // Only notify supervisors when cancelation was just toggled on (not on every re-save)
            if (!justCanceled)
                continue;

            // Group shifts by crew and notify supervisors
            var shiftsByCrewId = shifts
                .Where(s => crewIdByScheduleId.ContainsKey(s.ScheduleId))
                .GroupBy(s => crewIdByScheduleId[s.ScheduleId]);

            foreach (var crewGroup in shiftsByCrewId)
            {
                var crewId = crewGroup.Key;
                var crewShifts = crewGroup.ToList();
                var crewName = crewShifts.First().CrewName ?? $"Crew {crewId}";

                var content = _contentService.GetById(crewId);
                if (content == null) continue;

                var recipientEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                AddEmailsFromMemberUdi(content.GetValue<string>("scheduleSupervisor"), recipientEmails);
                AddEmailsFromMemberUdi(content.GetValue<string>("supervisors"), recipientEmails);

                if (!recipientEmails.Any())
                {
                    _logger.LogInformation("No supervisors found for crew {CrewName} — skipping notification.", crewName);
                    continue;
                }

                var culture = new System.Globalization.CultureInfo("da-DK");
                var shiftDescriptions = crewShifts
                    .OrderBy(s => s.ScheduleDate).ThenBy(s => s.StartTime)
                    .Select(s => $"{s.ScheduleName} – {s.ScheduleDate.ToString("d. MMMM yyyy", culture)} – {s.FormattedTime}")
                    .ToList();

                foreach (var email in recipientEmails)
                {
                    try
                    {
                        await _emailService.SendCancelationNotificationAsync(
                            email,
                            crewName,
                            fullName,
                            member.Email ?? string.Empty,
                            shiftDescriptions);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send cancelation notification to {Email} for crew {CrewName}.", email, crewName);
                    }
                }
            }
        }
    }

    private void AddEmailsFromMemberUdi(string? udiString, HashSet<string> emails)
    {
        if (string.IsNullOrWhiteSpace(udiString)) return;

        foreach (var part in udiString.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (!trimmed.StartsWith("umb://member/", StringComparison.OrdinalIgnoreCase)) continue;
            var guidPart = trimmed["umb://member/".Length..];
            if (!Guid.TryParse(guidPart, out var guid)) continue;

#pragma warning disable CS0618
            var supervisor = _memberService.GetByKey(guid);
#pragma warning restore CS0618
            if (supervisor?.Email != null)
                emails.Add(supervisor.Email);
        }
    }
}
