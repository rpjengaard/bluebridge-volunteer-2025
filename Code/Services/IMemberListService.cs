namespace Code.Services;

public interface IMemberListService
{
    Task<MemberListData?> GetAllMembersAsync(string requestingMemberEmail);
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
}
