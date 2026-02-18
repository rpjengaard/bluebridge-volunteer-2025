using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace Code.Services;

public class ApplicationsService : IApplicationsService
{
    private readonly IMemberService _memberService;
    private readonly IMemberGroupService _memberGroupService;
    private readonly IPublishedContentQuery _publishedContentQuery;
    private readonly ILogger<ApplicationsService> _logger;

    private const string CrewContentTypeAlias = "bbvCrewPage";

    // Member Group GUIDs
    private static readonly Guid AdminGroupKey = Guid.Parse("99e1edbb-8181-421d-a74b-e66a2f1e1148");
    private static readonly Guid SchedulerGroupKey = Guid.Parse("e6eef645-b13b-4edb-880b-7b3cdf5b6816"); // Vagtplanlæggere

    public ApplicationsService(
        IMemberService memberService,
        IMemberGroupService memberGroupService,
        IPublishedContentQuery publishedContentQuery,
        ILogger<ApplicationsService> logger)
    {
        _memberService = memberService;
        _memberGroupService = memberGroupService;
        _publishedContentQuery = publishedContentQuery;
        _logger = logger;
    }

    public Task<ApplicationsPageData> GetApplicationsForMemberAsync(string memberEmail)
    {
        var result = new ApplicationsPageData();

        var requestingMember = _memberService.GetByEmail(memberEmail);
        if (requestingMember == null)
        {
            return Task.FromResult(result);
        }

        // Get member's group assignments
        var memberGroups = _memberService.GetAllRoles(requestingMember.Id);

        var adminGroup = _memberGroupService.GetById(AdminGroupKey);
        var schedulerGroup = _memberGroupService.GetById(SchedulerGroupKey);

        result.IsAdmin = adminGroup != null && memberGroups.Contains(adminGroup.Name);
        result.IsScheduler = schedulerGroup != null && memberGroups.Contains(schedulerGroup.Name);

        // If not admin or scheduler, they can't see applications
        if (!result.IsAdmin && !result.IsScheduler)
        {
            _logger.LogWarning("Member {Email} attempted to access applications without permission", memberEmail);
            return Task.FromResult(result);
        }

        // Build crew cache from published content (replaces recursive tree walks and per-UDI DB calls)
        var crewCache = BuildCrewCache();

        // Sum of desired volunteers across all crews
        result.TotalDesiredVolunteers = crewCache.Values
            .Sum(c => c.MaxVoluntiers ?? 0);

        // Get crews the requesting member is allowed to see
        HashSet<int> allowedCrewIds;
        if (result.IsAdmin)
        {
            // Admin sees all crews
            result.AllowedCrews = crewCache.Values.ToList();
            allowedCrewIds = new HashSet<int>(crewCache.Values.Select(c => c.Id));
        }
        else
        {
            // Scheduler sees crews where they are assigned as supervisor or scheduleSupervisor
            var supervisorCrews = GetCrewsForSupervisor(requestingMember.Key, crewCache);
            result.AllowedCrews = supervisorCrews;
            allowedCrewIds = new HashSet<int>(supervisorCrews.Select(c => c.Id));
        }

        // Cache group names for the per-member checks (looked up once, not per-member)
        var adminGroupName = adminGroup?.Name;
        var volunteerGroup = _memberGroupService.GetByName("Frivillige");
        var volunteerGroupName = volunteerGroup?.Name;
        var supervisorGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (schedulerGroup != null)
            supervisorGroupNames.Add(schedulerGroup.Name);
        foreach (var groupName in new[] { "Sherif", "Vice sherif" })
        {
            var group = _memberGroupService.GetByName(groupName);
            if (group != null)
                supervisorGroupNames.Add(group.Name);
        }

        // Get all members and process in a single pass
        var allMembers = _memberService.GetAllMembers();

        foreach (var member in allMembers)
        {
            // Check accept2026 first (no DB call, just in-memory property) to skip most members early
            var accept2026 = member.GetValue<bool>("accept2026");
            if (!accept2026)
                continue;

            // Get roles once per member (only for members who accepted 2026)
            var roles = _memberService.GetAllRoles(member.Id);

            // Skip admin members
            if (!string.IsNullOrEmpty(adminGroupName) && roles.Contains(adminGroupName))
                continue;

            // Check if member has crews assigned (dictionary lookup instead of DB call per UDI)
            var assignedCrewsValue = member.GetValue<string>("crews");
            if (HasAnyCrewUdis(assignedCrewsValue, crewCache))
            {
                if (!string.IsNullOrEmpty(volunteerGroupName) && roles.Contains(volunteerGroupName))
                    result.VolunteersAssignedToCrews++;
                if (roles.Any(r => supervisorGroupNames.Contains(r)))
                    result.SupervisorsAssignedToCrews++;
                continue; // Skip if already assigned to any crew
            }

            // Get the member's crew wishes (single pass, dictionary lookups, no DB calls)
            var crewWishesValue = member.GetValue<string>("crewWishes");
            var (crewWishIds, crewWishes) = ParseCrewUdis(crewWishesValue, crewCache);

            // For schedulers, only show members whose crew wishes include one of their allowed crews
            if (!result.IsAdmin)
            {
                if (!crewWishIds.Any(wishId => allowedCrewIds.Contains(wishId)))
                    continue; // Skip if none of the crew wishes match allowed crews
            }

            var firstName = member.GetValue<string>("firstName") ?? string.Empty;
            var lastName = member.GetValue<string>("lastName") ?? string.Empty;
            var fullName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrEmpty(fullName))
                fullName = member.Name ?? member.Email ?? "Unknown";

            var birthdate = member.GetValue<DateTime?>("birthdate");
            int? age = null;
            if (birthdate.HasValue && birthdate.Value.Year > 1900)
            {
                age = CalculateAge(birthdate.Value);
            }

            var acceptedDate = member.GetValue<DateTime?>("acceptedDate");

            result.Applications.Add(new ApplicationInfo
            {
                MemberId = member.Id,
                MemberKey = member.Key,
                FirstName = firstName,
                LastName = lastName,
                FullName = fullName,
                Email = member.Email ?? string.Empty,
                Phone = member.GetValue<string>("phone"),
                Birthdate = birthdate == DateTime.MinValue ? null : birthdate,
                Age = age,
                Zipcode = member.GetValue<string>("zipcode"),
                TidligereArbejdssteder = member.GetValue<string>("tidligereArbejdssteder"),
                AcceptedDate = acceptedDate == DateTime.MinValue ? null : acceptedDate,
                CrewWishes = crewWishes
            });
        }

