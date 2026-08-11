// [CHANGE: Billetto ticket status dashboard] Related: Web/Controllers/BillettoTicketStatusController.cs, Web/App_Plugins/BillettoTicketStatus/*, Web/Program.cs, Web/appsettings.json
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace Code.Services;

public interface IBillettoTicketService
{
    Task<BillettoTicketStatusResult> GetStatusAsync(bool forceRefresh = false);
    BillettoFetchProgress GetFetchProgress();
}

// Live progress for the currently running Billetto fetch (polled by the dashboard)
public class BillettoFetchProgress
{
    public bool Active { get; set; }
    public int PagesFetched { get; set; }
    public int AttendeesFetched { get; set; }
    public int? RatelimitRemaining { get; set; }
    public int? RatelimitLimit { get; set; }
    public double? ThrottledWaitSeconds { get; set; }
}

public class BillettoTicketStatusResult
{
    public bool Configured { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public DateTime? FetchedAt { get; set; }
    public int TotalChecked { get; set; }
    public int WithTicket { get; set; }
    public int ExemptCount { get; set; }
    public List<MissingTicketMember> MissingMembers { get; set; } = new();
}

public class MissingTicketMember
{
    public int MemberId { get; set; }
    public Guid MemberKey { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool UsesAltEmail { get; set; }
    public List<string> CrewNames { get; set; } = new();
}

public class BillettoTicketService : IBillettoTicketService
{
    private const string CacheKey = "Billetto.Attendees";
    private static readonly object ProgressLock = new();
    private static BillettoFetchProgress _progress = new();

    // Single-flight: the dashboard element can mount twice, and concurrent cache
    // misses would otherwise start duplicate (expensive) Billetto fetches.
    private static readonly object InflightLock = new();
    private static Task<BillettoFetch>? _inflightFetch;
    private const string VolunteerGroup = "Frivillige";
    private static readonly string[] ExcludedGroups = { "Sherif", "Vice sherif", "Vagtplanlæggere" };

    private readonly IMemberService _memberService;
    private readonly IContentService _contentService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly AppCaches _appCaches;
    private readonly ILogger<BillettoTicketService> _logger;

    public BillettoTicketService(
        IMemberService memberService,
        IContentService contentService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        AppCaches appCaches,
        ILogger<BillettoTicketService> logger)
    {
        _memberService = memberService;
        _contentService = contentService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _appCaches = appCaches;
        _logger = logger;
    }

    public BillettoFetchProgress GetFetchProgress()
    {
        lock (ProgressLock)
        {
            return new BillettoFetchProgress
            {
                Active = _progress.Active,
                PagesFetched = _progress.PagesFetched,
                AttendeesFetched = _progress.AttendeesFetched,
                RatelimitRemaining = _progress.RatelimitRemaining,
                RatelimitLimit = _progress.RatelimitLimit,
                ThrottledWaitSeconds = _progress.ThrottledWaitSeconds
            };
        }
    }

    private static void UpdateProgress(Action<BillettoFetchProgress> update)
    {
        lock (ProgressLock)
        {
            update(_progress);
        }
    }

