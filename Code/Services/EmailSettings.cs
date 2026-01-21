namespace Code.Services;

public class EmailSettings
{
    // Provider: "Smtp" or "Postmark"
    public string Provider { get; set; } = "Smtp";

    // Postmark settings
    public string? PostmarkServerToken { get; set; }

    // SMTP settings
    public string SmtpHost { get; set; } = "localhost";
    public int SmtpPort { get; set; } = 25;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool EnableSsl { get; set; } = false;

    // Common settings
    public string FromEmail { get; set; } = "noreply@bluebridge.dk";
    public string FromName { get; set; } = "Blue Bridge Frivillig";
}
