namespace Code.Services;

public interface ICrewService
{
    Task<CrewsPageData> GetCrewsForMemberAsync(string memberEmail, bool isAdmin);
    Task<CrewDetailData?> GetCrewDetailAsync(int crewId, string memberEmail, CrewViewMode viewMode);
    Task<CrewViewMode> GetMemberCrewViewModeAsync(string memberEmail, int crewId);
}

public enum CrewViewMode
{
    Volunteer,
    Scheduler,
    Admin
}

public class CrewsPageData
{
    public bool IsAdmin { get; set; }
    public List<CrewListItem> Crews { get; set; } = new();
}

public class CrewListItem
{
    public int Id { get; set; }
    public Guid Key { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? AgeLimit { get; set; }
    public string? Url { get; set; }
    public int MemberCount { get; set; }
    public bool IsMemberAssigned { get; set; }
}

public class CrewDetailData
{
    public int Id { get; set; }
    public Guid Key { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DescriptionHtml { get; set; }
    public int? AgeLimit { get; set; }
    public string? Url { get; set; }
    public CrewViewMode ViewMode { get; set; }
    public List<CrewMemberInfo> Members { get; set; } = new();
    public List<CrewMemberInfo> WishlistMembers { get; set; } = new();
    public SupervisorInfo? ScheduleSupervisor { get; set; }
    public List<SupervisorInfo> Supervisors { get; set; } = new();
}

public class CrewMemberInfo
{
    public int MemberId { get; set; }
    public Guid MemberKey { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool HasAccepted2026 { get; set; }
}

public class SupervisorInfo
{
    public int MemberId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
}
