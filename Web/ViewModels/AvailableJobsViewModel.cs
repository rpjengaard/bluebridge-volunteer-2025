using Code.Services.DTOs;

namespace Web.ViewModels;

public class AvailableJobsViewModel
{
    public bool IsAuthenticated { get; set; }
    public string? MemberName { get; set; }
    public List<CrewJobListItem> Jobs { get; set; } = new();
    public int TotalJobs { get; set; }
    public int TotalAvailablePositions { get; set; }
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}