    public async Task<BillettoTicketStatusResult> GetStatusAsync(bool forceRefresh = false)
    {
        var keypair = _configuration["Billetto:Keypair"];
        var eventId = _configuration["Billetto:EventId"];

        if (string.IsNullOrWhiteSpace(keypair) || string.IsNullOrWhiteSpace(eventId))
        {
            return new BillettoTicketStatusResult
            {
                Configured = false,
                ErrorMessage = "Billetto er ikke konfigureret. Udfyld Billetto:Keypair og Billetto:EventId i appsettings."
            };
        }

        if (forceRefresh)
        {
            _appCaches.RuntimeCache.ClearByKey(CacheKey);
        }

        BillettoFetch fetch;
        try
        {
            fetch = (await _appCaches.RuntimeCache.GetCacheItemAsync(
                CacheKey,
                () => StartOrJoinFetchAsync(keypair, eventId),
                TimeSpan.FromMinutes(10)))!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Billetto: failed to fetch attendees for event {EventId}", eventId);
            return new BillettoTicketStatusResult
            {
                ErrorMessage = $"Kunne ikke hente data fra Billetto: {ex.Message}"
            };
        }

        var result = new BillettoTicketStatusResult { FetchedAt = fetch.FetchedAt };

        // Buyer email (lowercase) -> order; first order wins on duplicates.
        var lookup = new Dictionary<string, BillettoAttendee>();
        foreach (var attendee in fetch.Attendees)
        {
            AddToLookup(lookup, attendee.Email, attendee);
        }

        var volunteerIds = GetMemberIdsInRole(VolunteerGroup);
        var excludedIds = new HashSet<int>();
        foreach (var group in ExcludedGroups)
        {
            excludedIds.UnionWith(GetMemberIdsInRole(group));
        }

        var membersToSave = new List<IMember>();
        var missingCrewUdis = new Dictionary<int, List<Guid>>();

        foreach (var member in _memberService.GetAllMembers())
        {
            if (!member.IsApproved) continue;
            if (!member.GetValue<bool>("accept2026")) continue;
            if (member.GetValue<bool>("cancelation")) continue;
            if (!volunteerIds.Contains(member.Id) || excludedIds.Contains(member.Id)) continue;

            if (member.GetValue<bool>("ticketNotNeeded"))
            {
                result.ExemptCount++;
                continue;
            }

            result.TotalChecked++;

            var altEmail = member.GetValue<string>("altBillettoEmail")?.Trim();
            var usesAltEmail = !string.IsNullOrWhiteSpace(altEmail);
            var email = usesAltEmail ? altEmail! : (member.Email ?? string.Empty).Trim();

            BillettoAttendee? match = null;
            if (!string.IsNullOrWhiteSpace(email))
            {
                lookup.TryGetValue(email.ToLowerInvariant(), out match);
            }

            if (match != null)
            {
                result.WithTicket++;

                // Persist the matched order id; never clear on a lost match
                if (member.GetValue<string>("billettoId") != match.Id)
                {
                    member.SetValue("billettoId", match.Id);
                    membersToSave.Add(member);
                }
                continue;
            }

            var firstName = member.GetValue<string>("firstName") ?? string.Empty;
            var lastName = member.GetValue<string>("lastName") ?? string.Empty;
            var fullName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrEmpty(fullName)) fullName = member.Name ?? email;

            var missing = new MissingTicketMember
            {
                MemberId = member.Id,
                MemberKey = member.Key,
                FullName = fullName,
                Email = email,
                UsesAltEmail = usesAltEmail
            };
            result.MissingMembers.Add(missing);
            missingCrewUdis[member.Id] = ParseCrewGuids(member.GetValue<string>("crews"));
        }

        ResolveCrewNames(result.MissingMembers, missingCrewUdis);
        result.MissingMembers.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.CurrentCultureIgnoreCase));

        foreach (var member in membersToSave)
        {
            _memberService.Save(member);
        }
        if (membersToSave.Count > 0)
        {
            _logger.LogInformation("Billetto: updated billettoId on {Count} members", membersToSave.Count);
        }

