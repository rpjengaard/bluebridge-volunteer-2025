namespace Web.ViewModels;

public class MemberListViewModel
{
    public List<MemberListItemViewModel> Members { get; set; } = new();
    public List<string> AllCrews { get; set; } = new();
    public List<string> AllGroups { get; set; } = new();
    public bool IsAdmin { get; set; }
}

public class MemberListItemViewModel
{
    public Guid MemberKey { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime SignupDate { get; set; }
    public List<string> Crews { get; set; } = new();
    public List<string> Groups { get; set; } = new();
    public bool IsCanceled { get; set; }
}
