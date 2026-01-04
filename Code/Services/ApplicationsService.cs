using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Code.Services;

public class ApplicationsService : IApplicationsService
{
    private readonly IMemberService _memberService;
    private readonly IMemberGroupService _memberGroupService;
    private readonly IContentService _contentService;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly ILogger<ApplicationsService> _logger;

    private const string CrewContentTypeAlias = "bbvCrewPage";

    // Member Group GUIDs
    private static readonly Guid AdminGroupKey = Guid.Parse("99e1edbb-8181-421d-a74b-e66a2f1e1148");
    private static readonly Guid SchedulerGroupKey = Guid.Parse("e6eef645-b13b-4edb-880b-7b3cdf5b6816"); // Vagtplanlæggere

    public ApplicationsService(
        IMemberService memberService,
        IMemberGroupService memberGroupService,
        IContentService contentService,
        IUmbracoContextAccessor umbracoContextAccessor,
        ILogger<ApplicationsService> logger)
    {
        _memberService = memberService;
        _memberGroupService = memberGroupService;
        _contentService = contentService;
        _umbracoContextAccessor = umbracoContextAccessor;
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

        // Get crews the requesting member is allowed to see
        List<int> allowedCrewIds;
        if (result.IsAdmin)
        {
            // Admin sees all crews
            var allCrews = GetAllCrews();
            result.AllowedCrews = allCrews;
            allowedCrewIds = allCrews.Select(c => c.Id).ToList();
        }
        else
        {
            // Scheduler sees crews where they are assigned as supervisor or scheduleSupervisor
            var supervisorCrews = GetCrewsForSupervisor(requestingMember.Key);
            result.AllowedCrews = supervisorCrews;
            allowedCrewIds = supervisorCrews.Select(c => c.Id).ToList();
        }

        // Get all members who have accept2026 = true and have no crews assigned
        var allMembers = _memberService.GetAllMembers();

        foreach (var member in allMembers)
        {
            // Skip admin members
            if (IsMemberInAdminGroup(member.Id))
                continue;

            // Check if member has accepted 2026
            var accept2026 = member.GetValue<bool>("accept2026");
            if (!accept2026)
                continue;

            // Check if member has no crews assigned
            var assignedCrewsValue = member.GetValue<string>("crews");
            var assignedCrewIds = ParseCrewIds(assignedCrewsValue);
            if (assignedCrewIds.Any())
                continue; // Skip if already assigned to any crew

            // Get the member's crew wishes
            var crewWishesValue = member.GetValue<string>("crewWishes");
            var crewWishIds = ParseCrewIds(crewWishesValue);
            var crewWishes = ParseCrewReferences(crewWishesValue);

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

    private int CalculateAge(DateTime birthdate)
    {
        var today = DateTime.Today;
        var age = today.Year - birthdate.Year;
        if (birthdate.Date > today.AddYears(-age))
            age--;
        return age;
    }

    private bool IsMemberInAdminGroup(int memberId)
    {
        var memberGroups = _memberService.GetAllRoles(memberId);
        var adminGroup = _memberGroupService.GetById(AdminGroupKey);
        return adminGroup != null && memberGroups.Contains(adminGroup.Name);
    }

    private List<CrewListItem> GetCrewsForSupervisor(Guid memberKey)
    {
        var crews = new List<CrewListItem>();
        var rootContent = _contentService.GetRootContent();

        foreach (var root in rootContent)
        {
            FindSupervisorCrewsRecursive(root, memberKey, crews);
        }

        return crews;
    }

    private void FindSupervisorCrewsRecursive(IContent content, Guid memberKey, List<CrewListItem> crews)
    {
        if (content.ContentType.Alias == CrewContentTypeAlias)
        {
            // Check if member is supervisor or scheduleSupervisor for this crew
            var schedulerUdi = content.GetValue<string>("scheduleSupervisor");
            var supervisorsUdi = content.GetValue<string>("supervisors");

            if (IsMemberInUdiList(memberKey, schedulerUdi) || IsMemberInUdiList(memberKey, supervisorsUdi))
            {
                string? url = null;

                if (_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
                {
                    var publishedContent = umbracoContext.Content?.GetById(content.Key);
                    if (publishedContent != null)
                    {
                        url = publishedContent.Url();
                    }
                }

                crews.Add(new CrewListItem
                {
                    Id = content.Id,
                    Key = content.Key,
                    Name = content.Name ?? $"Crew {content.Id}",
                    Url = url
                });
            }
        }

        var children = _contentService.GetPagedChildren(content.Id, 0, int.MaxValue, out _);
        foreach (var child in children)
        {
            FindSupervisorCrewsRecursive(child, memberKey, crews);
        }
    }

    private bool IsMemberInUdiList(Guid memberKey, string? udiString)
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
            string? url = null;

            if (_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
            {
                var publishedContent = umbracoContext.Content?.GetById(content.Key);
                if (publishedContent != null)
                {
                    url = publishedContent.Url();
                }
            }

            crews.Add(new CrewListItem
            {
                Id = content.Id,
                Key = content.Key,
                Name = content.Name ?? $"Crew {content.Id}",
                Url = url
            });
        }

        var children = _contentService.GetPagedChildren(content.Id, 0, int.MaxValue, out _);
        foreach (var child in children)
        {
            FindCrewsRecursive(child, crews);
        }
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

    private List<CrewListItem> ParseCrewReferences(string? udiString)
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
                            string? url = null;

                            if (_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
                            {
                                var publishedContent = umbracoContext.Content?.GetById(content.Key);
                                if (publishedContent != null)
                                {
                                    url = publishedContent.Url();
                                }
                            }

                            crews.Add(new CrewListItem
                            {
                                Id = content.Id,
                                Key = content.Key,
                                Name = content.Name ?? $"Crew {content.Id}",
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