        return result;
    }

    private static void AddToLookup(Dictionary<string, BillettoAttendee> lookup, string? email, BillettoAttendee attendee)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        var key = email.Trim().ToLowerInvariant();

        lookup.TryAdd(key, attendee);
    }

    private HashSet<int> GetMemberIdsInRole(string roleName)
    {
        var ids = new HashSet<int>();
        try
        {
            foreach (var member in _memberService.GetMembersInRole(roleName))
            {
                ids.Add(member.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Billetto: could not resolve member group {Group}", roleName);
        }
        return ids;
    }

    private Task<BillettoFetch> StartOrJoinFetchAsync(string keypair, string eventId)
    {
        lock (InflightLock)
        {
            if (_inflightFetch == null || _inflightFetch.IsCompleted)
            {
                _inflightFetch = FetchAllAttendeesAsync(keypair, eventId);
            }
            return _inflightFetch;
        }
    }

    private async Task<BillettoFetch> FetchAllAttendeesAsync(string keypair, string eventId)
    {
        var baseUrl = (_configuration["Billetto:BaseUrl"] ?? "https://billetto.dk").TrimEnd('/');
        var client = _httpClientFactory.CreateClient("Billetto");
        client.DefaultRequestHeaders.Remove("Api-Keypair");
        client.DefaultRequestHeaders.Add("Api-Keypair", keypair);

        var attendees = new List<BillettoAttendee>();

        UpdateProgress(p =>
        {
            p.Active = true;
            p.PagesFetched = 0;
            p.AttendeesFetched = 0;
            p.RatelimitRemaining = null;
            p.RatelimitLimit = null;
            p.ThrottledWaitSeconds = null;
        });

        try
        {
            // Only orders are fetched: volunteers buy their own ticket, so they appear
            // as the order buyer — and altBillettoEmail covers the bought-by-someone-else
            // case. Attendee-level data (incl. ticket type) is deliberately skipped; it
            // tripled the page count and its expand blew the rate limit budget.
            // The orders endpoint paginates across ALL the account's events historically;
            // updated_after=<event created_at> skips old events (an order for this event
            // cannot predate the event itself).
            var ordersUrl = $"{baseUrl}/api/v3/organiser/orders?event_id={Uri.EscapeDataString(eventId)}&limit=100";
            var eventCreatedAt = await GetEventCreatedAtAsync(client, baseUrl, eventId);
            if (eventCreatedAt != null)
            {
                ordersUrl += $"&updated_after={eventCreatedAt.Value:yyyy-MM-dd}";
            }

            await FetchPagedAsync(client, baseUrl,
                ordersUrl,
                item =>
                {
                    // Billetto's event_id filter scopes data but the endpoint spans all
                    // events historically — guard against orders from other events
                    var orderEvent = item["event"]?.ToString();
                    if (orderEvent != null && orderEvent != eventId) return;

                    var email = item["email"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(email)) return;

                    attendees.Add(new BillettoAttendee(
                        Id: item["id"]?.GetValue<string>() ?? string.Empty,
                        Email: email));
                },
                () => attendees.Count);

            UpdateProgress(p => p.AttendeesFetched = attendees.Count);
        }
        finally
        {
            UpdateProgress(p => p.Active = false);
        }

        _logger.LogInformation("Billetto: fetched {Count} orders for event {EventId}", attendees.Count, eventId);
        return new BillettoFetch(attendees, DateTime.Now);
    }

    private async Task<DateTime?> GetEventCreatedAtAsync(HttpClient client, string baseUrl, string eventId)
    {
        try
        {
            var response = await GetWithThrottleRetryAsync(client, $"{baseUrl}/api/v3/organiser/events/{Uri.EscapeDataString(eventId)}");
            if (!response.IsSuccessStatusCode) return null;

            var root = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var createdAt = root?["created_at"]?.GetValue<string>();
            return DateTime.TryParse(createdAt, out var dt) ? dt : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Billetto: could not read event created_at, fetching orders unfiltered by date");
            return null;
        }
    }

    private async Task FetchPagedAsync(HttpClient client, string baseUrl, string url, Action<JsonNode> handleItem, Func<int>? currentCount = null)
    {
        var pageGuard = 0;
        string? nextUrl = url;

        // Billetto's has_more is broken: it stays true past the last page, serves an
        // empty page, then loops the cursor back to the start. Track seen ids and stop
        // on an empty page or a page with no new items.
        var seenIds = new HashSet<string>();

        while (!string.IsNullOrEmpty(nextUrl) && pageGuard++ < 200)
        {
            var response = await GetWithThrottleRetryAsync(client, nextUrl);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Billetto API svarede {(int)response.StatusCode}: {body}");
            }

            var hasRemaining = TryGetHeaderInt(response, "X-Ratelimit-Remaining", out var remaining);
            var hasLimit = TryGetHeaderInt(response, "X-Ratelimit-Limit", out var limit);
            UpdateProgress(p =>
            {
                p.PagesFetched++;
                if (hasRemaining) p.RatelimitRemaining = remaining;
                if (hasLimit) p.RatelimitLimit = limit;
            });

            var root = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var data = root?["data"]?.AsArray();
            if (data == null || data.Count == 0) return;

            var newItems = 0;
            foreach (var item in data)
            {
                if (item == null) continue;
                var id = item["id"]?.GetValue<string>();
                if (id != null && !seenIds.Add(id)) continue;
                newItems++;
                handleItem(item);
            }
            if (newItems == 0) return;

            if (currentCount != null)
            {
                var count = currentCount();
                UpdateProgress(p => p.AttendeesFetched = count);
            }

            var hasMore = root?["has_more"]?.GetValue<bool>() ?? false;
            var next = root?["next_url"]?.GetValue<string>();
            if (!hasMore || string.IsNullOrWhiteSpace(next))
            {
                nextUrl = null;
            }
            else
            {
                nextUrl = next.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? next : baseUrl + next;
                // Gentle pacing between pages instead of reactive long pauses
                await Task.Delay(300);
            }
        }
    }

    // Billetto throttles aggressively; on 429 wait the announced time and retry
    private async Task<HttpResponseMessage> GetWithThrottleRetryAsync(HttpClient client, string url)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await client.GetAsync(url);
            if ((int)response.StatusCode != 429 || attempt >= 3)
            {
                return response;
            }

            var waitSeconds = response.Headers.RetryAfter?.Delta?.TotalSeconds;
            if (waitSeconds == null)
            {
                var body = await response.Content.ReadAsStringAsync();
                var match = System.Text.RegularExpressions.Regex.Match(body, @"(\d+)\s*second");
                waitSeconds = match.Success ? int.Parse(match.Groups[1].Value) : 30;
            }

            var wait = TimeSpan.FromSeconds(Math.Clamp(waitSeconds.Value + 1, 2, 90));
            _logger.LogWarning("Billetto: throttled (429), waiting {Wait}s before retry {Attempt}/3", wait.TotalSeconds, attempt + 1);
            UpdateProgress(p => { p.RatelimitRemaining = 0; p.ThrottledWaitSeconds = wait.TotalSeconds; });
            await Task.Delay(wait);
            UpdateProgress(p => p.ThrottledWaitSeconds = null);
        }
    }

    private static bool TryGetHeaderInt(HttpResponseMessage response, string header, out int value)
    {
        value = 0;
        return response.Headers.TryGetValues(header, out var values)
            && int.TryParse(values.FirstOrDefault(), out value);
    }

    private static List<Guid> ParseCrewGuids(string? udiString)
    {
        var guids = new List<Guid>();
        if (string.IsNullOrWhiteSpace(udiString)) return guids;

        foreach (var part in udiString.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            const string prefix = "umb://document/";
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(trimmed[prefix.Length..], out var guid))
            {
                guids.Add(guid);
            }
        }
        return guids;
    }

    private void ResolveCrewNames(List<MissingTicketMember> members, Dictionary<int, List<Guid>> crewGuidsByMemberId)
    {
        var allGuids = crewGuidsByMemberId.Values.SelectMany(g => g).Distinct().ToList();
        if (allGuids.Count == 0) return;

        var nameByGuid = new Dictionary<Guid, string>();
        foreach (var guid in allGuids)
        {
            var content = _contentService.GetById(guid);
            if (content?.Name != null)
            {
                nameByGuid[guid] = content.Name;
            }
        }

        foreach (var member in members)
        {
            if (!crewGuidsByMemberId.TryGetValue(member.MemberId, out var guids)) continue;
            member.CrewNames = guids
                .Where(nameByGuid.ContainsKey)
                .Select(g => nameByGuid[g])
                .ToList();
        }
    }

    private sealed record BillettoFetch(List<BillettoAttendee> Attendees, DateTime FetchedAt);

    private sealed record BillettoAttendee(string Id, string? Email);
}
