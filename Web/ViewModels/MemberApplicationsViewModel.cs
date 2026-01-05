using Code.Services.DTOs;

namespace Web.ViewModels;

public class MemberApplicationsViewModel
{
    public string MemberName { get; set; } = string.Empty;
    public List<JobApplicationDetail> Applications { get; set; } = new();
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}
