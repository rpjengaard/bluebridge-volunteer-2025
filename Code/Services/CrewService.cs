using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Code.Services;

public class CrewService : ICrewService
{
    private readonly IMemberService _memberService;
    private readonly IMemberGroupService _memberGroupService;
    private readonly IContentService _contentService;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly ILogger<CrewService> _logger;

    private const string CrewContentTypeAlias = "bbvCrewPage";

    // Member Group GUIDs
    private static readonly Guid AdminGroupKey = Guid.Parse("99e1edbb-8181-421d-a74b-e66a2f1e1148");
    private static readonly Guid SchedulerGroupKey = Guid.Parse("e6eef645-b13b-4edb-880b-7b3cdf5b6816"); // Vagtplanlæggere
    private static readonly Guid VolunteerGroupKey = Guid.Parse("dd21e01b-ff9e-4e0f-b821-476f793b865f"); // Frivillige

    public CrewService(
        IMemberService memberService,
        IMemberGroupService memberGroupService,
        IContentService contentService,
        IUmbracoContextAccessor umbracoContextAccessor,
        ILogger<CrewService> logger)
    {
        _memberService = memberService;
        _memberGroupService = memberGroupService;
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
            var crewStats = ComputeCrewStats(allCrews);

            foreach (var crew in allCrews)
            {
                if (crewStats.TryGetValue(crew.Id, out var stats))
                {
                    crew.MemberCount = stats.MemberCount;
                    crew.WishCount = stats.WishCount;
                    crew.AssignedCount = stats.AssignedCount;
                    crew.SupervisorCount = stats.SupervisorCount;
                }
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

    public Task<CrewViewMode> GetMemberCrewViewModeAsync(string memberEmail, int crewId)
    {
        var member = _memberService.GetByEmail(memberEmail);
        if (member == null)
        {
            return Task.FromResult(CrewViewMode.Volunteer);
        }

        // Get member's group assignments
        var memberGroups = _memberService.GetAllRoles(member.Id);

        // Get group names for comparison
        var adminGroup = _memberGroupService.GetById(AdminGroupKey);
        var schedulerGroup = _memberGroupService.GetById(SchedulerGroupKey);

        // Check if member is in Admin group
        if (adminGroup != null && memberGroups.Contains(adminGroup.Name))
        {
            return Task.FromResult(CrewViewMode.Admin);
        }

        // Check if member is in Scheduler (Vagtplanlæggere) group
        if (schedulerGroup != null && memberGroups.Contains(schedulerGroup.Name))
        {
            return Task.FromResult(CrewViewMode.Scheduler);
        }

        // Check if member is in Sherif or Vice sherif group
        if (memberGroups.Contains("Sherif") || memberGroups.Contains("Vice sherif"))
        {
            return Task.FromResult(CrewViewMode.Scheduler);
        }

        // Also check if member is assigned as scheduler/supervisor for this specific crew
        var content = _contentService.GetById(crewId);
        if (content != null)
        {
            var schedulerUdi = content.GetValue<string>("scheduleSupervisor");
            if (!string.IsNullOrEmpty(schedulerUdi) && IsMemberInUdiList(member.Key, schedulerUdi))
            {
                return Task.FromResult(CrewViewMode.Scheduler);
            }

            var supervisorsUdi = content.GetValue<string>("supervisors");
            if (!string.IsNullOrEmpty(supervisorsUdi) && IsMemberInUdiList(member.Key, supervisorsUdi))
            {
                return Task.FromResult(CrewViewMode.Scheduler);
            }
        }

        return Task.FromResult(CrewViewMode.Volunteer);
    }

    public Task<MemberDetailData?> GetMemberByKeyAsync(Guid memberKey, string requestingMemberEmail)
    {
        // Check if requesting member has permission (must be admin or scheduler)
        var requestingMember = _memberService.GetByEmail(requestingMemberEmail);
        if (requestingMember == null)
        {
            return Task.FromResult<MemberDetailData?>(null);
        }

        // Get requesting member's groups
        var requestingMemberGroups = _memberService.GetAllRoles(requestingMember.Id);
        var adminGroup = _memberGroupService.GetById(AdminGroupKey);
        var schedulerGroup = _memberGroupService.GetById(SchedulerGroupKey);

        var isAdmin = adminGroup != null && requestingMemberGroups.Contains(adminGroup.Name);
        var isScheduler = schedulerGroup != null && requestingMemberGroups.Contains(schedulerGroup.Name);

        if (!isAdmin && !isScheduler)
        {
            _logger.LogWarning("Member {Email} attempted to view member details without permission", requestingMemberEmail);
            return Task.FromResult<MemberDetailData?>(null);
        }

        // Get the member by key
        var member = _memberService.GetByKey(memberKey);
        if (member == null)
        {
            return Task.FromResult<MemberDetailData?>(null);
        }

        var firstName = member.GetValue<string>("firstName") ?? string.Empty;
        var lastName = member.GetValue<string>("lastName") ?? string.Empty;
        var fullName = $"{firstName} {lastName}".Trim();
        if (string.IsNullOrEmpty(fullName))
            fullName = member.Name ?? member.Email ?? "Unknown";

        var birthdate = member.GetValue<DateTime?>("birthdate");
        var acceptedDate = member.GetValue<DateTime?>("acceptedDate");
        var invitationSentDate = member.GetValue<DateTime?>("invitationSentDate");

        var detail = new MemberDetailData
        {
            MemberId = member.Id,
            MemberKey = member.Key,
            FirstName = firstName,
            LastName = lastName,
            FullName = fullName,
            Email = member.Email ?? string.Empty,
            Phone = member.GetValue<string>("phone"),
            Birthdate = birthdate == DateTime.MinValue ? null : birthdate,
            TidligereArbejdssteder = member.GetValue<string>("tidligereArbejdssteder"),
            Accept2026 = member.GetValue<bool>("accept2026"),
            MemberWish = member.GetValue<string>("memberWish"),
            TimeslotWish = member.GetValue<string>("timeslotWish"),
            OtherNotes = ExtractRteMarkup(member.GetValue<string>("otherNotes")),
            AcceptedDate = acceptedDate == DateTime.MinValue ? null : acceptedDate,
            InvitationSentDate = invitationSentDate == DateTime.MinValue ? null : invitationSentDate,
            MemberGroups = _memberService.GetAllRoles(member.Id).ToList()
        };

        // Get assigned crews
        var assignedCrewsValue = member.GetValue<string>("crews");
        detail.AssignedCrews = ParseCrewReferences(assignedCrewsValue);

        // Get crew wishes
        var crewWishesValue = member.GetValue<string>("crewWishes");
        detail.CrewWishes = ParseCrewReferences(crewWishesValue);

        return Task.FromResult<MemberDetailData?>(detail);
    }

    public Task<CrewDetailData?> GetCrewDetailAsync(int crewId, string memberEmail, CrewViewMode viewMode)
    {
        var content = _contentService.GetById(crewId);
        if (content == null || content.ContentType.Alias != CrewContentTypeAlias)
        {
            return Task.FromResult<CrewDetailData?>(null);
        }

        // If volunteer, verify member is assigned to this crew
        if (viewMode == CrewViewMode.Volunteer)
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

        string? description = null;
        string? descriptionHtml = null;
        string? url = null;
        int? ageLimit = null;
        int? maxVoluntiers = null;

        // Get published content to access properly converted property values
        if (_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            var publishedContent = umbracoContext.Content?.GetById(content.Key);
            if (publishedContent != null)
            {
                url = publishedContent.Url();
                description = GetRteDescription(publishedContent);
                ageLimit = publishedContent.Value<int?>("ageLimit");
                maxVoluntiers = publishedContent.Value<int?>("maxVoluntiers");

                // Also get HTML version for full display
                var descriptionValue = publishedContent.Value<Umbraco.Cms.Core.Strings.IHtmlEncodedString>("description");
                descriptionHtml = descriptionValue?.ToHtmlString();
            }
        }

        // Fallback to IContent if published content not available
        ageLimit ??= content.GetValue<int?>("ageLimit");
        maxVoluntiers ??= content.GetValue<int?>("maxVoluntiers");

        var detail = new CrewDetailData
        {
            Id = content.Id,
            Key = content.Key,
            Name = content.Name ?? $"Crew {content.Id}",
            Description = description,
            DescriptionHtml = descriptionHtml,
            AgeLimit = ageLimit,
            MaxVoluntiers = maxVoluntiers,
            Url = url,
            ViewMode = viewMode
        };

        // Get supervisors for all view modes (volunteers need contact info)
        detail.ScheduleSupervisor = GetSupervisorFromUdi(content.GetValue<string>("scheduleSupervisor"));
        detail.Supervisors = GetSupervisorsFromUdi(content.GetValue<string>("supervisors"));

        // Get members assigned to this crew (admin and scheduler only)
        if (viewMode == CrewViewMode.Admin || viewMode == CrewViewMode.Scheduler)
        {
            // Share caching across both method calls for better performance
            var adminMemberIds = GetAdminMemberIds();   //TODO: This is to slow. Could we cache it on startup?
            var crewGuidToIdCache = new Dictionary<Guid, int>();

            detail.Members = GetCrewMembers(crewId, adminMemberIds, crewGuidToIdCache);
            detail.WishlistMembers = GetWishlistMembersNotAssigned(crewId, adminMemberIds, crewGuidToIdCache);
        }

        return Task.FromResult<CrewDetailData?>(detail);
    }

    private bool IsMemberInUdiList(Guid memberKey, string udiString)
    {
        if (string.IsNullOrWhiteSpace(udiString))
            return false;

        var udiParts = udiString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var udiPart in udiParts)
        {
            var trimmed = udiPart.Trim();
            if (trimmed.StartsWith("umb://member/", StringComparison.OrdinalIgnoreCase))
            {
                var guidPart = trimmed["umb://member/".Length..];
                if (Guid.TryParse(guidPart, out var guid) && guid == memberKey)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private SupervisorInfo? GetSupervisorFromUdi(string? udiString)
    {
        if (string.IsNullOrWhiteSpace(udiString))
            return null;

        var supervisors = GetSupervisorsFromUdi(udiString);
        return supervisors.FirstOrDefault();
    }

    private List<SupervisorInfo> GetSupervisorsFromUdi(string? udiString)
    {
        var supervisors = new List<SupervisorInfo>();
        if (string.IsNullOrWhiteSpace(udiString))
            return supervisors;

        var udiParts = udiString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var udiPart in udiParts)
        {
            var trimmed = udiPart.Trim();
            try
            {
                if (trimmed.StartsWith("umb://member/", StringComparison.OrdinalIgnoreCase))
                {
                    var guidPart = trimmed["umb://member/".Length..];
                    if (Guid.TryParse(guidPart, out var memberGuid))
                    {
                        var member = _memberService.GetByKey(memberGuid);
                        if (member != null)
                        {
                            var firstName = member.GetValue<string>("firstName") ?? string.Empty;
                            var lastName = member.GetValue<string>("lastName") ?? string.Empty;
                            var fullName = $"{firstName} {lastName}".Trim();
                            if (string.IsNullOrEmpty(fullName))
                                fullName = member.Name ?? member.Email ?? "Unknown";

                            supervisors.Add(new SupervisorInfo
                            {
                                MemberId = member.Id,
                                FullName = fullName,
                                Email = member.Email ?? string.Empty,
                                Phone = member.GetValue<string>("phone")
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse member UDI: {Udi}", trimmed);
            }
        }

        return supervisors;
    }

    private List<CrewMemberInfo> GetWishlistMembersNotAssigned(int crewId, HashSet<int> adminMemberIds, Dictionary<Guid, int> crewGuidToIdCache)
    {
        var wishlistMembers = new List<CrewMemberInfo>();
        var allMembers = _memberService.GetAllMembers();

        Stopwatch sw = Stopwatch.StartNew();

        foreach (var member in allMembers)
        {
            // Skip admin members using cached set
            if (adminMemberIds.Contains(member.Id))
                continue;

            // Skip rejected members
            if (member.GetValue<bool>("rejected"))
                continue;

            // Check if crew is on their wishlist
            var wishlistValue = member.GetValue<string>("crewWishes");
            var wishlistIds = ParseCrewIdsWithCache(wishlistValue, crewGuidToIdCache);

            if (!wishlistIds.Contains(crewId))
                continue;

            // Check if they're already assigned to any crew
            var assignedCrewsValue = member.GetValue<string>("crews");
            var assignedCrewIds = ParseCrewIdsWithCache(assignedCrewsValue, crewGuidToIdCache);

            if (assignedCrewIds.Any())
                continue; // Skip if already assigned to any crew

            var firstName = member.GetValue<string>("firstName") ?? string.Empty;
            var lastName = member.GetValue<string>("lastName") ?? string.Empty;
            var fullName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrEmpty(fullName))
                fullName = member.Name ?? member.Email ?? "Unknown";

            var acceptedDateVal = member.GetValue<DateTime?>("acceptedDate");

            wishlistMembers.Add(new CrewMemberInfo
            {
                MemberId = member.Id,
                MemberKey = member.Key,
                FullName = fullName,
                Email = member.Email ?? string.Empty,
                Phone = member.GetValue<string>("phone"),
                HasAccepted2026 = member.GetValue<bool>("accept2026"),
                AcceptedDate = acceptedDateVal > DateTime.MinValue ? acceptedDateVal : null,
                SignupDate = member.CreateDate,
                CrewWishes = ParseCrewReferences(wishlistValue)
            });
        }

        sw.Stop();
        _logger.LogInformation("GetWishlistMembersNotAssigned for crewId {CrewId} took {ElapsedMilliseconds} ms and found {MemberCount} members", crewId, sw.ElapsedMilliseconds, wishlistMembers.Count);

        return wishlistMembers.OrderBy(m => m.AcceptedDate ?? m.SignupDate).ToList();
    }

    private bool IsMemberInAdminGroup(int memberId)
    {
        var memberGroups = _memberService.GetAllRoles(memberId);
        var adminGroup = _memberGroupService.GetById(AdminGroupKey);
        return adminGroup != null && memberGroups.Contains(adminGroup.Name);
    }

    /// <summary>
    /// Gets all admin member IDs in a single operation to avoid repeated database queries
    /// </summary>
    private HashSet<int> GetAdminMemberIds()
    {
        var adminMemberIds = new HashSet<int>();
        var adminGroup = _memberGroupService.GetById(AdminGroupKey);

        if (adminGroup == null)
            return adminMemberIds;

        var allMembers = _memberService.GetAllMembers();
        foreach (var member in allMembers)
        {
            var memberGroups = _memberService.GetAllRoles(member.Id);
            if (memberGroups.Contains(adminGroup.Name))
            {
                adminMemberIds.Add(member.Id);
            }
        }

        return adminMemberIds;
    }

    /// <summary>
    /// Parses crew IDs with caching to minimize repeated _contentService.GetById calls
    /// </summary>
    private List<int> ParseCrewIdsWithCache(string? udiString, Dictionary<Guid, int> cache)
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
                        // Check cache first
                        if (cache.TryGetValue(contentGuid, out var cachedId))
                        {
                            ids.Add(cachedId);
                        }
                        else
                        {
                            // Not in cache, fetch from content service
                            var content = _contentService.GetById(contentGuid);
                            if (content != null)
                            {
                                cache[contentGuid] = content.Id;
                                ids.Add(content.Id);
                            }
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

    private struct CrewStatsEntry
    {
        public int MemberCount;
        public int WishCount;
        public int AssignedCount;
        public int SupervisorCount;
    }

    /// <summary>
    /// Single pass over all members to compute per-crew stats:
    /// - MemberCount: accept2026=true, in "Frivillige" group, assigned to crew
    /// - WishCount: accept2026=true, not admin, no crew assignments, crew in crewWishes
    /// - SupervisorCount: accept2026=true, assigned to crew, in Sherif/Vice sherif/Vagtplanlæggere group
    /// </summary>
    private Dictionary<int, CrewStatsEntry> ComputeCrewStats(List<CrewListItem> allCrews)
    {
        var stats = new Dictionary<int, CrewStatsEntry>();
        foreach (var crew in allCrews)
            stats[crew.Id] = new CrewStatsEntry();

        // Resolve group names once
        var adminGroup = _memberGroupService.GetById(AdminGroupKey);
        var volunteerGroup = _memberGroupService.GetById(VolunteerGroupKey);
        var schedulerGroup = _memberGroupService.GetById(SchedulerGroupKey);

        var adminGroupName = adminGroup?.Name;
        var volunteerGroupName = volunteerGroup?.Name;
        var schedulerGroupName = schedulerGroup?.Name;

        var supervisorGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Sherif", "Vice sherif" })
            supervisorGroupNames.Add(name);
        if (schedulerGroupName != null)
            supervisorGroupNames.Add(schedulerGroupName);

        var crewGuidToIdCache = new Dictionary<Guid, int>();
        var allMembers = _memberService.GetAllMembers();

        foreach (var member in allMembers)
        {
            // Rejected members count toward nothing
            if (member.GetValue<bool>("rejected"))
                continue;

            var roles = _memberService.GetAllRoles(member.Id);
            var isAdmin = adminGroupName != null && roles.Contains(adminGroupName);
            if (isAdmin)
                continue;

            var isVolunteer = volunteerGroupName != null && roles.Contains(volunteerGroupName);
            var isSupervisor = roles.Any(r => supervisorGroupNames.Contains(r));
            var crewsValue = member.GetValue<string>("crews");
            var assignedCrewIds = ParseCrewIdsWithCache(crewsValue, crewGuidToIdCache);

            // AssignedCount: Frivillige + assigned + not rejected (no accept2026 gate)
            if (isVolunteer && assignedCrewIds.Any())
            {
                foreach (var crewId in assignedCrewIds)
                {
                    if (!stats.ContainsKey(crewId)) continue;
                    var entry = stats[crewId];
                    entry.AssignedCount++;
                    stats[crewId] = entry;
                }
            }

            // MemberCount, WishCount, SupervisorCount require accept2026=true
            if (!member.GetValue<bool>("accept2026"))
                continue;

            if (assignedCrewIds.Any())
            {
                foreach (var crewId in assignedCrewIds)
                {
                    if (!stats.ContainsKey(crewId))
                        continue;

                    var entry = stats[crewId];
                    if (isVolunteer)
                        entry.MemberCount++;
                    if (isSupervisor)
                        entry.SupervisorCount++;
                    stats[crewId] = entry;
                }
            }
            else
            {
                // No crew assignments — check wish list for Ansøgere count
                var wishesValue = member.GetValue<string>("crewWishes");
                var wishCrewIds = ParseCrewIdsWithCache(wishesValue, crewGuidToIdCache);

                foreach (var crewId in wishCrewIds)
                {
                    if (!stats.ContainsKey(crewId))
                        continue;

                    var entry = stats[crewId];
                    entry.WishCount++;
                    stats[crewId] = entry;
                }
            }
        }

        return stats;
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
            string? description = null;
            string? url = null;

            // Get published content to access properly converted property values
            if (_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
            {
                var publishedContent = umbracoContext.Content?.GetById(content.Key);
                if (publishedContent != null)
                {
                    url = publishedContent.Url();
                    description = GetRteDescription(publishedContent, 150);
                }
            }

            crews.Add(new CrewListItem
            {
                Id = content.Id,
                Key = content.Key,
                Name = content.Name ?? $"Crew {content.Id}",
                Description = description,
                AgeLimit = content.GetValue<int?>("ageLimit"),
                MaxVoluntiers = content.GetValue<int?>("maxVoluntiers"),
                Url = url
            });
        }

        var children = _contentService.GetPagedChildren(content.Id, 0, int.MaxValue, out _);
        foreach (var child in children)
        {
            FindCrewsRecursive(child, crews);
        }
    }

    /// <summary>
    /// Extracts the HTML markup from a raw RTE JSON value stored on IContent/IMember.
    /// Umbraco v17 stores RTE as JSON: {"blocks":{...},"markup":"<p>...</p>"}
    /// </summary>
    private static string? ExtractRteMarkup(string? rawRteValue)
    {
        if (string.IsNullOrWhiteSpace(rawRteValue))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawRteValue);
            if (doc.RootElement.TryGetProperty("markup", out var markupElement))
            {
                var markup = markupElement.GetString();
                return string.IsNullOrWhiteSpace(markup) ? null : markup;
            }
        }
        catch
        {
            // Not JSON — might already be plain HTML
            return rawRteValue;
        }

        return null;
    }

    /// <summary>
    /// Gets plain text description from RTE property, properly handling the IHtmlEncodedString type
    /// </summary>
    private static string? GetRteDescription(Umbraco.Cms.Core.Models.PublishedContent.IPublishedContent publishedContent, int? maxLength = null)
    {
        var descriptionValue = publishedContent.Value<Umbraco.Cms.Core.Strings.IHtmlEncodedString>("description");
        if (descriptionValue == null)
            return null;

        var htmlContent = descriptionValue.ToHtmlString();
        if (string.IsNullOrEmpty(htmlContent))
            return null;

        // Strip HTML tags for plain text preview
        var description = System.Text.RegularExpressions.Regex.Replace(htmlContent, "<[^>]*>", "");
        // Trim whitespace and normalize spaces
        description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ").Trim();

        if (maxLength.HasValue && description.Length > maxLength.Value)
            description = description[..maxLength.Value] + "...";

        return description;
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

    private List<(int memberId, List<int> crewIds)> GetMemberWishAssignments()
    {
        var assignments = new List<(int memberId, List<int> crewIds)>();
        var members = _memberService.GetAllMembers();

        foreach (var member in members)
        {
            var wishesValue = member.GetValue<string>("crewWishes");
            var crewIds = ParseCrewIds(wishesValue);
            if (crewIds.Any())
            {
                assignments.Add((member.Id, crewIds));
            }
        }

        return assignments;
    }

    private List<CrewMemberInfo> GetCrewMembers(int crewId, HashSet<int> adminMemberIds, Dictionary<Guid, int> crewGuidToIdCache)
    {
        var members = new List<CrewMemberInfo>();
        var allMembers = _memberService.GetAllMembers();

        Stopwatch sw = Stopwatch.StartNew();

        foreach (var member in allMembers)
        {
            // Skip admin members using cached set
            if (adminMemberIds.Contains(member.Id))
                continue;

            // Skip rejected members
            if (member.GetValue<bool>("rejected"))
                continue;

            var crewsValue = member.GetValue<string>("crews");
            var crewIds = ParseCrewIdsWithCache(crewsValue, crewGuidToIdCache);

            if (crewIds.Contains(crewId))
            {
                var firstName = member.GetValue<string>("firstName") ?? string.Empty;
                var lastName = member.GetValue<string>("lastName") ?? string.Empty;
                var fullName = $"{firstName} {lastName}".Trim();
                if (string.IsNullOrEmpty(fullName))
                    fullName = member.Name ?? member.Email ?? "Unknown";

                var acceptedDateVal = member.GetValue<DateTime?>("acceptedDate");
                var birthdateVal = member.GetValue<DateTime?>("birthdate");

                members.Add(new CrewMemberInfo
                {
                    MemberId = member.Id,
                    MemberKey = member.Key,
                    FirstName = firstName,
                    LastName = lastName,
                    FullName = fullName,
                    Email = member.Email ?? string.Empty,
                    Phone = member.GetValue<string>("phone"),
                    HasAccepted2026 = member.GetValue<bool>("accept2026"),
                    AcceptedDate = acceptedDateVal > DateTime.MinValue ? acceptedDateVal : null,
                    SignupDate = member.CreateDate,
                    Birthdate = birthdateVal > new DateTime(1900, 1, 1) ? birthdateVal : null,
                    MemberWish = member.GetValue<string>("memberWish"),
                    TimeslotWish = member.GetValue<string>("timeslotWish"),
                    OtherNotes = ExtractRteMarkup(member.GetValue<string>("otherNotes")),
                    MemberGroups = _memberService.GetAllRoles(member.Id).ToList()
                });
            }
        }

        sw.Stop();
        _logger.LogInformation("GetCrewMembers for crewId {CrewId} took {ElapsedMilliseconds} ms and found {MemberCount} members", crewId, sw.ElapsedMilliseconds, members.Count);


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
                            string? description = null;
                            string? url = null;

                            // Get published content to access properly converted property values
                            if (_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
                            {
                                var publishedContent = umbracoContext.Content?.GetById(content.Key);
                                if (publishedContent != null)
                                {
                                    url = publishedContent.Url();
                                    description = GetRteDescription(publishedContent, 150);
                                }
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
