// [CHANGE: Billetto ticket status dashboard] Related: Web/Controllers/BillettoTicketStatusController.cs, Web/App_Plugins/BillettoTicketStatus/*, Web/Program.cs, Web/appsettings.json, Code/Services/BillettoApiClient.cs
// [CHANGE: Billetto ticket status dashboard] Related: Web/Controllers/BillettoTicketStatusController.cs, Web/App_Plugins/BillettoTicketStatus/*, Web/Program.cs, Web/appsettings.json
// [CHANGE: Billetto ordre property editor] Related: Web/Controllers/BillettoOrderController.cs, Web/App_Plugins/BillettoOrder/*
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
    Task<BillettoOrderLookupResult> GetOrderForMemberAsync(
        Guid memberKey,
        string? billettoIdOverride = null,
        string? altEmailOverride = null,
        bool forceRefresh = false);
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

public class BillettoOrderLookupResult
{
    public bool Configured { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public bool Found { get; set; }
    public string? MatchedBy { get; set; }   // "billettoId" | "altEmail" | "email"
    public string? BillettoId { get; set; }
    public JsonNode? Order { get; set; }     // rå Billetto ordre-JSON
    // [CHANGE: cache order on member] Related: Web/Controllers/BillettoOrderController.cs, Web/App_Plugins/BillettoOrder/billetto-order.js, Web/uSync/v17/DataTypes/BillettoOrdre.config
    public bool FromCache { get; set; }      // true = læst fra billettoOrderInfo, Billetto ikke kontaktet
    public DateTime? FetchedAt { get; set; } // hvornår data senest blev hentet fra Billetto (UTC)
}

public class MissingTicketMember
{
    public int MemberId { get; set; }
    public Guid MemberKey { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool UsesAltEmail { get; set; }
    public bool HasShift { get; set; }
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
    private readonly IScheduleService _scheduleService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly AppCaches _appCaches;
    private readonly ILogger<BillettoTicketService> _logger;

    public BillettoTicketService(
        IMemberService memberService,
        IContentService contentService,
        IScheduleService scheduleService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        AppCaches appCaches,
        ILogger<BillettoTicketService> logger)
    {
        _memberService = memberService;
        _contentService = contentService;
        _scheduleService = scheduleService;
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
        var assignedMemberKeys = await _scheduleService.GetAssignedMemberKeysAsync();

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
                UsesAltEmail = usesAltEmail,
                HasShift = assignedMemberKeys.Contains(member.Key)
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

    public async Task<BillettoOrderLookupResult> GetOrderForMemberAsync(
        Guid memberKey,
        string? billettoIdOverride = null,
        string? altEmailOverride = null,
        bool forceRefresh = false)
    {
        var keypair = _configuration["Billetto:Keypair"];
        var eventId = _configuration["Billetto:EventId"];

        if (string.IsNullOrWhiteSpace(keypair) || string.IsNullOrWhiteSpace(eventId))
        {
            return new BillettoOrderLookupResult
            {
                Configured = false,
                ErrorMessage = "Billetto er ikke konfigureret. Udfyld Billetto:Keypair og Billetto:EventId i appsettings."
            };
        }

        var member = _memberService.GetByKey(memberKey);
        if (member == null)
        {
            return new BillettoOrderLookupResult
            {
                ErrorMessage = "Medlemmet blev ikke fundet. Gem medlemmet og prøv igen."
            };
        }

        var usesOverride = !string.IsNullOrWhiteSpace(billettoIdOverride) || !string.IsNullOrWhiteSpace(altEmailOverride);
        // Overrides come from the (possibly unsaved) editor values; only persist the
        // fetched order on the member when they don't differ from the saved values
        var overridesDiffer =
            (!string.IsNullOrWhiteSpace(billettoIdOverride)
                && !string.Equals(billettoIdOverride.Trim(), member.GetValue<string>("billettoId")?.Trim(), StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(altEmailOverride)
                && !string.Equals(altEmailOverride.Trim(), member.GetValue<string>("altBillettoEmail")?.Trim(), StringComparison.OrdinalIgnoreCase));
        var billettoId = !string.IsNullOrWhiteSpace(billettoIdOverride)
            ? billettoIdOverride.Trim()
            : member.GetValue<string>("billettoId")?.Trim();
        var altEmail = !string.IsNullOrWhiteSpace(altEmailOverride)
            ? altEmailOverride.Trim()
            : member.GetValue<string>("altBillettoEmail")?.Trim();
        var email = (member.Email ?? string.Empty).Trim();

        // [CHANGE: cache order on member] Related: Web/Controllers/BillettoOrderController.cs, Web/App_Plugins/BillettoOrder/billetto-order.js, Web/uSync/v17/DataTypes/BillettoOrdre.config
        // Serve the order stored on the member unless a refresh is requested, so
        // opening a member doesn't hit Billetto every time
        if (!forceRefresh)
        {
            var cached = TryReadStoredOrder(member);
            if (cached != null)
            {
                return cached;
            }
        }

        var baseUrl = (_configuration["Billetto:BaseUrl"] ?? "https://billetto.dk").TrimEnd('/');
        var client = CreateBillettoClient(keypair);
        var result = new BillettoOrderLookupResult();

        try
        {
            // 1) Direct point read on the order id — cheap, no paging
            if (!string.IsNullOrWhiteSpace(billettoId))
            {
                var order = await TryGetOrderAsync(client, baseUrl, billettoId);
                if (order != null)
                {
                    result.Found = true;
                    result.MatchedBy = "billettoId";
                    result.BillettoId = billettoId;
                    result.Order = order;
                    result.FetchedAt = DateTime.UtcNow;
                    if (!overridesDiffer) PersistOrderOnMember(member, result);
                    return result;
                }
            }

            // 2) Fallback: match by e-mail against the cached order index —
            //    Billetto has no e-mail lookup endpoint
            if (forceRefresh)
            {
                _appCaches.RuntimeCache.ClearByKey(CacheKey);
            }

            var fetch = (await _appCaches.RuntimeCache.GetCacheItemAsync(
                CacheKey,
                () => StartOrJoinFetchAsync(keypair, eventId),
                TimeSpan.FromMinutes(10)))!;

            var lookup = new Dictionary<string, BillettoAttendee>();
            foreach (var attendee in fetch.Attendees)
            {
                AddToLookup(lookup, attendee.Email, attendee);
            }

            BillettoAttendee? match = null;
            if (!string.IsNullOrWhiteSpace(altEmail) && lookup.TryGetValue(altEmail.ToLowerInvariant(), out var altMatch))
            {
                match = altMatch;
                result.MatchedBy = "altEmail";
            }
            else if (!string.IsNullOrWhiteSpace(email) && lookup.TryGetValue(email.ToLowerInvariant(), out var emailMatch))
            {
                match = emailMatch;
                result.MatchedBy = "email";
            }

            if (match == null || string.IsNullOrWhiteSpace(match.Id))
            {
                result.MatchedBy = null;
                return result;
            }

            result.Found = true;
            result.BillettoId = match.Id;
            result.Order = await TryGetOrderAsync(client, baseUrl, match.Id);
            result.FetchedAt = DateTime.UtcNow;

            // Persist the matched order id (mirrors GetStatusAsync); never clear on a
            // lost match, and skip when unsaved override values drove the lookup
            if (!usesOverride && member.GetValue<string>("billettoId") != match.Id)
            {
                member.SetValue("billettoId", match.Id);
                _logger.LogInformation("Billetto: updated billettoId on member {MemberId} via order lookup", member.Id);
            }
            if (!overridesDiffer) PersistOrderOnMember(member, result);
            else if (member.IsDirty()) _memberService.Save(member);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Billetto: order lookup failed for member {MemberKey}", memberKey);
            result.ErrorMessage = $"Kunne ikke hente data fra Billetto: {ex.Message}";
            return result;
        }
    }

    // Returns the raw order JSON, or null on 404 so callers can fall back to e-mail
    private async Task<JsonNode?> TryGetOrderAsync(HttpClient client, string baseUrl, string orderId)
    {
        var response = await GetWithThrottleRetryAsync(client, $"{baseUrl}/api/v3/organiser/orders/{Uri.EscapeDataString(orderId)}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Billetto API svarede {(int)response.StatusCode}: {body}");
        }
        return JsonNode.Parse(body);
    }

    // [CHANGE: cache order on member] Related: Web/Controllers/BillettoOrderController.cs, Web/App_Plugins/BillettoOrder/billetto-order.js, Web/uSync/v17/DataTypes/BillettoOrdre.config
    // Stored on billettoOrderInfo as {"fetchedAt","matchedBy","billettoId","order"}
    private BillettoOrderLookupResult? TryReadStoredOrder(IMember member)
    {
        var raw = member.GetValue<string>("billettoOrderInfo");
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            var stored = JsonNode.Parse(raw);
            var order = stored?["order"];
            if (order == null) return null;

            return new BillettoOrderLookupResult
            {
                Found = true,
                FromCache = true,
                MatchedBy = stored!["matchedBy"]?.GetValue<string>(),
                BillettoId = stored["billettoId"]?.GetValue<string>(),
                FetchedAt = DateTime.TryParse(stored["fetchedAt"]?.GetValue<string>(), null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out var fetchedAt) ? fetchedAt : null,
                Order = order.DeepClone(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Billetto: could not parse stored order on member {MemberId}, refetching", member.Id);
            return null;
        }
    }

    private void PersistOrderOnMember(IMember member, BillettoOrderLookupResult result)
    {
        if (result.Order != null)
        {
            var stored = new JsonObject
            {
                ["fetchedAt"] = (result.FetchedAt ?? DateTime.UtcNow).ToString("O"),
                ["matchedBy"] = result.MatchedBy,
                ["billettoId"] = result.BillettoId,
                ["order"] = result.Order.DeepClone(),
            };
            member.SetValue("billettoOrderInfo", stored.ToJsonString());
        }

        if (member.IsDirty())
        {
            _memberService.Save(member);
        }
    }

    private HttpClient CreateBillettoClient(string keypair)
    {
        var client = _httpClientFactory.CreateClient("Billetto");
        client.DefaultRequestHeaders.Remove("Api-Keypair");
        client.DefaultRequestHeaders.Add("Api-Keypair", keypair);
        return client;
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
        var client = BillettoApiClient.CreateClient(_httpClientFactory, keypair);
        var api = new BillettoApiClient(
            _logger,
            onPageFetched: () => UpdateProgress(p => p.PagesFetched++),
            onRatelimit: (remaining, limit) => UpdateProgress(p =>
            {
                if (remaining != null) p.RatelimitRemaining = remaining;
                if (limit != null) p.RatelimitLimit = limit;
            }),
            onThrottleWait: seconds => UpdateProgress(p => p.ThrottledWaitSeconds = seconds));
        var client = CreateBillettoClient(keypair);

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
            var eventCreatedAt = await GetEventCreatedAtAsync(api, client, baseUrl, eventId);
            if (eventCreatedAt != null)
            {
                ordersUrl += $"&updated_after={eventCreatedAt.Value:yyyy-MM-dd}";
            }

            await api.FetchPagedAsync(client, baseUrl,
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
                _ => UpdateProgress(p => p.AttendeesFetched = attendees.Count));

            UpdateProgress(p => p.AttendeesFetched = attendees.Count);
        }
        finally
        {
            UpdateProgress(p => p.Active = false);
        }

        _logger.LogInformation("Billetto: fetched {Count} orders for event {EventId}", attendees.Count, eventId);
        return new BillettoFetch(attendees, DateTime.Now);
    }

    private async Task<DateTime?> GetEventCreatedAtAsync(BillettoApiClient api, HttpClient client, string baseUrl, string eventId)
    {
        try
        {
            var response = await api.GetWithThrottleRetryAsync(client, $"{baseUrl}/api/v3/organiser/events/{Uri.EscapeDataString(eventId)}");
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
