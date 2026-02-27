using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;

namespace Code.Services;

public class MemberListService : IMemberListService
{
    private readonly IMemberService _memberService;
    private readonly IMemberGroupService _memberGroupService;
    private readonly IContentService _contentService;
    private readonly ILogger<MemberListService> _logger;

    private static readonly Guid AdminGroupKey = Guid.Parse("99e1edbb-8181-421d-a74b-e66a2f1e1148");
    private static readonly Guid SchedulerGroupKey = Guid.Parse("e6eef645-b13b-4edb-880b-7b3cdf5b6816");

    public MemberListService(
        IMemberService memberService,
        IMemberGroupService memberGroupService,
        IContentService contentService,
        ILogger<MemberListService> logger)
    {
        _memberService = memberService;
        _memberGroupService = memberGroupService;
        _contentService = contentService;
        _logger = logger;
    }

    public Task<MemberListData?> GetAcceptedMembersAsync(string requestingMemberEmail)
    {
        var requestingMember = _memberService.GetByEmail(requestingMemberEmail);
        if (requestingMember == null)
            return Task.FromResult<MemberListData?>(null);

        var requestingMemberGroups = _memberService.GetAllRoles(requestingMember.Id);
        var adminGroup = _memberGroupService.GetById(AdminGroupKey);
        var schedulerGroup = _memberGroupService.GetById(SchedulerGroupKey);

        var isAdmin = adminGroup != null && requestingMemberGroups.Contains(adminGroup.Name);
        var isScheduler = schedulerGroup != null && requestingMemberGroups.Contains(schedulerGroup.Name);

        if (!isAdmin && !isScheduler)
        {
            _logger.LogWarning("Member {Email} attempted to access member list without permission", requestingMemberEmail);
            return Task.FromResult<MemberListData?>(null);
        }

        var allMembers = _memberService.GetAllMembers();
        var crewNameCache = new Dictionary<Guid, string>();
        var items = new List<MemberListItem>();
        var allCrewNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var adminGroupName = adminGroup?.Name;

        foreach (var member in allMembers)
        {
            if (!member.GetValue<bool>("accept2026"))
                continue;

            // Skip admin members from the list
            if (adminGroupName != null)
            {
                var memberGroups = _memberService.GetAllRoles(member.Id);
                if (memberGroups.Contains(adminGroupName))
                    continue;
            }

            var firstName = member.GetValue<string>("firstName") ?? string.Empty;
            var lastName = member.GetValue<string>("lastName") ?? string.Empty;
            var fullName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrEmpty(fullName))
                fullName = member.Name ?? member.Email ?? "Unknown";

            var crewsValue = member.GetValue<string>("crews");
            var crewNames = ResolveCrewNames(crewsValue, crewNameCache);

            foreach (var name in crewNames)
                allCrewNames.Add(name);

            items.Add(new MemberListItem
            {
                MemberKey = member.Key,
                FullName = fullName,
                Email = member.Email ?? string.Empty,
                SignupDate = member.CreateDate,
                CrewNames = crewNames
            });
        }

        var result = new MemberListData
        {
            Members = items.OrderBy(m => m.FullName).ToList(),
            AllCrewNames = allCrewNames.OrderBy(c => c).ToList()
        };

        return Task.FromResult<MemberListData?>(result);
    }

    private List<string> ResolveCrewNames(string? udiString, Dictionary<Guid, string> cache)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(udiString))
            return names;

        var udiParts = udiString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var udiPart in udiParts)
        {
            var trimmed = udiPart.Trim();
            try
            {
                if (!trimmed.StartsWith("umb://document/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var guidPart = trimmed["umb://document/".Length..];
                if (!Guid.TryParse(guidPart, out var contentGuid))
                    continue;

                if (cache.TryGetValue(contentGuid, out var cachedName))
                {
                    names.Add(cachedName);
                }
                else
                {
                    var content = _contentService.GetById(contentGuid);
                    if (content != null)
                    {
                        var name = content.Name ?? $"Crew {content.Id}";
                        cache[contentGuid] = name;
                        names.Add(name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve crew UDI: {Udi}", trimmed);
            }
        }

        return names;
    }
}
