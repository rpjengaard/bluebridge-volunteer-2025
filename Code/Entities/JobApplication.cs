using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Code.Entities;

/// <summary>
/// Represents a member's application for a crew job
/// </summary>
[Table("JobApplications")]
public class JobApplication
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to CrewJob
    /// </summary>
    [Required]
    public int CrewJobId { get; set; }

    /// <summary>
    /// Navigation property to the job
    /// </summary>
    [ForeignKey(nameof(CrewJobId))]
    public virtual CrewJob CrewJob { get; set; } = null!;

    /// <summary>
    /// The Umbraco member ID who applied
    /// </summary>
    [Required]
    public int MemberId { get; set; }

    /// <summary>
    /// The Umbraco member key (GUID) who applied
    /// </summary>
    [Required]
    public Guid MemberKey { get; set; }

    /// <summary>
    /// Member's email address (cached for quick lookup)
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string MemberEmail { get; set; } = string.Empty;

    /// <summary>
    /// Member's full name (cached for quick lookup)
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string MemberName { get; set; } = string.Empty;

    /// <summary>
    /// Application status
    /// </summary>
    [Required]
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;

    /// <summary>
    /// Optional message from the applicant
    /// </summary>
    [MaxLength(1000)]
    public string? ApplicationMessage { get; set; }

    /// <summary>
    /// When the application was submitted
    /// </summary>
    public DateTime SubmittedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the application was reviewed (accepted/rejected)
    /// </summary>
    public DateTime? ReviewedDate { get; set; }

    /// <summary>
    /// Who reviewed the application (admin/scheduler member ID)
    /// </summary>
    public int? ReviewedByMemberId { get; set; }

    /// <summary>
    /// Link to ticket purchase page (sent via email when accepted)
    /// </summary>
    [MaxLength(500)]
    public string? TicketLink { get; set; }

    /// <summary>
    /// Admin notes about this application
    /// </summary>
    [MaxLength(1000)]
    public string? AdminNotes { get; set; }
}

/// <summary>
/// Application status workflow
/// </summary>
public enum ApplicationStatus
{
    /// <summary>
    /// Application submitted, awaiting review
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Application accepted by admin/scheduler
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// Application rejected by admin/scheduler
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// Application withdrawn by applicant
    /// </summary>
    Withdrawn = 3
}
