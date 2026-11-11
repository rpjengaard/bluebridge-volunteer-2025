namespace Code.Services;

// [CHANGE: crew message replies] Related: Code/Migrations/AddCrewMessageTableMigration.cs, Code/Services/CrewMessageService.cs, Code/Services/IMemberEmailService.cs, Code/Services/MemberEmailService.cs, Web/Controllers/CrewSurfaceController.cs, Web/Views/Crew.cshtml
public interface ICrewMessageService
{
    Task<List<CrewMessageData>> GetMessagesAsync(int crewId);
    Task<CrewMessageData> PostMessageAsync(int crewId, string authorEmail, string authorName, string messageText, bool allowReplies = true);
    Task<bool> DeleteMessageAsync(int messageId, int crewId, string requestingEmail, bool isAdminOrScheduler);
    Task<List<CrewMessageRecipient>> GetCrewMemberRecipientsAsync(int crewId);

    /// <summary>Adds a reply to a message. Throws ArgumentException on invalid text,
    /// InvalidOperationException if the message is missing, belongs to another crew, or is closed for replies.</summary>
    Task<CrewMessageReplyResult> PostReplyAsync(int messageId, int crewId, string authorEmail, string authorName, string replyText);
    Task<bool> DeleteReplyAsync(int replyId, int crewId, string requestingEmail, bool isAdminOrScheduler);
    Task<bool> SetRepliesClosedAsync(int messageId, int crewId, bool closed);

    /// <summary>True if the member is assigned to the crew and not rejected/cancelled.</summary>
    Task<bool> IsMemberAssignedToCrewAsync(string memberEmail, int crewId);

    /// <summary>Looks up an existing member by email; null if no longer a member.</summary>
    Task<CrewMessageRecipient?> GetRecipientByEmailAsync(string email);
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
    public bool RepliesClosed { get; set; }
    public List<CrewMessageReplyData> Replies { get; set; } = new();
}

public class CrewMessageReplyData
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public string AuthorEmail { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string ReplyText { get; set; } = string.Empty;
    public string ReplyHtml { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}

public class CrewMessageReplyResult
{
    public CrewMessageReplyData Reply { get; set; } = new();
    public string MessageAuthorEmail { get; set; } = string.Empty;
}

public class CrewMessageRecipient
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}
