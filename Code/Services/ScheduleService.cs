using Code.Migrations;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Code.Services;

public class ScheduleService : IScheduleService
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<ScheduleService> _logger;

    public ScheduleService(IScopeProvider scopeProvider, ILogger<ScheduleService> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public Task<List<ScheduleData>> GetSchedulesForCrewAsync(int crewId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var schedules = db.Fetch<ScheduleSchema>(
            "SELECT * FROM BbvSchedule WHERE CrewId = @0 ORDER BY ScheduleDate ASC, Name ASC", crewId);

        var result = schedules.Select(MapSchedule).ToList();
        LoadShiftsForSchedules(db, result);

        return Task.FromResult(result);
    }

    public Task<ScheduleData?> GetScheduleAsync(int scheduleId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var row = db.SingleOrDefault<ScheduleSchema>(
            "SELECT * FROM BbvSchedule WHERE Id = @0", scheduleId);

        if (row == null) return Task.FromResult<ScheduleData?>(null);

        var schedule = MapSchedule(row);
        schedule.Shifts = GetShiftsForSchedule(db, scheduleId);

        return Task.FromResult<ScheduleData?>(schedule);
    }

    public Task<int> CreateScheduleAsync(int crewId, Guid crewKey, string name, DateTime date)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var row = new ScheduleSchema
        {
            CrewId = crewId,
            CrewKey = crewKey,
            Name = name,
            ScheduleDate = date.Date,
            IsPublished = false,
            CreatedUtc = DateTime.UtcNow
        };

        db.Insert(row);
        return Task.FromResult(row.Id);
    }

    public Task DeleteScheduleAsync(int scheduleId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        // Cascade delete handles shifts via FK constraint
        db.Execute("DELETE FROM BbvSchedule WHERE Id = @0", scheduleId);

        return Task.CompletedTask;
    }

    public Task AddSingleShiftAsync(int scheduleId, string startTime, string endTime, int count)
    {
        if (!IsValidTime(startTime)) throw new ArgumentException("Ugyldig starttid.", nameof(startTime));
        if (!IsValidTime(endTime))   throw new ArgumentException("Ugyldig sluttid.", nameof(endTime));
        if (count <= 0 || count > 100) throw new ArgumentOutOfRangeException(nameof(count), "Antal skal være 1–100.");

        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        for (int i = 0; i < count; i++)
        {
            db.Insert(new ShiftSchema
            {
                ScheduleId = scheduleId,
                StartTime = startTime,
                EndTime = endTime,
                AssignedMemberKey = null,
                AssignedMemberName = null,
                CreatedUtc = DateTime.UtcNow
            });
        }

        return Task.CompletedTask;
    }

    public Task AddSmartShiftsAsync(int scheduleId, string firstStart, string lastEnd, int slotMinutes, int shiftsPerSlot)
    {
        if (!IsValidTime(firstStart)) throw new ArgumentException("Ugyldig starttid.", nameof(firstStart));
        if (!IsValidTime(lastEnd))    throw new ArgumentException("Ugyldig sluttid.", nameof(lastEnd));
        if (slotMinutes <= 0) throw new ArgumentException("slotMinutes must be > 0", nameof(slotMinutes));
        if (shiftsPerSlot <= 0) throw new ArgumentException("shiftsPerSlot must be > 0", nameof(shiftsPerSlot));

        var startMins = TimeToMinutes(firstStart);
        var endMins = TimeToMinutes(lastEnd);

        // "00:00" as lastEnd means midnight (end of day = 24 * 60)
        if (endMins == 0) endMins = 24 * 60;
        // If lastEnd <= firstStart, it spans midnight
        if (endMins <= startMins) endMins += 24 * 60;

        // Safety cap: refuse to generate more than 500 shifts at once
        var totalSlots = (endMins - startMins + slotMinutes - 1) / slotMinutes;
        if ((long)totalSlots * shiftsPerSlot > 500)
            throw new ArgumentException(
                "For mange vagter ville blive genereret. Begræns tidsvinduet eller øg slot-længden (maks. 500 vagter ad gangen).");

        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var current = startMins;
        while (current < endMins)
        {
            var slotEnd = current + slotMinutes;
            var startStr = MinutesToTime(current);
            var endStr = MinutesToTime(slotEnd >= 24 * 60 ? slotEnd - 24 * 60 : slotEnd);

            for (int i = 0; i < shiftsPerSlot; i++)
            {
                db.Insert(new ShiftSchema
                {
                    ScheduleId = scheduleId,
                    StartTime = startStr,
                    EndTime = endStr,
                    AssignedMemberKey = null,
                    AssignedMemberName = null,
                    CreatedUtc = DateTime.UtcNow
                });
            }

            current = slotEnd;
        }

        return Task.CompletedTask;
    }

    public Task DeleteShiftsAsync(IEnumerable<int> shiftIds)
    {
        var ids = shiftIds.ToList();
        if (!ids.Any()) return Task.CompletedTask;

        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        // Only delete shifts that are NOT booked
        var placeholders = string.Join(",", ids.Select((_, i) => $"@{i}"));
        var args = ids.Cast<object>().ToArray();
        db.Execute(
            $"DELETE FROM BbvShift WHERE Id IN ({placeholders}) AND AssignedMemberKey IS NULL",
            args);

        return Task.CompletedTask;
    }

    public Task<bool> AssignMemberToShiftAsync(int shiftId, Guid memberKey, string memberName)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        // Only assign if shift exists and is not already booked
        var rows = db.Execute(
            "UPDATE BbvShift SET AssignedMemberKey = @0, AssignedMemberName = @1 WHERE Id = @2 AND AssignedMemberKey IS NULL",
            memberKey, memberName, shiftId);

        return Task.FromResult(rows > 0);
    }

    public Task<bool> UnassignMemberFromShiftAsync(int shiftId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var rows = db.Execute(
            "UPDATE BbvShift SET AssignedMemberKey = NULL, AssignedMemberName = NULL WHERE Id = @0",
            shiftId);

        return Task.FromResult(rows > 0);
    }

    public Task PublishScheduleAsync(int scheduleId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        scope.Database.Execute("UPDATE BbvSchedule SET IsPublished = 1 WHERE Id = @0", scheduleId);
        return Task.CompletedTask;
    }

    public Task UnpublishScheduleAsync(int scheduleId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        scope.Database.Execute("UPDATE BbvSchedule SET IsPublished = 0 WHERE Id = @0", scheduleId);
        return Task.CompletedTask;
    }

    public Task<List<ScheduleShiftData>> GetShiftsForMemberAsync(Guid memberKey)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        // Only return shifts from PUBLISHED schedules – volunteers must not see drafts
        var rows = db.Fetch<dynamic>(@"
            SELECT sh.Id, sh.ScheduleId, sh.StartTime, sh.EndTime,
                   sh.AssignedMemberKey, sh.AssignedMemberName,
                   sc.Name AS ScheduleName, sc.ScheduleDate,
                   sc.CrewId, sc.CrewKey
            FROM BbvShift sh
            INNER JOIN BbvSchedule sc ON sh.ScheduleId = sc.Id
            WHERE sh.AssignedMemberKey = @0
              AND sc.IsPublished = 1
            ORDER BY sc.ScheduleDate ASC, sh.StartTime ASC", memberKey);

        var shifts = rows.Select(r => new ScheduleShiftData
        {
            Id = (int)r.Id,
            ScheduleId = (int)r.ScheduleId,
            StartTime = (string)r.StartTime,
            EndTime = (string)r.EndTime,
            AssignedMemberKey = (Guid?)r.AssignedMemberKey,
            AssignedMemberName = (string?)r.AssignedMemberName,
            ScheduleName = (string)r.ScheduleName,
            ScheduleDate = (DateTime)r.ScheduleDate
        }).ToList();

        return Task.FromResult(shifts);
    }

    public Task<List<ScheduleData>> GetPublishedSchedulesForCrewAsync(int crewId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var schedules = db.Fetch<ScheduleSchema>(
            "SELECT * FROM BbvSchedule WHERE CrewId = @0 AND IsPublished = 1 ORDER BY ScheduleDate ASC, Name ASC",
            crewId);

        var result = schedules.Select(MapSchedule).ToList();
        LoadShiftsForSchedules(db, result);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Batch-loads shifts for a list of schedules in a single SQL query (avoids N+1).
    /// </summary>
    private static void LoadShiftsForSchedules(
        Umbraco.Cms.Infrastructure.Persistence.IUmbracoDatabase db,
        List<ScheduleData> schedules)
    {
        if (schedules.Count == 0) return;

        var ids = schedules.Select(s => s.Id).ToList();
        var placeholders = string.Join(",", ids.Select((_, i) => $"@{i}"));
        var args = ids.Cast<object>().ToArray();

        var rows = db.Fetch<ShiftSchema>(
            $"SELECT * FROM BbvShift WHERE ScheduleId IN ({placeholders}) ORDER BY ScheduleId ASC, StartTime ASC, CreatedUtc ASC",
            args);

        var shiftsBySchedule = rows
            .GroupBy(r => r.ScheduleId)
            .ToDictionary(g => g.Key, g => g.Select(MapShift).ToList());

        foreach (var schedule in schedules)
        {
            schedule.Shifts = shiftsBySchedule.TryGetValue(schedule.Id, out var shifts)
                ? shifts
                : new List<ScheduleShiftData>();
        }
    }

    private static List<ScheduleShiftData> GetShiftsForSchedule(
        Umbraco.Cms.Infrastructure.Persistence.IUmbracoDatabase db, int scheduleId)
    {
        var rows = db.Fetch<ShiftSchema>(
            "SELECT * FROM BbvShift WHERE ScheduleId = @0 ORDER BY StartTime ASC, CreatedUtc ASC",
            scheduleId);

        return rows.Select(MapShift).ToList();
    }

    private static ScheduleData MapSchedule(ScheduleSchema s) => new()
    {
        Id = s.Id,
        CrewId = s.CrewId,
        CrewKey = s.CrewKey,
        Name = s.Name,
        ScheduleDate = s.ScheduleDate,
        IsPublished = s.IsPublished,
        CreatedUtc = s.CreatedUtc
    };

    private static ScheduleShiftData MapShift(ShiftSchema s) => new()
    {
        Id = s.Id,
        ScheduleId = s.ScheduleId,
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        AssignedMemberKey = s.AssignedMemberKey,
        AssignedMemberName = s.AssignedMemberName
    };

    private static int TimeToMinutes(string time)
    {
        if (string.IsNullOrEmpty(time)) return 0;
        var parts = time.Split(':');
        if (parts.Length != 2) return 0;
        if (int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
            return h * 60 + m;
        return 0;
    }

    private static string MinutesToTime(int totalMinutes)
    {
        var h = totalMinutes / 60 % 24;
        var m = totalMinutes % 60;
        return $"{h:00}:{m:00}";
    }

    /// <summary>Returns true iff <paramref name="time"/> is a valid "HH:mm" string (00:00 – 23:59).</summary>
    private static bool IsValidTime(string? time) =>
        !string.IsNullOrEmpty(time) &&
        time.Length == 5 &&
        time[2] == ':' &&
        int.TryParse(time.AsSpan(0, 2), out var h) && h >= 0 && h <= 23 &&
        int.TryParse(time.AsSpan(3, 2), out var m) && m >= 0 && m <= 59;
}
