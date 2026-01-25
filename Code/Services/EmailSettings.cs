namespace Code.Services;

public class EmailSettings
{
    public string SmtpHost { get; set; } = "localhost";
    public int SmtpPort { get; set; } = 25;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool EnableSsl { get; set; } = false;
    public string FromEmail { get; set; } = "noreply@bluebridge.dk";
    public string FromName { get; set; } = "Blue Bridge Frivillig";

    // Postmark broadcast stream ID for bulk emails (e.g., "broadcast")
    // When set, invitation emails will use this stream for better deliverability
    public string? BroadcastStreamId { get; set; }
}
