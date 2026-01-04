namespace Code.Services;

public interface IApplicationsService
{
    Task<ApplicationsPageData> GetApplicationsForMemberAsync(string memberEmail);
}

public class ApplicationsPageData
{
    public bool IsAdmin { get; set; }
    public bool IsScheduler { get; set; }
    public List<ApplicationInfo> Applications { get; set; } = new();
    public List<CrewListItem> AllowedCrews { get; set; } = new();
}

public class ApplicationInfo
{
    public int MemberId { get; set; }
    public Guid MemberKey { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateTime? Birthdate { get; set; }
    public int? Age { get; set; }
    public string? Zipcode { get; set; }
    public string? TidligereArbejdssteder { get; set; }
    public DateTime? AcceptedDate { get; set; }
    public List<CrewListItem> CrewWishes { get; set; } = new();
}
