using Code.Services;
using Code.Services.DTOs;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace Web.Controllers;

/// <summary>
/// API controller for managing job applications (admin/scheduler only)
/// </summary>
[ApiController]
[Route("api/jobs")]
public class JobApiController : ControllerBase
{
    private readonly IJobService _jobService;
    private readonly IMemberManager _memberManager;
    private readonly IMemberService _memberService;
    private readonly IMemberGroupService _memberGroupService;

    private static readonly Guid AdminGroupKey = Guid.Parse("99e1edbb-8181-421d-a74b-e66a2f1e1148");
    private static readonly Guid SchedulerGroupKey = Guid.Parse("e6eef645-b13b-4edb-880b-7b3cdf5b6816");

    public JobApiController(
        IJobService jobService,
        IMemberManager memberManager,
        IMemberService memberService,
        IMemberGroupService memberGroupService)
    {
        _jobService = jobService;
        _memberManager = memberManager;
        _memberService = memberService;
        _memberGroupService = memberGroupService;
    }

    #region Authorization Helpers

    private async Task<(bool isAuthorized, bool isAdmin, bool isScheduler, string? email)> CheckAdminOrSchedulerAsync()
    {
        if (!_memberManager.IsLoggedIn())
        {
            return (false, false, false, null);
        }

        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
        {
            return (false, false, false, null);
        }

        var member = _memberService.GetByEmail(currentMember.Email);
        if (member == null)
        {
            return (false, false, false, null);
        }

        var memberGroups = _memberService.GetAllRoles(member.Id);
        var adminGroup = _memberGroupService.GetById(AdminGroupKey);
        var schedulerGroup = _memberGroupService.GetById(SchedulerGroupKey);

        var isAdmin = adminGroup != null && memberGroups.Contains(adminGroup.Name);
        var isScheduler = schedulerGroup != null && memberGroups.Contains(schedulerGroup.Name);

        return (isAdmin || isScheduler, isAdmin, isScheduler, currentMember.Email);
    }

    #endregion

    #region Applications

    /// <summary>
    /// Get all applications for review (admin/scheduler)
    /// </summary>
    [HttpGet("applications")]
    public async Task<IActionResult> GetApplicationsForReview()
    {
        var (isAuthorized, _, _, email) = await CheckAdminOrSchedulerAsync();
        if (!isAuthorized || email == null)
        {
            return Unauthorized(new { error = "You must be an admin or scheduler to access this endpoint" });
        }

        var data = await _jobService.GetApplicationsForReviewAsync(email);
        return Ok(data);
    }

    /// <summary>
    /// Get application details
    /// </summary>
    [HttpGet("applications/{applicationId:int}")]
    public async Task<IActionResult> GetApplicationDetail(int applicationId)
    {
        var (isAuthorized, _, _, _) = await CheckAdminOrSchedulerAsync();
        if (!isAuthorized)
        {
            return Unauthorized(new { error = "You must be an admin or scheduler to access this endpoint" });
        }

        var application = await _jobService.GetApplicationDetailAsync(applicationId);
        if (application == null)
        {
            return NotFound();
        }

        return Ok(application);
    }

    /// <summary>
    /// Review an application (accept/reject)
    /// </summary>
    [HttpPost("applications/review")]
    public async Task<IActionResult> ReviewApplication([FromBody] ReviewApplicationRequest request)
    {
        var (isAuthorized, _, _, email) = await CheckAdminOrSchedulerAsync();
        if (!isAuthorized || email == null)
        {
            return Unauthorized(new { error = "You must be an admin or scheduler to access this endpoint" });
        }

        var result = await _jobService.ReviewApplicationAsync(email, request);

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(result);
    }

    /// <summary>
    /// Get applications for a specific job
    /// </summary>
    [HttpGet("jobs/{jobId:int}/applications")]
    public async Task<IActionResult> GetApplicationsForJob(int jobId)
    {
        var (isAuthorized, _, _, _) = await CheckAdminOrSchedulerAsync();
        if (!isAuthorized)
        {
            return Unauthorized(new { error = "You must be an admin or scheduler to access this endpoint" });
        }

        var applications = await _jobService.GetApplicationsForJobAsync(jobId);
        return Ok(applications);
    }

