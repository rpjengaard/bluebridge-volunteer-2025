namespace Web.ViewModels;

public class DashboardViewModel
{
    public MemberProfileViewModel Profile { get; set; } = new();
    public List<CrewViewModel> CrewWishes { get; set; } = new();
    public List<CrewViewModel> AssignedCrews { get; set; } = new();
    public List<ShiftViewModel> Shifts { get; set; } = new();
    public bool HasAccepted2026 { get; set; }
    public DateTime? AcceptedDate { get; set; }
}

public class MemberProfileViewModel
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateTime? Birthdate { get; set; }
    public string? PreviousWorkplaces { get; set; }
}

public class CrewViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? AgeLimit { get; set; }
    public string? Url { get; set; }
}

public class ShiftViewModel
{
    public int Id { get; set; }
    public string CrewName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }

    public string FormattedDate => StartTime.ToString("dddd d. MMMM", new System.Globalization.CultureInfo("da-DK"));
    public string FormattedTime => $"{StartTime:HH:mm} - {EndTime:HH:mm}";
    public string Duration
    {
        get
        {
            var duration = EndTime - StartTime;
            return duration.TotalHours >= 1
                ? $"{duration.TotalHours:0.#} timer"
                : $"{duration.TotalMinutes:0} min";
        }
    }
}
