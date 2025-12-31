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
    private readonly ILogger<DashboardService> _logger;

    private const string CrewContentTypeAlias = "bbvCrewPage";

    public DashboardService(
        IMemberService memberService,
        IContentService contentService,
        IUmbracoContextAccessor umbracoContextAccessor,
        ILogger<DashboardService> logger)
    {
        _memberService = memberService;
        _contentService = contentService;
        _umbracoContextAccessor = umbracoContextAccessor;
        _logger = logger;
    }

    public Task<DashboardData?> GetDashboardDataAsync(string memberEmail)
    {
        if (string.IsNullOrWhiteSpace(memberEmail))
        {
            return Task.FromResult<DashboardData?>(null);
        }

        var member = _memberService.GetByEmail(memberEmail);
        if (member == null)
        {
            _logger.LogWarning("Member not found for email: {Email}", memberEmail);
            return Task.FromResult<DashboardData?>(null);
        }

        var data = new DashboardData
        {
            Profile = new MemberProfileData
            {
                MemberId = member.Id,
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
        {
            data.CrewWishes = ParseCrewReferences(crewWishesValue);
        }

        // Get assigned crews
        var assignedCrewsValue = member.GetValue<string>("crews");
        if (!string.IsNullOrEmpty(assignedCrewsValue))
        {
            data.AssignedCrews = ParseCrewReferences(assignedCrewsValue);
        }

        // Shifts would be loaded here when the shift content type exists
        // For now, return empty list
        data.Shifts = new List<ShiftData>();

        _logger.LogDebug("Loaded dashboard data for {Email}: {WishCount} wishes, {AssignedCount} assigned crews",
            memberEmail, data.CrewWishes.Count, data.AssignedCrews.Count);

        return Task.FromResult<DashboardData?>(data);
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
