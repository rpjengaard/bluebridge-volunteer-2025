namespace Code.Services;

public interface IEmailLogService
{
    Task AddLogAsync(EmailLogEntry entry);
    Task<List<EmailLogEntry>> GetLogsAsync(int maxEntries = 50);
}

public class EmailLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime SentAt { get; set; } = DateTime.Now;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int SentCount { get; set; }
    public int ErrorCount { get; set; }
    public List<EmailLogRecipient> Recipients { get; set; } = new();
}

public class EmailLogRecipient
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
