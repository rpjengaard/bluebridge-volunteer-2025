namespace Web.ViewModels;

public class CrewsPageViewModel
{
    public string MemberName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public List<CrewListItemViewModel> Crews { get; set; } = new();
}

public class CrewListItemViewModel
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

public class CrewDetailViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? AgeLimit { get; set; }
    public string? Url { get; set; }
    public bool IsAdmin { get; set; }
    public List<CrewMemberViewModel> Members { get; set; } = new();
}

public class CrewMemberViewModel
{
    public int MemberId { get; set; }
    public Guid MemberKey { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool HasAccepted2026 { get; set; }
}
