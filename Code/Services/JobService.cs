using Code.Data;
using Code.Entities;
using Code.Services.DTOs;
using Microsoft.EntityFrameworkCore;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Code.Services;

/// <summary>
/// Service implementation for managing crew jobs and applications
/// </summary>
public class JobService : IJobService
{
    private readonly JobApplicationDbContext _dbContext;
    private readonly IUmbracoContextFactory _umbracoContextFactory;
    private readonly IMemberService _memberService;
    private readonly IMemberGroupService _memberGroupService;
    private readonly IContentService _contentService;
    private readonly IMemberEmailService _emailService;

    // Member Group GUIDs (same as ApplicationsService)
    private static readonly Guid AdminGroupKey = Guid.Parse("99e1edbb-8181-421d-a74b-e66a2f1e1148");
    private static readonly Guid SchedulerGroupKey = Guid.Parse("e6eef645-b13b-4edb-880b-7b3cdf5b6816");

    public JobService(
        JobApplicationDbContext dbContext,
        IUmbracoContextFactory umbracoContextFactory,
        IMemberService memberService,
        IMemberGroupService memberGroupService,
        IContentService contentService,
        IMemberEmailService emailService)
    {
        _dbContext = dbContext;
        _umbracoContextFactory = umbracoContextFactory;
        _memberService = memberService;
        _memberGroupService = memberGroupService;
        _contentService = contentService;
        _emailService = emailService;
    }

    #region Job Management

    public async Task<int> CreateJobAsync(CreateJobRequest request)
    {
        var job = new CrewJob
        {
            CrewContentId = request.CrewContentId,
            CrewKey = request.CrewKey,
            Title = request.Title,
            Description = request.Description,
            TotalPositions = request.TotalPositions,
            FilledPositions = 0,
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        };

        _dbContext.CrewJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        return job.Id;
    }