        // Sort applications by accepted date (newest first)
        result.Applications = result.Applications
            .OrderByDescending(a => a.AcceptedDate)
            .ThenBy(a => a.FullName)
            .ToList();

        return Task.FromResult(result);
    }

    /// <summary>
    /// Builds a dictionary of all crew pages from the published content cache.
    /// Replaces recursive tree walks and per-UDI _contentService.GetById() calls.
    /// </summary>
    private Dictionary<Guid, CrewListItem> BuildCrewCache()
    {
        var cache = new Dictionary<Guid, CrewListItem>();

        foreach (var root in _publishedContentQuery.ContentAtRoot())
        {
            foreach (var crew in root.DescendantsOrSelfOfType(CrewContentTypeAlias))
            {
                if (cache.ContainsKey(crew.Key))
                    continue;

                cache[crew.Key] = new CrewListItem
                {
                    Id = crew.Id,
                    Key = crew.Key,
                    Name = crew.Name ?? $"Crew {crew.Id}",
                    Url = crew.Url(),
                    MaxVoluntiers = crew.Value<int?>("maxVoluntiers")
                };
            }
        }

        return cache;
    }

    /// <summary>
    /// Gets crews where the given member is a supervisor, using the published content cache.
    /// </summary>
    private List<CrewListItem> GetCrewsForSupervisor(Guid memberKey, Dictionary<Guid, CrewListItem> crewCache)
    {
        var crews = new List<CrewListItem>();

        foreach (var root in _publishedContentQuery.ContentAtRoot())
        {
            foreach (var crew in root.DescendantsOrSelfOfType(CrewContentTypeAlias))
            {
                var scheduleSupervisors = crew.Value<IEnumerable<IPublishedContent>>("scheduleSupervisor");
                var supervisors = crew.Value<IEnumerable<IPublishedContent>>("supervisors");

                var isSupervisor =
                    (scheduleSupervisors != null && scheduleSupervisors.Any(s => s.Key == memberKey)) ||
                    (supervisors != null && supervisors.Any(s => s.Key == memberKey));

                if (isSupervisor && crewCache.TryGetValue(crew.Key, out var crewItem))
                {
                    crews.Add(crewItem);
                }
            }
        }

        return crews;
    }

    /// <summary>
    /// Checks if a UDI string contains any crew references that exist in the cache.
    /// </summary>
    private static bool HasAnyCrewUdis(string? udiString, Dictionary<Guid, CrewListItem> crewCache)
    {
        if (string.IsNullOrWhiteSpace(udiString))
            return false;

        var udiParts = udiString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var udiPart in udiParts)
        {
            var trimmed = udiPart.Trim();
            if (trimmed.StartsWith("umb://document/", StringComparison.OrdinalIgnoreCase))
            {
                var guidPart = trimmed["umb://document/".Length..];
                if (Guid.TryParse(guidPart, out var contentGuid) && crewCache.ContainsKey(contentGuid))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Parses a UDI string and returns both the crew IDs and CrewListItem references in a single pass.
    /// Uses the pre-built crew cache for O(1) lookups instead of per-UDI DB calls.
    /// </summary>
    private (List<int> Ids, List<CrewListItem> Items) ParseCrewUdis(string? udiString, Dictionary<Guid, CrewListItem> crewCache)
    {
        var ids = new List<int>();
        var items = new List<CrewListItem>();

        if (string.IsNullOrWhiteSpace(udiString))
            return (ids, items);

        var udiParts = udiString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var udiPart in udiParts)
        {
            var trimmed = udiPart.Trim();
            try
            {
                if (trimmed.StartsWith("umb://document/", StringComparison.OrdinalIgnoreCase))
                {
                    var guidPart = trimmed["umb://document/".Length..];
                    if (Guid.TryParse(guidPart, out var contentGuid) && crewCache.TryGetValue(contentGuid, out var crewItem))
                    {
                        ids.Add(crewItem.Id);
                        items.Add(crewItem);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse UDI: {Udi}", trimmed);
            }
        }

        return (ids, items);
    }

    private static int CalculateAge(DateTime birthdate)
    {
        var today = DateTime.Today;
        var age = today.Year - birthdate.Year;
        if (birthdate.Date > today.AddYears(-age))
            age--;
        return age;
    }
}
