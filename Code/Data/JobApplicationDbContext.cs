using Code.Entities;
using Microsoft.EntityFrameworkCore;

namespace Code.Data;

/// <summary>
/// Database context for job applications
/// </summary>
public class JobApplicationDbContext : DbContext
{
    public JobApplicationDbContext(DbContextOptions<JobApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<CrewJob> CrewJobs { get; set; } = null!;
    public DbSet<JobApplication> JobApplications { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure CrewJob entity
        modelBuilder.Entity<CrewJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CrewKey);
            entity.HasIndex(e => e.CrewContentId);
            entity.HasIndex(e => e.IsActive);

            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(2000);

            // Configure one-to-many relationship
            entity.HasMany(e => e.Applications)
                  .WithOne(e => e.CrewJob)
                  .HasForeignKey(e => e.CrewJobId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure JobApplication entity
        modelBuilder.Entity<JobApplication>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MemberKey);
            entity.HasIndex(e => e.MemberEmail);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.SubmittedDate);
            entity.HasIndex(e => new { e.CrewJobId, e.MemberKey }).IsUnique();

            entity.Property(e => e.MemberEmail).IsRequired().HasMaxLength(255);
            entity.Property(e => e.MemberName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ApplicationMessage).HasMaxLength(1000);
            entity.Property(e => e.TicketLink).HasMaxLength(500);
            entity.Property(e => e.AdminNotes).HasMaxLength(1000);
        });
    }
}