    public async Task<bool> UpdateJobAsync(UpdateJobRequest request)
    {
        var job = await _dbContext.CrewJobs.FindAsync(request.JobId);
        if (job == null)
            return false;

        if (request.Title != null)
            job.Title = request.Title;

        if (request.Description != null)
            job.Description = request.Description;

        if (request.TotalPositions.HasValue)
            job.TotalPositions = request.TotalPositions.Value;

        if (request.IsActive.HasValue)
            job.IsActive = request.IsActive.Value;

        job.UpdatedDate = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteJobAsync(int jobId)
    {
        var job = await _dbContext.CrewJobs.FindAsync(jobId);
        if (job == null)
            return false;

        _dbContext.CrewJobs.Remove(job);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<CrewJobListItem?> GetJobByIdAsync(int jobId, string? memberEmail = null)
    {
        var job = await _dbContext.CrewJobs
            .Include(j => j.Applications)
            .FirstOrDefaultAsync(j => j.Id == jobId);

        if (job == null)
            return null;

        return await MapToJobListItemAsync(job, memberEmail);
    }

    public async Task<List<CrewJobListItem>> GetJobsForCrewAsync(int crewContentId, string? memberEmail = null)
    {
        var jobs = await _dbContext.CrewJobs
            .Include(j => j.Applications)
            .Where(j => j.CrewContentId == crewContentId)
            .OrderByDescending(j => j.CreatedDate)
            .ToListAsync();

        var result = new List<CrewJobListItem>();
        foreach (var job in jobs)
        {
            var item = await MapToJobListItemAsync(job, memberEmail);
            result.Add(item);
        }

        return result;
    }

    #endregion

    #region Available Jobs

    public async Task<AvailableJobsData> GetAvailableJobsAsync(string? memberEmail = null)
    {
        var jobs = await GetActiveJobsAsync(memberEmail);

        return new AvailableJobsData
        {
            Jobs = jobs,
            IsAuthenticated = !string.IsNullOrEmpty(memberEmail),
            TotalJobs = jobs.Count,
            TotalAvailablePositions = jobs.Sum(j => j.AvailablePositions)
        };
    }

    public async Task<List<CrewJobListItem>> GetActiveJobsAsync(string? memberEmail = null)
    {
        var jobs = await _dbContext.CrewJobs
            .Include(j => j.Applications)
            .Where(j => j.IsActive && j.FilledPositions < j.TotalPositions)
            .OrderBy(j => j.CreatedDate)
            .ToListAsync();

        var result = new List<CrewJobListItem>();
        foreach (var job in jobs)
        {
            var item = await MapToJobListItemAsync(job, memberEmail);
            result.Add(item);
        }

        return result;
    }

    #endregion

    #region Application Submission

    public async Task<SubmitApplicationResult> SubmitApplicationAsync(string memberEmail, SubmitApplicationRequest request)
    {
        // Get member
        var member = _memberService.GetByEmail(memberEmail);
        if (member == null)
        {
            return new SubmitApplicationResult
            {
                Success = false,
                ErrorMessage = "Member not found"
            };
        }

        // Get job
        var job = await _dbContext.CrewJobs.FindAsync(request.JobId);
        if (job == null)
        {
            return new SubmitApplicationResult
            {
                Success = false,
                ErrorMessage = "Job not found"
            };
        }

        // Check if job is active and has available positions
        if (!job.IsActive)
        {
            return new SubmitApplicationResult
            {
                Success = false,
                ErrorMessage = "This job is no longer accepting applications"
            };
        }

        if (job.FilledPositions >= job.TotalPositions)
        {
            return new SubmitApplicationResult
            {
                Success = false,
                ErrorMessage = "This job has no available positions"
            };
        }

        // Check if member already applied
        var existingApplication = await _dbContext.JobApplications
            .FirstOrDefaultAsync(a => a.CrewJobId == request.JobId && a.MemberKey == member.Key);

        if (existingApplication != null)
        {
            return new SubmitApplicationResult
            {
                Success = false,
                ErrorMessage = "You have already applied for this job"
            };
        }

        // Create application
        var firstName = member.GetValue<string>("firstName") ?? "";
        var lastName = member.GetValue<string>("lastName") ?? "";
        var memberName = $"{firstName} {lastName}".Trim();

        var application = new JobApplication
        {
            CrewJobId = request.JobId,
            MemberId = member.Id,
            MemberKey = member.Key,
            MemberEmail = memberEmail,
            MemberName = memberName,
            Status = ApplicationStatus.Pending,
            ApplicationMessage = request.Message,
            SubmittedDate = DateTime.UtcNow
        };

        _dbContext.JobApplications.Add(application);
        await _dbContext.SaveChangesAsync();

        return new SubmitApplicationResult
        {
            Success = true,
            ApplicationId = application.Id
        };
    }

    public async Task<bool> WithdrawApplicationAsync(int applicationId, string memberEmail)
    {
        var application = await _dbContext.JobApplications
            .FirstOrDefaultAsync(a => a.Id == applicationId && a.MemberEmail == memberEmail);

        if (application == null)
            return false;

        // Only allow withdrawal of pending applications
        if (application.Status != ApplicationStatus.Pending)
            return false;

        application.Status = ApplicationStatus.Withdrawn;
        await _dbContext.SaveChangesAsync();

        return true;
    }

    public async Task<List<JobApplicationDetail>> GetMemberApplicationsAsync(string memberEmail)
    {
        var applications = await _dbContext.JobApplications
            .Include(a => a.CrewJob)
            .Where(a => a.MemberEmail == memberEmail)
            .OrderByDescending(a => a.SubmittedDate)
            .ToListAsync();

        var result = new List<JobApplicationDetail>();
        foreach (var app in applications)
        {
            var detail = await MapToApplicationDetailAsync(app);
            result.Add(detail);
        }

        return result;
    }

    #endregion

    #region Application Management

    public async Task<ManageApplicationsData> GetApplicationsForReviewAsync(string adminEmail)
    {
        var member = _memberService.GetByEmail(adminEmail);
        if (member == null)
        {
            return new ManageApplicationsData
            {
                IsAdmin = false,
                IsScheduler = false
            };
        }

        var isAdmin = IsAdmin(member.Id);
        var isScheduler = IsScheduler(member.Id);

        List<int> managedCrewIds = new();

        if (isScheduler && !isAdmin)
        {
            // Get crews managed by this scheduler
            managedCrewIds = GetCrewsForSupervisor(member.Key);
        }

        var query = _dbContext.JobApplications
            .Include(a => a.CrewJob)
            .AsQueryable();

        // Filter by managed crews for schedulers
        if (!isAdmin && isScheduler && managedCrewIds.Any())
        {
            query = query.Where(a => managedCrewIds.Contains(a.CrewJob.CrewContentId));
        }

        var applications = await query
            .OrderByDescending(a => a.SubmittedDate)
            .ToListAsync();

        var pending = new List<JobApplicationDetail>();
        var accepted = new List<JobApplicationDetail>();
        var rejected = new List<JobApplicationDetail>();

        foreach (var app in applications)
        {
            var detail = await MapToApplicationDetailAsync(app);

            switch (app.Status)
            {
                case ApplicationStatus.Pending:
                    pending.Add(detail);
                    break;
                case ApplicationStatus.Accepted:
                    accepted.Add(detail);
                    break;
                case ApplicationStatus.Rejected:
                    rejected.Add(detail);
                    break;
            }
        }

        return new ManageApplicationsData
        {
            PendingApplications = pending,
            AcceptedApplications = accepted,
            RejectedApplications = rejected,
            IsAdmin = isAdmin,
            IsScheduler = isScheduler,
            ManagedCrewIds = managedCrewIds
        };
    }

    public async Task<JobApplicationDetail?> GetApplicationDetailAsync(int applicationId)
    {
        var application = await _dbContext.JobApplications
            .Include(a => a.CrewJob)
            .FirstOrDefaultAsync(a => a.Id == applicationId);

        if (application == null)
            return null;

        return await MapToApplicationDetailAsync(application);
    }

    public async Task<ReviewApplicationResult> ReviewApplicationAsync(string reviewerEmail, ReviewApplicationRequest request)
    {
        var application = await _dbContext.JobApplications
            .Include(a => a.CrewJob)
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId);

        if (application == null)
        {
            return new ReviewApplicationResult
            {
                Success = false,
                ErrorMessage = "Application not found"
            };
        }

        // Get reviewer member ID
        var reviewer = _memberService.GetByEmail(reviewerEmail);
        if (reviewer == null)
        {
            return new ReviewApplicationResult
            {
                Success = false,
                ErrorMessage = "Reviewer not found"
            };
        }

        // Verify reviewer has permission
        var isAdmin = IsAdmin(reviewer.Id);
        var isScheduler = IsScheduler(reviewer.Id);

        if (!isAdmin && !isScheduler)
        {
            return new ReviewApplicationResult
            {
                Success = false,
                ErrorMessage = "You do not have permission to review applications"
            };
        }

        // Update application
        application.Status = request.NewStatus;
        application.ReviewedDate = DateTime.UtcNow;
        application.ReviewedByMemberId = reviewer.Id;
        application.AdminNotes = request.AdminNotes;

        if (!string.IsNullOrEmpty(request.TicketLink))
        {
            application.TicketLink = request.TicketLink;
        }

        // Update filled positions if accepted
        if (request.NewStatus == ApplicationStatus.Accepted)
        {
            application.CrewJob.FilledPositions++;
        }
        else if (application.Status == ApplicationStatus.Accepted && request.NewStatus != ApplicationStatus.Accepted)
        {
            // If changing from accepted to another status, decrement filled positions
            application.CrewJob.FilledPositions = Math.Max(0, application.CrewJob.FilledPositions - 1);
        }

        await _dbContext.SaveChangesAsync();

        // Send email if accepted
        bool emailSent = false;
        if (request.NewStatus == ApplicationStatus.Accepted)
        {
            var crewName = await GetCrewNameAsync(application.CrewJob.CrewContentId);
            emailSent = await _emailService.SendJobApplicationAcceptedEmailAsync(
                application.MemberEmail,
                application.MemberName,
                application.CrewJob.Title,
                crewName,
                application.TicketLink ?? "");
        }

        return new ReviewApplicationResult
        {
            Success = true,
            EmailSent = emailSent
        };
    }

    public async Task<List<JobApplicationDetail>> GetApplicationsForJobAsync(int jobId)
    {
        var applications = await _dbContext.JobApplications
            .Include(a => a.CrewJob)
            .Where(a => a.CrewJobId == jobId)
            .OrderByDescending(a => a.SubmittedDate)
            .ToListAsync();

        var result = new List<JobApplicationDetail>();
        foreach (var app in applications)
        {
            var detail = await MapToApplicationDetailAsync(app);
            result.Add(detail);
        }

        return result;
    }

    public async Task<List<JobApplicationDetail>> GetApplicationsForCrewAsync(int crewContentId)
    {
        var applications = await _dbContext.JobApplications
            .Include(a => a.CrewJob)
            .Where(a => a.CrewJob.CrewContentId == crewContentId)
            .OrderByDescending(a => a.SubmittedDate)
            .ToListAsync();

        var result = new List<JobApplicationDetail>();
        foreach (var app in applications)
        {
            var detail = await MapToApplicationDetailAsync(app);
            result.Add(detail);
        }

        return result;
    }

    #endregion

    #region Statistics

    public async Task<int> GetTotalAvailablePositionsAsync()
    {
        return await _dbContext.CrewJobs
            .Where(j => j.IsActive && j.FilledPositions < j.TotalPositions)
            .SumAsync(j => j.TotalPositions - j.FilledPositions);
    }

    public async Task<int> GetPendingApplicationCountAsync(string? adminEmail = null)
    {
        var query = _dbContext.JobApplications
            .Where(a => a.Status == ApplicationStatus.Pending);

        if (!string.IsNullOrEmpty(adminEmail))
        {
            var member = _memberService.GetByEmail(adminEmail);
            if (member != null)
            {
                var isAdmin = IsAdmin(member.Id);
                var isScheduler = IsScheduler(member.Id);

                if (isScheduler && !isAdmin)
                {
                    // Get managed crews for scheduler
                    var managedCrewIds = GetCrewsForSupervisor(member.Key);

                    if (managedCrewIds.Any())
                    {
                        query = query.Where(a => managedCrewIds.Contains(a.CrewJob.CrewContentId));
                    }
                }
            }
        }

        return await query.CountAsync();
    }

    #endregion

    #region Helper Methods

    private async Task<CrewJobListItem> MapToJobListItemAsync(CrewJob job, string? memberEmail)
    {
        var crewName = await GetCrewNameAsync(job.CrewContentId);
        var crewUrl = await GetCrewUrlAsync(job.CrewContentId);

        JobApplication? userApplication = null;
        if (!string.IsNullOrEmpty(memberEmail))
        {
            userApplication = job.Applications
                .FirstOrDefault(a => a.MemberEmail == memberEmail);
        }

        return new CrewJobListItem
        {
            JobId = job.Id,
            CrewContentId = job.CrewContentId,
            CrewKey = job.CrewKey,
            CrewName = crewName,
            CrewUrl = crewUrl,
            JobTitle = job.Title,
            JobDescription = job.Description,
            TotalPositions = job.TotalPositions,
            FilledPositions = job.FilledPositions,
            AvailablePositions = job.AvailablePositions,
            IsActive = job.IsActive,
            HasUserApplied = userApplication != null,
            UserApplicationId = userApplication?.Id,
            UserApplicationStatus = userApplication?.Status
        };
    }

    private async Task<JobApplicationDetail> MapToApplicationDetailAsync(JobApplication application)
    {
        var crewName = await GetCrewNameAsync(application.CrewJob.CrewContentId);
        var crewUrl = await GetCrewUrlAsync(application.CrewJob.CrewContentId);

        string? reviewerName = null;
        if (application.ReviewedByMemberId.HasValue)
        {
            var reviewer = _memberService.GetById(application.ReviewedByMemberId.Value);
            if (reviewer != null)
            {
                var firstName = reviewer.GetValue<string>("firstName") ?? "";
                var lastName = reviewer.GetValue<string>("lastName") ?? "";
                reviewerName = $"{firstName} {lastName}".Trim();
            }
        }

        // Get member details
        var member = _memberService.GetByEmail(application.MemberEmail);
        string? phone = null;
        DateTime? birthdate = null;
        int? age = null;

        if (member != null)
        {
            phone = member.GetValue<string>("phone");
            birthdate = member.GetValue<DateTime?>("birthdate");
            if (birthdate.HasValue)
            {
                var today = DateTime.Today;
                age = today.Year - birthdate.Value.Year;
                if (birthdate.Value.Date > today.AddYears(-age.Value))
                    age--;
            }
        }

        return new JobApplicationDetail
        {
            ApplicationId = application.Id,
            JobId = application.CrewJobId,
            JobTitle = application.CrewJob.Title,
            CrewContentId = application.CrewJob.CrewContentId,
            CrewKey = application.CrewJob.CrewKey,
            CrewName = crewName,
            CrewUrl = crewUrl,
            MemberId = application.MemberId,
            MemberKey = application.MemberKey,
            MemberEmail = application.MemberEmail,
            MemberName = application.MemberName,
            MemberPhone = phone,
            MemberBirthdate = birthdate,
            MemberAge = age,
            Status = application.Status,
            ApplicationMessage = application.ApplicationMessage,
            SubmittedDate = application.SubmittedDate,
            ReviewedDate = application.ReviewedDate,
            ReviewedByMemberId = application.ReviewedByMemberId,
            ReviewedByName = reviewerName,
            TicketLink = application.TicketLink,
            AdminNotes = application.AdminNotes
        };
    }

    private async Task<string> GetCrewNameAsync(int crewContentId)
    {
        using var umbracoContext = _umbracoContextFactory.EnsureUmbracoContext();
        var content = umbracoContext.UmbracoContext.Content?.GetById(crewContentId);
        return content?.Name ?? "Unknown Crew";
    }

    private async Task<string> GetCrewUrlAsync(int crewContentId)
    {
        using var umbracoContext = _umbracoContextFactory.EnsureUmbracoContext();
        var content = umbracoContext.UmbracoContext.Content?.GetById(crewContentId);
        return content?.Url() ?? "#";
    }

    private bool IsAdmin(int memberId)
    {
        var memberGroups = _memberService.GetAllRoles(memberId);
        var adminGroup = _memberGroupService.GetById(AdminGroupKey);
        return adminGroup != null && memberGroups.Contains(adminGroup.Name);
    }

    private bool IsScheduler(int memberId)
    {
        var memberGroups = _memberService.GetAllRoles(memberId);
        var schedulerGroup = _memberGroupService.GetById(SchedulerGroupKey);
        return schedulerGroup != null && memberGroups.Contains(schedulerGroup.Name);
    }

    private List<int> GetCrewsForSupervisor(Guid memberKey)
    {
        var supervisorCrewIds = new List<int>();

        // Get all crew pages
        var allCrews = _contentService.GetPagedOfType(
            contentTypeId: _contentService.GetContentType("bbvCrewPage")?.Id ?? 0,
            pageIndex: 0,
            pageSize: 10000,
            out long totalRecords,
            null,
            Umbraco.Cms.Core.Persistence.Querying.Ordering.By("Name"));

        foreach (var crew in allCrews)
        {
            // Check if this member is a supervisor or schedule supervisor
            var supervisors = crew.GetValue<string>("supervisors");
            var scheduleSupervisor = crew.GetValue<string>("scheduleSupervisor");

            var memberUdi = $"umb://member/{memberKey:D}";

            if (!string.IsNullOrEmpty(supervisors) && supervisors.Contains(memberUdi, StringComparison.OrdinalIgnoreCase))
            {
                supervisorCrewIds.Add(crew.Id);
            }
            else if (!string.IsNullOrEmpty(scheduleSupervisor) && scheduleSupervisor.Contains(memberUdi, StringComparison.OrdinalIgnoreCase))
            {
                supervisorCrewIds.Add(crew.Id);
            }
        }

        return supervisorCrewIds;
    }

    #endregion
}