    /// <summary>
    /// Get applications for a specific crew
    /// </summary>
    [HttpGet("crews/{crewKey:guid}/applications")]
    public async Task<IActionResult> GetApplicationsForCrew(Guid crewKey)
    {
        var (isAuthorized, _, _, _) = await CheckAdminOrSchedulerAsync();
        if (!isAuthorized)
        {
            return Unauthorized(new { error = "You must be an admin or scheduler to access this endpoint" });
        }

        var applications = await _jobService.GetApplicationsForCrewAsync(crewKey);
        return Ok(applications);
    }

    /// <summary>
    /// Get pending application count
    /// </summary>
    [HttpGet("applications/pending/count")]
    public async Task<IActionResult> GetPendingCount()
    {
        var (isAuthorized, _, _, email) = await CheckAdminOrSchedulerAsync();
        if (!isAuthorized)
        {
            return Unauthorized(new { error = "You must be an admin or scheduler to access this endpoint" });
        }

        var count = await _jobService.GetPendingApplicationCountAsync(email);
        return Ok(new { count });
    }

    #endregion

    #region Jobs

    /// <summary>
    /// Create a new job for a crew
    /// </summary>
    [HttpPost("jobs")]
    public async Task<IActionResult> CreateJob([FromBody] CreateJobRequest request)
    {
        var (isAuthorized, _, _, _) = await CheckAdminOrSchedulerAsync();
        if (!isAuthorized)
        {
            return Unauthorized(new { error = "You must be an admin or scheduler to access this endpoint" });
        }

        var jobId = await _jobService.CreateJobAsync(request);
        return Ok(new { jobId });
    }

    /// <summary>
    /// Update an existing job
    /// </summary>
    [HttpPut("jobs/{jobId:int}")]
    public async Task<IActionResult> UpdateJob(int jobId, [FromBody] UpdateJobRequest request)
    {
        var (isAuthorized, _, _, _) = await CheckAdminOrSchedulerAsync();
        if (!isAuthorized)
        {
            return Unauthorized(new { error = "You must be an admin or scheduler to access this endpoint" });
        }

        if (jobId != request.JobId)
        {
            return BadRequest(new { error = "Job ID mismatch" });
        }

        var success = await _jobService.UpdateJobAsync(request);
        if (!success)
        {
            return NotFound();
        }

        return Ok();
    }

    /// <summary>
    /// Delete a job
    /// </summary>
    [HttpDelete("jobs/{jobId:int}")]
    public async Task<IActionResult> DeleteJob(int jobId)
    {
        var (isAuthorized, _, _, _) = await CheckAdminOrSchedulerAsync();
        if (!isAuthorized)
        {
            return Unauthorized(new { error = "You must be an admin or scheduler to access this endpoint" });
        }

        var success = await _jobService.DeleteJobAsync(jobId);
        if (!success)
        {
            return NotFound();
        }

        return Ok();
    }

    /// <summary>
    /// Get all jobs for a crew
    /// </summary>
    [HttpGet("crews/{crewKey:guid}/jobs")]
    public async Task<IActionResult> GetJobsForCrew(Guid crewKey)
    {
        var (isAuthorized, _, _, email) = await CheckAdminOrSchedulerAsync();
        if (!isAuthorized)
        {
            return Unauthorized(new { error = "You must be an admin or scheduler to access this endpoint" });
        }

        var jobs = await _jobService.GetJobsForCrewAsync(crewKey, email);
        return Ok(jobs);
    }

    /// <summary>
    /// Get job details
    /// </summary>
    [HttpGet("jobs/{jobId:int}")]
    public async Task<IActionResult> GetJobDetail(int jobId)
    {
        var (isAuthorized, _, _, email) = await CheckAdminOrSchedulerAsync();
        if (!isAuthorized)
        {
            return Unauthorized(new { error = "You must be an admin or scheduler to access this endpoint" });
        }

        var job = await _jobService.GetJobByIdAsync(jobId, email);
        if (job == null)
        {
            return NotFound();
        }

        return Ok(job);
    }

    /// <summary>
    /// Get statistics
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics()
    {
        var (isAuthorized, _, _, email) = await CheckAdminOrSchedulerAsync();
        if (!isAuthorized)
        {
            return Unauthorized(new { error = "You must be an admin or scheduler to access this endpoint" });
        }

        var totalAvailable = await _jobService.GetTotalAvailablePositionsAsync();
        var pendingCount = await _jobService.GetPendingApplicationCountAsync(email);

        return Ok(new
        {
            totalAvailablePositions = totalAvailable,
            pendingApplications = pendingCount
        });
    }

    #endregion
}
