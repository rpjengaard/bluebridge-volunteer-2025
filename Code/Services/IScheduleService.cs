namespace Code.Services;

public class ScheduleData
{
    public int Id { get; set; }
    public int CrewId { get; set; }
    public Guid CrewKey { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime ScheduleDate { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedUtc { get; set; }
    public List<ScheduleShiftData> Shifts { get; set; } = new();
}

public class ScheduleShiftData
{
    public int Id { get; set; }
    public int ScheduleId { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public Guid? AssignedMemberKey { get; set; }
    public string? AssignedMemberName { get; set; }
    // [CHANGE: internal bookings without a member] Related: AddScheduleTablesMigration.cs, ScheduleService.cs, Web/Controllers/ScheduleGetController.cs, Web/Views/CrewSchedule.cshtml
    public bool IsInternal { get; set; }
    public string? Title { get; set; }
    public bool IsAvailable => AssignedMemberKey == null && !IsInternal;

    // Dashboard-only fields (populated by GetShiftsForMemberAsync)
    public string? CrewName { get; set; }
    public string? ScheduleName { get; set; }
    public DateTime ScheduleDate { get; set; }

    // Computed display helpers
    public string FormattedTime => $"{StartTime} – {EndTime}";

    public string Duration
    {
        get
        {
            var startMins = TimeToMinutes(StartTime);
            var endMins = TimeToMinutes(EndTime);
            if (endMins == 0) endMins = 24 * 60; // midnight = end of day
            if (endMins <= startMins) endMins += 24 * 60; // spans midnight
            var diff = endMins - startMins;
            var hours = diff / 60;
            var mins = diff % 60;
            if (hours > 0 && mins > 0) return $"{hours} t {mins} min";
            if (hours > 0) return $"{hours} time{(hours > 1 ? "r" : "")}";
            return $"{mins} min";
        }
    }

    private static int TimeToMinutes(string time)
    {
        if (string.IsNullOrEmpty(time)) return 0;
        var parts = time.Split(':');
        if (parts.Length != 2) return 0;
        if (int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
            return h * 60 + m;
        return 0;
    }
}

public interface IScheduleService
{
    Task<List<ScheduleData>> GetSchedulesForCrewAsync(int crewId);
    Task<ScheduleData?> GetScheduleAsync(int scheduleId);
    Task<int> CreateScheduleAsync(int crewId, Guid crewKey, string name, DateTime date);
    Task DeleteScheduleAsync(int scheduleId);
    Task AddSingleShiftAsync(int scheduleId, string startTime, string endTime, int count);
    Task AddSmartShiftsAsync(int scheduleId, string firstStart, string lastEnd, int slotMinutes, int shiftsPerSlot);
    Task DeleteShiftsAsync(IEnumerable<int> shiftIds);
    Task<bool> AssignMemberToShiftAsync(int shiftId, Guid memberKey, string memberName);
    Task<bool> UnassignMemberFromShiftAsync(int shiftId);
    // [CHANGE: internal bookings without a member] Related: AddScheduleTablesMigration.cs, ScheduleService.cs, Web/Controllers/ScheduleGetController.cs, Web/Views/CrewSchedule.cshtml
    Task<bool> BookInternalShiftAsync(int shiftId, string title);
    Task<bool> UnbookInternalShiftAsync(int shiftId);
    Task PublishScheduleAsync(int scheduleId);
    Task UnpublishScheduleAsync(int scheduleId);
    Task<List<ScheduleShiftData>> GetShiftsForMemberAsync(Guid memberKey);
    Task<List<ScheduleShiftData>> GetAllShiftsForMemberAsync(Guid memberKey);
    // [CHANGE: hasShift filter on member export] Related: ScheduleService.cs, IMemberListService.cs, MemberListService.cs, Web/Controllers/MemberExportApiController.cs
    Task<HashSet<Guid>> GetAssignedMemberKeysAsync();
    Task<List<ScheduleData>> GetPublishedSchedulesForCrewAsync(int crewId);
    // [CHANGE: bbvShiftList cross-crew overview] Related: ScheduleService.cs, Web/Views/ShiftList.cshtml
    Task<List<ScheduleData>> GetUpcomingSchedulesAsync();
}
