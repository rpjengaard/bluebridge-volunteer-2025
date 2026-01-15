using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Code.Entities;

/// <summary>
/// Represents an available job position for a crew
/// </summary>
[Table("CrewJobs")]
public class CrewJob
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// The Umbraco content key (GUID) of the crew page
    /// </summary>
    [Required]
    public Guid CrewKey { get; set; }

    /// <summary>
    /// Job title/position name
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Detailed job description
    /// </summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Total number of positions available for this job
    /// </summary>
    [Required]
    public int TotalPositions { get; set; }

    /// <summary>
    /// Number of positions already filled
    /// </summary>
    public int FilledPositions { get; set; } = 0;

    /// <summary>
    /// Whether this job is currently accepting applications
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When this job was created
    /// </summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this job was last updated
    /// </summary>
    public DateTime? UpdatedDate { get; set; }

    /// <summary>
    /// Calculated property: Available positions remaining
    /// </summary>
    [NotMapped]
    public int AvailablePositions => Math.Max(0, TotalPositions - FilledPositions);

    /// <summary>
    /// Navigation property: Applications for this job
    /// </summary>
    public virtual ICollection<JobApplication> Applications { get; set; } = new List<JobApplication>();
}
