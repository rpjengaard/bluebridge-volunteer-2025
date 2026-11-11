using System.Text.RegularExpressions;
using Code.Migrations;
using Markdig;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Code.Services;

// [CHANGE: crew message replies] Related: Code/Migrations/AddCrewMessageTableMigration.cs, Code/Services/ICrewMessageService.cs, Code/Services/IMemberEmailService.cs, Code/Services/MemberEmailService.cs, Web/Controllers/CrewSurfaceController.cs, Web/Views/Crew.cshtml
public class CrewMessageService : ICrewMessageService
{
    private readonly IScopeProvider _scopeProvider;
    private readonly IMemberService _memberService;
    private readonly IContentService _contentService;
    private readonly ILogger<CrewMessageService> _logger;
    private readonly MarkdownPipeline _markdownPipeline;

    public const int MaxReplyLength = 1000;

    // Allowed HTML tags for sanitization
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "strong", "em", "a", "ul", "ol", "li", "br", "b", "i"
    };

    public CrewMessageService(IScopeProvider scopeProvider, IMemberService memberService, IContentService contentService, ILogger<CrewMessageService> logger)
    {
        _scopeProvider = scopeProvider;
        _memberService = memberService;
        _contentService = contentService;
        _logger = logger;
        _markdownPipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .Build();
    }

    public Task<List<CrewMessageData>> GetMessagesAsync(int crewId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var rows = db.Fetch<CrewMessageSchema>(
            "SELECT * FROM BbvCrewMessage WHERE CrewId = @0 ORDER BY CreatedUtc DESC", crewId);

        var messages = rows.Select(r => new CrewMessageData
        {
            Id = r.Id,
            CrewId = r.CrewId,
            AuthorEmail = r.AuthorEmail,
            AuthorName = r.AuthorName,
            MessageText = r.MessageText,
            MessageHtml = RenderMarkdown(r.MessageText),
            CreatedUtc = r.CreatedUtc,
            RepliesClosed = r.RepliesClosed
        }).ToList();

        if (messages.Count > 0)
        {
            var replyRows = db.Fetch<CrewMessageReplySchema>(
                @"SELECT r.* FROM BbvCrewMessageReply r
                  INNER JOIN BbvCrewMessage m ON m.Id = r.MessageId
                  WHERE m.CrewId = @0
                  ORDER BY r.CreatedUtc ASC", crewId);

            var repliesByMessage = replyRows
                .GroupBy(r => r.MessageId)
                .ToDictionary(g => g.Key, g => g.Select(ToReplyData).ToList());

            foreach (var message in messages)
            {
                if (repliesByMessage.TryGetValue(message.Id, out var replies))
                    message.Replies = replies;
            }
        }

        return Task.FromResult(messages);
    }

    public Task<CrewMessageData> PostMessageAsync(int crewId, string authorEmail, string authorName, string messageText, bool allowReplies = true)
    {
        if (string.IsNullOrWhiteSpace(messageText))
            throw new ArgumentException("Beskedtekst må ikke være tom.");

        if (messageText.Length > 4000)
            throw new ArgumentException("Beskeden er for lang (max 4000 tegn).");

        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var row = new CrewMessageSchema
        {
            CrewId = crewId,
            AuthorEmail = authorEmail,
            AuthorName = authorName,
            MessageText = messageText,
            CreatedUtc = DateTime.UtcNow,
            RepliesClosed = !allowReplies
        };

        db.Insert(row);

        var result = new CrewMessageData
        {
            Id = row.Id,
            CrewId = row.CrewId,
            AuthorEmail = row.AuthorEmail,
            AuthorName = row.AuthorName,
            MessageText = row.MessageText,
            MessageHtml = RenderMarkdown(row.MessageText),
            CreatedUtc = row.CreatedUtc,
            RepliesClosed = row.RepliesClosed
        };

        return Task.FromResult(result);
    }

    public Task<bool> DeleteMessageAsync(int messageId, int crewId, string requestingEmail, bool isAdminOrScheduler)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var message = db.SingleOrDefaultById<CrewMessageSchema>(messageId);
        // Crew check: permissions are evaluated per crew, so the message must belong to that crew
        if (message == null || message.CrewId != crewId)
            return Task.FromResult(false);

        // Only allow deletion by author or admin/scheduler
        if (!isAdminOrScheduler &&
            !string.Equals(message.AuthorEmail, requestingEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        // Replies are removed by the FK's ON DELETE CASCADE
        db.Delete<CrewMessageSchema>(messageId);
        return Task.FromResult(true);
    }

    public Task<CrewMessageReplyResult> PostReplyAsync(int messageId, int crewId, string authorEmail, string authorName, string replyText)
    {
        if (string.IsNullOrWhiteSpace(replyText))
            throw new ArgumentException("Svaret må ikke være tomt.");

        if (replyText.Length > MaxReplyLength)
            throw new ArgumentException($"Svaret er for langt (max {MaxReplyLength} tegn).");

        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var message = db.SingleOrDefaultById<CrewMessageSchema>(messageId);
        if (message == null || message.CrewId != crewId)
            throw new InvalidOperationException("Beskeden findes ikke.");

        if (message.RepliesClosed)
            throw new InvalidOperationException("Beskeden er lukket for svar.");

        var row = new CrewMessageReplySchema
        {
            MessageId = messageId,
            AuthorEmail = authorEmail,
            AuthorName = authorName,
            ReplyText = replyText,
            CreatedUtc = DateTime.UtcNow
        };

        db.Insert(row);

        return Task.FromResult(new CrewMessageReplyResult
        {
            Reply = ToReplyData(row),
            MessageAuthorEmail = message.AuthorEmail
        });
    }

    public Task<bool> DeleteReplyAsync(int replyId, int crewId, string requestingEmail, bool isAdminOrScheduler)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var reply = db.SingleOrDefaultById<CrewMessageReplySchema>(replyId);
        if (reply == null)
            return Task.FromResult(false);

        var message = db.SingleOrDefaultById<CrewMessageSchema>(reply.MessageId);
        if (message == null || message.CrewId != crewId)
            return Task.FromResult(false);

        if (!isAdminOrScheduler &&
            !string.Equals(reply.AuthorEmail, requestingEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        db.Delete<CrewMessageReplySchema>(replyId);
        return Task.FromResult(true);
    }

    public Task<bool> SetRepliesClosedAsync(int messageId, int crewId, bool closed)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var updated = db.Execute(
            "UPDATE BbvCrewMessage SET RepliesClosed = @0 WHERE Id = @1 AND CrewId = @2",
            closed, messageId, crewId);

        return Task.FromResult(updated > 0);
    }

    public Task<bool> IsMemberAssignedToCrewAsync(string memberEmail, int crewId)
    {
        var member = _memberService.GetByEmail(memberEmail);
        if (member == null)
            return Task.FromResult(false);

        if (member.GetValue<bool>("rejected") || member.GetValue<bool>("cancelation"))
            return Task.FromResult(false);

        var crewContent = _contentService.GetById(crewId);
        if (crewContent == null)
            return Task.FromResult(false);

        return Task.FromResult(IsCrewInUdiList(member.GetValue<string>("crews"), crewContent.Key));
    }

    public Task<CrewMessageRecipient?> GetRecipientByEmailAsync(string email)
    {
        var member = _memberService.GetByEmail(email);
        if (member == null || string.IsNullOrEmpty(member.Email))
            return Task.FromResult<CrewMessageRecipient?>(null);

        return Task.FromResult<CrewMessageRecipient?>(new CrewMessageRecipient
        {
            Email = member.Email,
            FullName = GetFullName(member)
        });
    }

    private CrewMessageReplyData ToReplyData(CrewMessageReplySchema r) => new()
    {
        Id = r.Id,
        MessageId = r.MessageId,
        AuthorEmail = r.AuthorEmail,
        AuthorName = r.AuthorName,
        ReplyText = r.ReplyText,
        ReplyHtml = RenderMarkdown(r.ReplyText),
        CreatedUtc = r.CreatedUtc
    };

    private static bool IsCrewInUdiList(string? crewsValue, Guid crewGuid)
    {
        if (string.IsNullOrWhiteSpace(crewsValue))
            return false;

        foreach (var udiPart in crewsValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = udiPart.Trim();
            if (trimmed.StartsWith("umb://document/", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(trimmed["umb://document/".Length..], out var contentGuid)
                && contentGuid == crewGuid)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetFullName(Umbraco.Cms.Core.Models.IMember member)
    {
        var firstName = member.GetValue<string>("firstName") ?? string.Empty;
        var lastName = member.GetValue<string>("lastName") ?? string.Empty;
        var fullName = $"{firstName} {lastName}".Trim();
        return string.IsNullOrEmpty(fullName) ? member.Name ?? member.Email ?? "Unknown" : fullName;
    }

    public Task<List<CrewMessageRecipient>> GetCrewMemberRecipientsAsync(int crewId)
    {
        var recipients = new List<CrewMessageRecipient>();

        var crewContent = _contentService.GetById(crewId);
        if (crewContent == null)
            return Task.FromResult(recipients);

        var crewGuid = crewContent.Key;
        var allMembers = _memberService.GetAllMembers();

        foreach (var member in allMembers)
        {
            // Check if this member is assigned to the crew by matching the GUID in UDI references
            if (!IsCrewInUdiList(member.GetValue<string>("crews"), crewGuid))
                continue;

            var fullName = GetFullName(member);

            if (!string.IsNullOrEmpty(member.Email))
            {
                recipients.Add(new CrewMessageRecipient
                {
                    Email = member.Email,
                    FullName = fullName
                });
            }
        }

        return Task.FromResult(recipients);
    }

    private string RenderMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        var html = Markdown.ToHtml(markdown, _markdownPipeline);
        return SanitizeHtml(html);
    }

    private static string SanitizeHtml(string html)
    {
        // Remove all tags except allowed ones
        // Match opening tags, closing tags, and self-closing tags
        var sanitized = Regex.Replace(html, @"<(/?)(\w+)([^>]*)(/?)>", match =>
        {
            var isClosing = match.Groups[1].Value;
            var tagName = match.Groups[2].Value;
            var attributes = match.Groups[3].Value;
            var selfClosing = match.Groups[4].Value;

            if (!AllowedTags.Contains(tagName))
                return string.Empty;

            // For anchor tags, only allow href and add security attributes for external links
            if (tagName.Equals("a", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(isClosing))
            {
                var hrefMatch = Regex.Match(attributes, @"href\s*=\s*""([^""]*)""");
                if (hrefMatch.Success)
                {
                    var href = hrefMatch.Groups[1].Value;
                    var isInternal = href.StartsWith('/') && !href.StartsWith("//");
                    if (isInternal)
                    {
                        return $@"<a href=""{href}"">";
                    }
                    return $@"<a href=""{href}"" rel=""noopener noreferrer"" target=""_blank"">";
                }
                return "<a>";
            }

            // For closing tags or other allowed tags, return clean tag
            if (!string.IsNullOrEmpty(isClosing))
                return $"</{tagName}>";

            return $"<{tagName}>";
        });

        return sanitized;
    }
}
