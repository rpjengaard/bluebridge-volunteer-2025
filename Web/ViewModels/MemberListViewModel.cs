namespace Web.ViewModels;

public class MemberListViewModel
{
    public List<MemberListItemViewModel> Members { get; set; } = new();
    public List<string> AllCrews { get; set; } = new();
}

public class MemberListItemViewModel
{
    public Guid MemberKey { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime SignupDate { get; set; }
    public List<string> Crews { get; set; } = new();
}
