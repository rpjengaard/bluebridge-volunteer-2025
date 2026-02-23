using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Code.Services;

public class EmailLogService : IEmailLogService
{
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<EmailLogService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public EmailLogService(string dataDirectory, ILogger<EmailLogService> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(dataDirectory);
        _logFilePath = Path.Combine(dataDirectory, "email-dashboard-logs.json");
    }

    public async Task AddLogAsync(EmailLogEntry entry)
    {
        await _lock.WaitAsync();
        try
        {
            var logs = await ReadLogsInternalAsync();
            logs.Insert(0, entry); // newest first

            if (logs.Count > 200)
                logs = logs.Take(200).ToList();

            var json = JsonSerializer.Serialize(logs, JsonOptions);
            await File.WriteAllTextAsync(_logFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write email log entry");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<EmailLogEntry>> GetLogsAsync(int maxEntries = 50)
    {
        await _lock.WaitAsync();
        try
        {
            var logs = await ReadLogsInternalAsync();
            return logs.Take(maxEntries).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<EmailLogEntry>> ReadLogsInternalAsync()
    {
        if (!File.Exists(_logFilePath))
            return new List<EmailLogEntry>();

        try
        {
            var json = await File.ReadAllTextAsync(_logFilePath);
            return JsonSerializer.Deserialize<List<EmailLogEntry>>(json, JsonOptions) ?? new List<EmailLogEntry>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read email log file, returning empty list");
            return new List<EmailLogEntry>();
        }
    }
}
