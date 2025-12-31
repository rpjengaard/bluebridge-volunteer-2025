using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Code.Services;

public class CrewService : ICrewService
{
    private readonly IMemberService _memberService;
    private readonly IContentService _contentService;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly ILogger<CrewService> _logger;

    private const string CrewContentTypeAlias = "bbvCrewPage";

    public CrewService(
        IMemberService memberService,
        IContentService contentService,
        IUmbracoContextAccessor umbracoContextAccessor,
        ILogger<CrewService> logger)
    {
        _memberService = memberService;
        _contentService = contentService;
        _umbracoContextAccessor = umbracoContextAccessor;
        _logger = logger;
    }

    public Task<CrewsPageData> GetCrewsForMemberAsync(string memberEmail, bool isAdmin)
    {
        var result = new CrewsPageData { IsAdmin = isAdmin };

        if (isAdmin)
        {
            // Admin sees all crews with member counts
            var allCrews = GetAllCrews();
            var memberCrewAssignments = GetMemberCrewAssignments();

            foreach (var crew in allCrews)
            {
                var memberCount = memberCrewAssignments.Count(m => m.crewIds.Contains(crew.Id));
                crew.MemberCount = memberCount;
                result.Crews.Add(crew);
            }
        }
        else
        {
            // Regular member sees only their assigned crews
            var member = _memberService.GetByEmail(memberEmail);
            if (member == null)
            {
                _logger.LogWarning("Member not found for email: {Email}", memberEmail);
                return Task.FromResult(result);
            }

            var assignedCrewsValue = member.GetValue<string>("crews");
            if (!string.IsNullOrEmpty(assignedCrewsValue))
            {
                var crews = ParseCrewReferences(assignedCrewsValue);
                foreach (var crew in crews)
                {
                    crew.IsMemberAssigned = true;
                    result.Crews.Add(crew);
                }
            }
        }

        result.Crews = result.Crews.OrderBy(c => c.Name).ToList();
        return Task.FromResult(result);
    }

    public Task<CrewDetailData?> GetCrewDetailAsync(int crewId, string memberEmail, bool isAdmin)
    {
        var content = _contentService.GetById(crewId);
        if (content == null || content.ContentType.Alias != CrewContentTypeAlias)
        {
            return Task.FromResult<CrewDetailData?>(null);
        }

        // If not admin, verify member is assigned to this crew
        if (!isAdmin)
        {
            var member = _memberService.GetByEmail(memberEmail);
            if (member == null)
            {
                return Task.FromResult<CrewDetailData?>(null);
            }

            var assignedCrewsValue = member.GetValue<string>("crews");
            var assignedCrewIds = ParseCrewIds(assignedCrewsValue);
            if (!assignedCrewIds.Contains(crewId))
            {
                _logger.LogWarning("Member {Email} attempted to access crew {CrewId} without assignment", memberEmail, crewId);
                return Task.FromResult<CrewDetailData?>(null);
            }
        }

        var description = content.GetValue<string>("description");
        if (!string.IsNullOrEmpty(description))
        {
            description = System.Text.RegularExpressions.Regex.Replace(description, "<[^>]*>", "");
        }

        string? url = null;
        if (_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            var publishedContent = umbracoContext.Content?.GetById(content.Key);
            url = publishedContent?.Url();
        }

        var detail = new CrewDetailData
        {
            Id = content.Id,
            Key = content.Key,
            Name = content.Name ?? $"Crew {content.Id}",
            Description = description,
            AgeLimit = content.GetValue<int?>("ageLimit"),
            Url = url
        };

        // Get members assigned to this crew (admin only shows full list)
        if (isAdmin)
        {
            detail.Members = GetCrewMembers(crewId);
        }

        return Task.FromResult<CrewDetailData?>(detail);
    }

    private List<CrewListItem> GetAllCrews()
    {
        var crews = new List<CrewListItem>();
        var rootContent = _contentService.GetRootContent();

        foreach (var root in rootContent)
        {
            FindCrewsRecursive(root, crews);
        }

        return crews;
    }

