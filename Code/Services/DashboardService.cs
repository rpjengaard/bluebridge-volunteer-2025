using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Code.Services;

public class DashboardService : IDashboardService
{
    private readonly IMemberService _memberService;
    private readonly IContentService _contentService;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IScheduleService _scheduleService;
    private readonly ILogger<DashboardService> _logger;

    private const string CrewContentTypeAlias = "bbvCrewPage";

    public DashboardService(
        IMemberService memberService,
        IContentService contentService,
        IUmbracoContextAccessor umbracoContextAccessor,
        IScheduleService scheduleService,
        ILogger<DashboardService> logger)
    {
        _memberService = memberService;
        _contentService = contentService;
        _umbracoContextAccessor = umbracoContextAccessor;
        _scheduleService = scheduleService;
        _logger = logger;
    }

    public async Task<DashboardData?> GetDashboardDataAsync(string memberEmail)
    {
        if (string.IsNullOrWhiteSpace(memberEmail))
            return null;

        var member = _memberService.GetByEmail(memberEmail);
        if (member == null)
        {
            _logger.LogWarning("Member not found for email: {Email}", memberEmail);
            return null;
        }

        var data = new DashboardData
        {
            Profile = new MemberProfileData
            {
                MemberId = member.Id,
                MemberKey = member.Key,
                FirstName = member.GetValue<string>("firstName") ?? string.Empty,
                LastName = member.GetValue<string>("lastName") ?? string.Empty,
                Email = member.Email ?? string.Empty,
                Phone = member.GetValue<string>("phone"),
                Birthdate = member.GetValue<DateTime?>("birthdate"),
                PreviousWorkplaces = member.GetValue<string>("tidligereArbejdssteder")
            },
            HasAccepted2026 = member.GetValue<bool>("accept2026"),
            AcceptedDate = member.GetValue<DateTime?>("acceptedDate")
        };

        // Get crew wishes
        var crewWishesValue = member.GetValue<string>("crewWishes");
        if (!string.IsNullOrEmpty(crewWishesValue))
            data.CrewWishes = ParseCrewReferences(crewWishesValue);

        // Get assigned crews
        var assignedCrewsValue = member.GetValue<string>("crews");
        if (!string.IsNullOrEmpty(assignedCrewsValue))
            data.AssignedCrews = ParseCrewReferences(assignedCrewsValue);

        // Load shifts assigned to this member
        var scheduleShifts = await _scheduleService.GetShiftsForMemberAsync(member.Key);
        data.Shifts = scheduleShifts.Select(s => new ShiftData
        {
            Id = s.Id,
            CrewName = s.CrewName ?? string.Empty,
            StartTime = CombineDateTime(s.ScheduleDate, s.StartTime),
            EndTime = CombineEndDateTime(s.ScheduleDate, s.StartTime, s.EndTime),
            Location = null,
            Notes = s.ScheduleName
        }).ToList();

        // Load published schedules for assigned crews (for the "Crew vagter" section)
        var publishedSchedules = new List<ScheduleData>();
        foreach (var crew in data.AssignedCrews)
        {
            var crewSchedules = await _scheduleService.GetPublishedSchedulesForCrewAsync(crew.Id);
            publishedSchedules.AddRange(crewSchedules);
        }
        data.PublishedCrewSchedules = publishedSchedules;

        _logger.LogDebug("Loaded dashboard data for {Email}: {WishCount} wishes, {AssignedCount} assigned crews, {ShiftCount} shifts",
            memberEmail, data.CrewWishes.Count, data.AssignedCrews.Count, data.Shifts.Count);

        return data;
    }

    private static DateTime CombineDateTime(DateTime date, string timeStr)
    {
        if (string.IsNullOrEmpty(timeStr)) return date;
        var parts = timeStr.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
            return date.Date.AddHours(h).AddMinutes(m);
        return date;
    }

    private static DateTime CombineEndDateTime(DateTime scheduleDate, string startTimeStr, string endTimeStr)
    {
        var startMins = TimeToMinutes(startTimeStr);
        var endMins = TimeToMinutes(endTimeStr);
        // endTime "00:00" = midnight end of day
        if (endMins == 0) endMins = 24 * 60;
        // spans midnight
        var addDays = endMins <= startMins ? 1 : 0;
        var h = endMins / 60 % 24;
        var m = endMins % 60;
        return scheduleDate.Date.AddDays(addDays).AddHours(h).AddMinutes(m);
    }

    private static int TimeToMinutes(string time)
    {
        if (string.IsNullOrEmpty(time)) return 0;
        var parts = time.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
            return h * 60 + m;
        return 0;
    }

    private List<CrewData> ParseCrewReferences(string udiString)
    {
        var crews = new List<CrewData>();

        if (string.IsNullOrWhiteSpace(udiString))
            return crews;

        // UDI format: umb://document/guid,umb://document/guid,...
        var udiParts = udiString.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var udiPart in udiParts)
        {
            var trimmed = udiPart.Trim();
            try
            {
                // Try to extract GUID from UDI format: umb://document/xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
                if (trimmed.StartsWith("umb://document/", StringComparison.OrdinalIgnoreCase))
                {
                    var guidPart = trimmed["umb://document/".Length..];
                    if (Guid.TryParse(guidPart, out var contentGuid))
                    {
                        var content = _contentService.GetById(contentGuid);
                        if (content != null && content.ContentType.Alias == CrewContentTypeAlias)
                        {
                            AddCrewData(crews, content);
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

    private void AddCrewData(List<CrewData> crews, IContent content)
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

                // Get description from published content - RTE returns IHtmlEncodedString
                var descriptionValue = publishedContent.Value<Umbraco.Cms.Core.Strings.IHtmlEncodedString>("description");
                if (descriptionValue != null)
                {
                    var htmlContent = descriptionValue.ToHtmlString();
                    if (!string.IsNullOrEmpty(htmlContent))
                    {
                        // Strip HTML tags for plain text preview
                        description = System.Text.RegularExpressions.Regex.Replace(htmlContent, "<[^>]*>", "");
                        // Trim whitespace and normalize spaces
                        description = System.Text.RegularExpressions.Regex.Replace(description, @"\s+", " ").Trim();
                        if (description.Length > 150)
                            description = description[..150] + "...";
                    }
                }
            }
        }

        crews.Add(new CrewData
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
