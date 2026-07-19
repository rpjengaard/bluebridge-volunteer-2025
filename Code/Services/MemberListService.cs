using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
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

    public Task<MemberListData?> GetAllMembersAsync(string requestingMemberEmail)
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
        var allGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in allMembers)
        {
            var core = ReadMemberCore(member, crewNameCache);
            if (!core.Accepted2026)
                continue;

            var fullName = $"{core.FirstName} {core.LastName}".Trim();
            if (string.IsNullOrEmpty(fullName))
                fullName = member.Name ?? member.Email ?? "Unknown";

            foreach (var name in core.CrewNames)
                allCrewNames.Add(name);

            foreach (var group in core.MemberGroups)
                allGroupNames.Add(group);

            items.Add(new MemberListItem
            {
                MemberKey = member.Key,
                FullName = fullName,
                Email = member.Email ?? string.Empty,
                SignupDate = core.SignupDate,
                CrewNames = core.CrewNames,
                MemberGroups = core.MemberGroups,
                IsCanceled = core.IsCanceled
            });
        }

        var result = new MemberListData
        {
            Members = items,
            AllCrewNames = allCrewNames.OrderBy(c => c).ToList(),
            AllGroupNames = allGroupNames.OrderBy(g => g).ToList(),
            IsAdmin = isAdmin
        };

        return Task.FromResult<MemberListData?>(result);
    }

    // [CHANGE: member export API endpoint] Related: IMemberListService.cs, Web/Controllers/MemberExportApiController.cs
    // No permission check here by design: the API key check in MemberExportApiController is the gate.
    public Task<List<MemberExportItem>> GetMemberExportAsync(string? groupFilter)
    {
        var allMembers = _memberService.GetAllMembers();
        var crewNameCache = new Dictionary<Guid, string>();
        var items = new List<MemberExportItem>();

        foreach (var member in allMembers)
        {
            var core = ReadMemberCore(member, crewNameCache);

            if (!string.IsNullOrWhiteSpace(groupFilter) &&
                !core.MemberGroups.Contains(groupFilter, StringComparer.OrdinalIgnoreCase))
                continue;

            items.Add(new MemberExportItem
            {
                FirstName = core.FirstName,
                LastName = core.LastName,
                Email = member.Email ?? string.Empty,
                Crews = core.CrewNames,
                MemberGroups = core.MemberGroups,
                SignupDate = core.SignupDate,
                IsCanceled = core.IsCanceled,
                Accepted2026 = core.Accepted2026
            });
        }

        return Task.FromResult(items);
    }

    private MemberCore ReadMemberCore(IMember member, Dictionary<Guid, string> crewNameCache)
    {
        return new MemberCore(
            member.GetValue<string>("firstName") ?? string.Empty,
            member.GetValue<string>("lastName") ?? string.Empty,
            ResolveCrewNames(member.GetValue<string>("crews"), crewNameCache),
            _memberService.GetAllRoles(member.Id).ToList(),
            member.GetValue<DateTime?>("acceptedDate") ?? member.CreateDate,
            member.GetValue<bool>("cancelation"),
            member.GetValue<bool>("accept2026"));
    }

    private sealed record MemberCore(
        string FirstName,
        string LastName,
        List<string> CrewNames,
        List<string> MemberGroups,
        DateTime SignupDate,
        bool IsCanceled,
        bool Accepted2026);

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
