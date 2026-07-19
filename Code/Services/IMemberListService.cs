namespace Code.Services;

public interface IMemberListService
{
    Task<MemberListData?> GetAllMembersAsync(string requestingMemberEmail);

    // [CHANGE: hasShift filter on member export] Related: MemberListService.cs, IScheduleService.cs, Web/Controllers/MemberExportApiController.cs
    Task<List<MemberExportItem>> GetMemberExportAsync(string? groupFilter, bool hasShift = false);
}

public class MemberExportItem
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Crews { get; set; } = new();
    public List<string> MemberGroups { get; set; } = new();
    public DateTime SignupDate { get; set; }
    public bool IsCanceled { get; set; }
    public bool Accepted2026 { get; set; }
}

public class MemberListData
{
    public List<MemberListItem> Members { get; set; } = new();
    public List<string> AllCrewNames { get; set; } = new();
    public List<string> AllGroupNames { get; set; } = new();
    public bool IsAdmin { get; set; }
}

public class MemberListItem
{
    public Guid MemberKey { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime SignupDate { get; set; }
    public List<string> CrewNames { get; set; } = new();
    public List<string> MemberGroups { get; set; } = new();
    public bool IsCanceled { get; set; }
}
