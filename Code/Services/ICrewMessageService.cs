namespace Code.Services;

public interface ICrewMessageService
{
    Task<List<CrewMessageData>> GetMessagesAsync(int crewId);
    Task<CrewMessageData> PostMessageAsync(int crewId, string authorEmail, string authorName, string messageText);
    Task<bool> DeleteMessageAsync(int messageId, string requestingEmail, bool isAdminOrScheduler);
    Task<List<CrewMessageRecipient>> GetCrewMemberRecipientsAsync(int crewId);
}

public class CrewMessageData
{
    public int Id { get; set; }
    public int CrewId { get; set; }
    public string AuthorEmail { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
    public string MessageHtml { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}

public class CrewMessageRecipient
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}