    private void FindCrewsRecursive(IContent content, List<CrewListItem> crews)
    {
        if (content.ContentType.Alias == CrewContentTypeAlias)
        {
            var description = content.GetValue<string>("description");
            if (!string.IsNullOrEmpty(description))
            {
                description = System.Text.RegularExpressions.Regex.Replace(description, "<[^>]*>", "");
                if (description.Length > 150)
                    description = description[..150] + "...";
            }

            string? url = null;
            if (_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
            {
                var publishedContent = umbracoContext.Content?.GetById(content.Key);
                url = publishedContent?.Url();
            }

            crews.Add(new CrewListItem
            {
                Id = content.Id,
                Key = content.Key,
                Name = content.Name ?? $"Crew {content.Id}",
                Description = description,
                AgeLimit = content.GetValue<int?>("ageLimit"),
                Url = url
            });
        }

        var children = _contentService.GetPagedChildren(content.Id, 0, int.MaxValue, out _);
        foreach (var child in children)
        {
            FindCrewsRecursive(child, crews);
        }
    }

    private List<(int memberId, List<int> crewIds)> GetMemberCrewAssignments()
    {
        var assignments = new List<(int memberId, List<int> crewIds)>();
        var members = _memberService.GetAllMembers();

        foreach (var member in members)
        {
            var crewsValue = member.GetValue<string>("crews");
            var crewIds = ParseCrewIds(crewsValue);
            if (crewIds.Any())
            {
                assignments.Add((member.Id, crewIds));
            }
        }

        return assignments;
    }

    private List<CrewMemberInfo> GetCrewMembers(int crewId)
    {
        var members = new List<CrewMemberInfo>();
        var allMembers = _memberService.GetAllMembers();

        foreach (var member in allMembers)
        {
            var crewsValue = member.GetValue<string>("crews");
            var crewIds = ParseCrewIds(crewsValue);

            if (crewIds.Contains(crewId))
            {
                var firstName = member.GetValue<string>("firstName") ?? string.Empty;
                var lastName = member.GetValue<string>("lastName") ?? string.Empty;
                var fullName = $"{firstName} {lastName}".Trim();
                if (string.IsNullOrEmpty(fullName))
                    fullName = member.Name ?? member.Email ?? "Unknown";

                members.Add(new CrewMemberInfo
                {
                    MemberId = member.Id,
                    MemberKey = member.Key,
                    FullName = fullName,
                    Email = member.Email ?? string.Empty,
                    Phone = member.GetValue<string>("phone"),
                    HasAccepted2026 = member.GetValue<bool>("accept2026")
                });
            }
        }

        return members.OrderBy(m => m.FullName).ToList();
    }

    private List<int> ParseCrewIds(string? udiString)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(udiString))
            return ids;

        var udiParts = udiString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var udiPart in udiParts)
        {
            var trimmed = udiPart.Trim();
            try
            {
                if (trimmed.StartsWith("umb://document/", StringComparison.OrdinalIgnoreCase))
                {
                    var guidPart = trimmed["umb://document/".Length..];
                    if (Guid.TryParse(guidPart, out var contentGuid))
                    {
                        var content = _contentService.GetById(contentGuid);
                        if (content != null)
                        {
                            ids.Add(content.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse UDI: {Udi}", trimmed);
            }
        }

        return ids;
    }

    private List<CrewListItem> ParseCrewReferences(string udiString)
    {
        var crews = new List<CrewListItem>();
        if (string.IsNullOrWhiteSpace(udiString))
            return crews;

        var udiParts = udiString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var udiPart in udiParts)
        {
            var trimmed = udiPart.Trim();
            try
            {
                if (trimmed.StartsWith("umb://document/", StringComparison.OrdinalIgnoreCase))
                {
                    var guidPart = trimmed["umb://document/".Length..];
                    if (Guid.TryParse(guidPart, out var contentGuid))
                    {
                        var content = _contentService.GetById(contentGuid);
                        if (content != null && content.ContentType.Alias == CrewContentTypeAlias)
                        {
                            var description = content.GetValue<string>("description");
                            if (!string.IsNullOrEmpty(description))
                            {
                                description = System.Text.RegularExpressions.Regex.Replace(description, "<[^>]*>", "");
                                if (description.Length > 150)
                                    description = description[..150] + "...";
                            }

                            string? url = null;
                            if (_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
                            {
                                var publishedContent = umbracoContext.Content?.GetById(content.Key);
                                url = publishedContent?.Url();
                            }

                            crews.Add(new CrewListItem
                            {
                                Id = content.Id,
                                Key = content.Key,
                                Name = content.Name ?? $"Crew {content.Id}",
                                Description = description,
                                AgeLimit = content.GetValue<int?>("ageLimit"),
                                Url = url
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse UDI: {Udi}", trimmed);
            }
        }

        return crews;
    }
}
