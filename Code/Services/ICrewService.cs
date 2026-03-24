namespace Code.Services;

public interface ICrewService
{
    Task<CrewsPageData> GetCrewsForMemberAsync(string memberEmail, bool isAdmin);
    Task<CrewDetailData?> GetCrewDetailAsync(int crewId, string memberEmail, CrewViewMode viewMode);
    Task<CrewViewMode> GetMemberCrewViewModeAsync(string memberEmail, int crewId);
    Task<MemberDetailData?> GetMemberByKeyAsync(Guid memberKey, string requestingMemberEmail);
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
    public int? MaxVoluntiers { get; set; }
    public int MemberCount { get; set; }
    public int WishCount { get; set; }
    public int AssignedCount { get; set; }  // Frivillige + assigned to crew + not rejected
    public int SupervisorCount { get; set; }
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
    public int? MaxVoluntiers { get; set; }
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
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool HasAccepted2026 { get; set; }
    public DateTime? AcceptedDate { get; set; }
    public DateTime SignupDate { get; set; }   // member.CreateDate – always set, used for "tilmeldingsdato" sort
    public DateTime? Birthdate { get; set; }
    public string? MemberWish { get; set; }
    public string? TimeslotWish { get; set; }
    public string? OtherNotes { get; set; }
    public List<string> MemberGroups { get; set; } = new();
    public List<CrewListItem> CrewWishes { get; set; } = new();
    public bool IsCanceled { get; set; }
}

public class SupervisorInfo
{
    public int MemberId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
}

public class MemberDetailData
{
    public int MemberId { get; set; }
    public Guid MemberKey { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateTime? Birthdate { get; set; }
    public string? TidligereArbejdssteder { get; set; }
    public bool Accept2026 { get; set; }
    public DateTime? AcceptedDate { get; set; }
    public DateTime? InvitationSentDate { get; set; }
    public string? MemberWish { get; set; }
    public string? TimeslotWish { get; set; }
    public List<CrewListItem> AssignedCrews { get; set; } = new();
    public string? OtherNotes { get; set; }
    public List<CrewListItem> CrewWishes { get; set; } = new();
    public List<string> MemberGroups { get; set; } = new();
}
