namespace Web.ViewModels;

public class ApplicationsViewModel
{
    public string MemberName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsScheduler { get; set; }
    public List<ApplicationItemViewModel> Applications { get; set; } = new();
    public List<CrewFilterItemViewModel> AllowedCrews { get; set; } = new();
}

public class ApplicationItemViewModel
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
    public List<CrewWishViewModel> CrewWishes { get; set; } = new();
}

public class CrewWishViewModel
{
    public int Id { get; set; }
    public Guid Key { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
}

public class CrewFilterItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
