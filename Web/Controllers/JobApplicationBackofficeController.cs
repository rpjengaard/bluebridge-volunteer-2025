using Asp.Versioning;
using Code.Entities;
using Code.Services;
using Code.Services.DTOs;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Security;

namespace Web.Controllers;

/// <summary>
/// Backoffice API controller for managing job applications
/// </summary>
[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("jobapplications")]
[ApiExplorerSettings(GroupName = "Job Applications API")]
public class JobApplicationBackofficeController : ManagementApiControllerBase
{
    private readonly IJobService _jobService;
    private readonly IMemberManager _memberManager;

    public JobApplicationBackofficeController(
        IJobService jobService,
        IMemberManager memberManager)
    {
        _jobService = jobService;
        _memberManager = memberManager;
    }

    /// <summary>
    /// Get all applications for review (admin/scheduler)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetApplicationsForReview()
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
        {
            return Unauthorized();
        }

        var data = await _jobService.GetApplicationsForReviewAsync(currentMember.Email);
        return Ok(data);
    }

    /// <summary>
    /// Get application details
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetApplicationDetail(int applicationId)
    {
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
    [HttpPost]
    public async Task<IActionResult> ReviewApplication([FromBody] ReviewApplicationRequest request)
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
        {
            return Unauthorized();
        }

        var result = await _jobService.ReviewApplicationAsync(currentMember.Email, request);

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(result);
    }

    /// <summary>
    /// Get applications for a specific job
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetApplicationsForJob(int jobId)
    {
        var applications = await _jobService.GetApplicationsForJobAsync(jobId);
        return Ok(applications);
    }

    /// <summary>
    /// Get applications for a specific crew
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetApplicationsForCrew(int crewContentId)
    {
        var applications = await _jobService.GetApplicationsForCrewAsync(crewContentId);
        return Ok(applications);
    }

    /// <summary>
    /// Get pending application count
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPendingCount()
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        var count = await _jobService.GetPendingApplicationCountAsync(currentMember?.Email);
        return Ok(new { count });
    }

    /// <summary>
    /// Create a new job for a crew
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateJob([FromBody] CreateJobRequest request)
    {
        var jobId = await _jobService.CreateJobAsync(request);
        return Ok(new { jobId });
    }

    /// <summary>
    /// Update an existing job
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> UpdateJob([FromBody] UpdateJobRequest request)
    {
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
    [HttpPost]
    public async Task<IActionResult> DeleteJob(int jobId)
    {
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
    [HttpGet]
    public async Task<IActionResult> GetJobsForCrew(int crewContentId)
    {
        var jobs = await _jobService.GetJobsForCrewAsync(crewContentId);
        return Ok(jobs);
    }

    /// <summary>
    /// Get job details
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetJobDetail(int jobId)
    {
        var job = await _jobService.GetJobByIdAsync(jobId);
        if (job == null)
        {
            return NotFound();
        }

        return Ok(job);
    }

    /// <summary>
    /// Get statistics
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetStatistics()
    {
        var totalAvailable = await _jobService.GetTotalAvailablePositionsAsync();
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        var pendingCount = await _jobService.GetPendingApplicationCountAsync(currentMember?.Email);

        return Ok(new
        {
            totalAvailablePositions = totalAvailable,
            pendingApplications = pendingCount
        });
    }
}
