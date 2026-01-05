using Code.Entities;

namespace Code.Services.DTOs;

/// <summary>
/// DTO for crew job listing
/// </summary>
public record CrewJobListItem
{
    public int JobId { get; init; }
    public int CrewContentId { get; init; }
    public Guid CrewKey { get; init; }
    public string CrewName { get; init; } = string.Empty;
    public string CrewUrl { get; init; } = string.Empty;
    public string JobTitle { get; init; } = string.Empty;
    public string? JobDescription { get; init; }
    public int TotalPositions { get; init; }
    public int FilledPositions { get; init; }
    public int AvailablePositions { get; init; }
    public bool IsActive { get; init; }
    public bool HasUserApplied { get; init; }
    public int? UserApplicationId { get; init; }
    public ApplicationStatus? UserApplicationStatus { get; init; }
}

/// <summary>
/// DTO for job application details
/// </summary>
public record JobApplicationDetail
{
    public int ApplicationId { get; init; }
    public int JobId { get; init; }
    public string JobTitle { get; init; } = string.Empty;
    public int CrewContentId { get; init; }
    public Guid CrewKey { get; init; }
    public string CrewName { get; init; } = string.Empty;
    public string CrewUrl { get; init; } = string.Empty;
    public int MemberId { get; init; }
    public Guid MemberKey { get; init; }
    public string MemberEmail { get; init; } = string.Empty;
    public string MemberName { get; init; } = string.Empty;
    public string? MemberPhone { get; init; }
    public DateTime? MemberBirthdate { get; init; }
    public int? MemberAge { get; init; }
    public ApplicationStatus Status { get; init; }
    public string? ApplicationMessage { get; init; }
    public DateTime SubmittedDate { get; init; }
    public DateTime? ReviewedDate { get; init; }
    public int? ReviewedByMemberId { get; init; }
    public string? ReviewedByName { get; init; }
    public string? TicketLink { get; init; }
    public string? AdminNotes { get; init; }
}

/// <summary>
/// DTO for available jobs page
/// </summary>
public record AvailableJobsData
{
    public List<CrewJobListItem> Jobs { get; init; } = new();
    public bool IsAuthenticated { get; init; }
    public int TotalJobs { get; init; }
    public int TotalAvailablePositions { get; init; }
}

/// <summary>
/// DTO for job application submission
/// </summary>
public record SubmitApplicationRequest
{
    public int JobId { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Result of application submission
/// </summary>
public record SubmitApplicationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int? ApplicationId { get; init; }
}

/// <summary>
/// DTO for managing applications (admin/scheduler view)
/// </summary>
public record ManageApplicationsData
{
    public List<JobApplicationDetail> PendingApplications { get; init; } = new();
    public List<JobApplicationDetail> AcceptedApplications { get; init; } = new();
    public List<JobApplicationDetail> RejectedApplications { get; init; } = new();
    public bool IsAdmin { get; init; }
    public bool IsScheduler { get; init; }
    public List<int> ManagedCrewIds { get; init; } = new();
}

/// <summary>
/// DTO for reviewing an application
/// </summary>
public record ReviewApplicationRequest
{
    public int ApplicationId { get; init; }
    public ApplicationStatus NewStatus { get; init; }
    public string? AdminNotes { get; init; }
    public string? TicketLink { get; init; }
}

/// <summary>
/// Result of application review
/// </summary>
public record ReviewApplicationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool EmailSent { get; init; }
}

/// <summary>
/// DTO for creating a new job
/// </summary>
public record CreateJobRequest
{
    public int CrewContentId { get; init; }
    public Guid CrewKey { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int TotalPositions { get; init; }
}

/// <summary>
/// DTO for updating a job
/// </summary>
public record UpdateJobRequest
{
    public int JobId { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public int? TotalPositions { get; init; }
    public bool? IsActive { get; init; }
}
