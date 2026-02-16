using System.Text.RegularExpressions;
using Code.Migrations;
using Markdig;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Code.Services;

public class CrewMessageService : ICrewMessageService
{
    private readonly IScopeProvider _scopeProvider;
    private readonly IMemberService _memberService;
    private readonly IContentService _contentService;
    private readonly ILogger<CrewMessageService> _logger;
    private readonly MarkdownPipeline _markdownPipeline;

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
            CreatedUtc = r.CreatedUtc
        }).ToList();

        return Task.FromResult(messages);
    }

    public Task<CrewMessageData> PostMessageAsync(int crewId, string authorEmail, string authorName, string messageText)
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
            CreatedUtc = DateTime.UtcNow
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
            CreatedUtc = row.CreatedUtc
        };

        return Task.FromResult(result);
    }

    public Task<bool> DeleteMessageAsync(int messageId, string requestingEmail, bool isAdminOrScheduler)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var message = db.SingleOrDefaultById<CrewMessageSchema>(messageId);
        if (message == null)
            return Task.FromResult(false);

        // Only allow deletion by author or admin/scheduler
        if (!isAdminOrScheduler &&
            !string.Equals(message.AuthorEmail, requestingEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        db.Delete<CrewMessageSchema>(messageId);
        return Task.FromResult(true);
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
            var crewsValue = member.GetValue<string>("crews");
            if (string.IsNullOrWhiteSpace(crewsValue))
                continue;

            // Check if this member is assigned to the crew by matching the GUID in UDI references
            var udiParts = crewsValue.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var isAssigned = false;
            foreach (var udiPart in udiParts)
            {
                var trimmed = udiPart.Trim();
                if (trimmed.StartsWith("umb://document/", StringComparison.OrdinalIgnoreCase))
                {
                    var guidPart = trimmed["umb://document/".Length..];
                    if (Guid.TryParse(guidPart, out var contentGuid) && contentGuid == crewGuid)
                    {
                        isAssigned = true;
                        break;
                    }
                }
            }

            if (!isAssigned)
                continue;

            var firstName = member.GetValue<string>("firstName") ?? string.Empty;
            var lastName = member.GetValue<string>("lastName") ?? string.Empty;
            var fullName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrEmpty(fullName))
                fullName = member.Name ?? member.Email ?? "Unknown";

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
