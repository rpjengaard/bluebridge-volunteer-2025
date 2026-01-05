using Code.Entities;
using Code.Services.DTOs;

namespace Code.Services;

/// <summary>
/// Service for managing crew jobs and applications
/// </summary>
public interface IJobService
{
    // Job Management (Admin/Scheduler)
    Task<int> CreateJobAsync(CreateJobRequest request);
    Task<bool> UpdateJobAsync(UpdateJobRequest request);
    Task<bool> DeleteJobAsync(int jobId);
    Task<CrewJobListItem?> GetJobByIdAsync(int jobId, string? memberEmail = null);
    Task<List<CrewJobListItem>> GetJobsForCrewAsync(int crewContentId, string? memberEmail = null);

    // Available Jobs (Public/Member)
    Task<AvailableJobsData> GetAvailableJobsAsync(string? memberEmail = null);
    Task<List<CrewJobListItem>> GetActiveJobsAsync(string? memberEmail = null);

    // Application Submission (Member)
    Task<SubmitApplicationResult> SubmitApplicationAsync(string memberEmail, SubmitApplicationRequest request);
    Task<bool> WithdrawApplicationAsync(int applicationId, string memberEmail);
    Task<List<JobApplicationDetail>> GetMemberApplicationsAsync(string memberEmail);

    // Application Management (Admin/Scheduler)
    Task<ManageApplicationsData> GetApplicationsForReviewAsync(string adminEmail);
    Task<JobApplicationDetail?> GetApplicationDetailAsync(int applicationId);
    Task<ReviewApplicationResult> ReviewApplicationAsync(string reviewerEmail, ReviewApplicationRequest request);
    Task<List<JobApplicationDetail>> GetApplicationsForJobAsync(int jobId);
    Task<List<JobApplicationDetail>> GetApplicationsForCrewAsync(int crewContentId);

    // Statistics
    Task<int> GetTotalAvailablePositionsAsync();
    Task<int> GetPendingApplicationCountAsync(string? adminEmail = null);
}
