namespace Code.Services;

public interface IDashboardService
{
    Task<DashboardData?> GetDashboardDataAsync(string memberEmail);
}

public class DashboardData
{
    public MemberProfileData Profile { get; set; } = new();
    public List<CrewData> CrewWishes { get; set; } = new();
    public List<CrewData> AssignedCrews { get; set; } = new();
    public List<ShiftData> Shifts { get; set; } = new();
    public bool HasAccepted2026 { get; set; }
    public DateTime? AcceptedDate { get; set; }
}

public class MemberProfileData
{
    public int MemberId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateTime? Birthdate { get; set; }
    public string? PreviousWorkplaces { get; set; }
}

public class CrewData
{
    public int Id { get; set; }
    public Guid Key { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? AgeLimit { get; set; }
    public string? Url { get; set; }
}

public class ShiftData
{
    public int Id { get; set; }
    public string CrewName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
}
