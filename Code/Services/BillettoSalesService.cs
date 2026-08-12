// [CHANGE: Billetto sales dashboard] Related: Web/Controllers/BillettoSalesController.cs, Web/App_Plugins/BillettoSales/*, Code/Services/BillettoApiClient.cs, Web/Program.cs
// [CHANGE: incremental sales sync] Full attendee scan only on first run; state is
// persisted to umbraco/Data/BillettoSalesCache.json and refreshes fetch only
// attendees changed since last sync (updated_after) — Billetto caps page size at
// 100 and rate-credits are shared with the other dashboards, so full scans on
// every refresh throttle the whole backoffice.
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Hosting;

namespace Code.Services;

public interface IBillettoSalesService
{
    Task<BillettoSalesResult> GetSalesAsync(bool forceRefresh = false);
    BillettoFetchProgress GetFetchProgress();
}

public class BillettoSalesResult
{
    public bool Configured { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public DateTime? FetchedAt { get; set; }
    public int TotalSold { get; set; }
    public int TotalCheckedIn { get; set; }
    public bool CheckInDataAvailable { get; set; }
    public int CancelledCount { get; set; }
    public List<BillettoTicketTypeSales> TicketTypes { get; set; } = new();
}

public class BillettoTicketTypeSales
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Sold { get; set; }
    public int CheckedIn { get; set; }
}

public class BillettoSalesService : IBillettoSalesService
{
    private const string CacheKey = "Billetto.Sales";
    private const string UnknownTypeKey = "__unknown";
    private const string UnknownTypeName = "Ukendt billettype";

    // Progress and single-flight state are statics on THIS class, deliberately
    // separate from BillettoTicketService's — the two dashboards must not
    // clobber each other's progress or join each other's fetches.
    private static readonly object ProgressLock = new();
    private static BillettoFetchProgress _progress = new();
    private static readonly object InflightLock = new();
    private static Task<BillettoSalesResult>? _inflightFetch;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly AppCaches _appCaches;
    private readonly ILogger<BillettoSalesService> _logger;
    private readonly IHostingEnvironment _hostingEnvironment;

    public BillettoSalesService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        AppCaches appCaches,
        ILogger<BillettoSalesService> logger,
        IHostingEnvironment hostingEnvironment)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _appCaches = appCaches;
        _logger = logger;
        _hostingEnvironment = hostingEnvironment;
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

    public async Task<BillettoSalesResult> GetSalesAsync(bool forceRefresh = false)
    {
        var keypair = _configuration["Billetto:Keypair"];
        var eventId = _configuration["Billetto:EventId"];

        if (string.IsNullOrWhiteSpace(keypair) || string.IsNullOrWhiteSpace(eventId))
        {
            return new BillettoSalesResult
            {
                Configured = false,
                ErrorMessage = "Billetto er ikke konfigureret. Udfyld Billetto:Keypair og Billetto:EventId i appsettings."
            };
        }

        if (forceRefresh)
        {
            _appCaches.RuntimeCache.ClearByKey(CacheKey);
        }

        try
        {
            return (await _appCaches.RuntimeCache.GetCacheItemAsync<BillettoSalesResult?>(
                CacheKey,
                async () => await StartOrJoinFetchAsync(keypair, eventId),
                TimeSpan.FromMinutes(10)))!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Billetto: failed to fetch sales for event {EventId}", eventId);
            return new BillettoSalesResult
            {
                ErrorMessage = $"Kunne ikke hente data fra Billetto: {ex.Message}"
            };
        }
    }

    private Task<BillettoSalesResult> StartOrJoinFetchAsync(string keypair, string eventId)
    {
        lock (InflightLock)
        {
            if (_inflightFetch == null || _inflightFetch.IsCompleted)
            {
                _inflightFetch = FetchSalesAsync(keypair, eventId);
            }
            return _inflightFetch;
        }
    }

    private async Task<BillettoSalesResult> FetchSalesAsync(string keypair, string eventId)
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
            // Incremental sync: everything already seen lives in the cache file, so
            // only attendees updated since last sync are fetched. Full scan only when
            // no cache exists (first run or deleted file).
            var cache = LoadCacheFile(eventId);
            var isFullScan = cache == null;
            cache ??= new SalesCacheFile { EventId = eventId };
            var syncStartedUtc = DateTime.UtcNow;

            if (cache.TypeNames.Count == 0)
            {
                cache.TypeNames = await FetchTicketTypeNamesAsync(api, client, baseUrl, eventId);
            }

            var attendeesUrl = $"{baseUrl}/api/v3/organiser/events/{Uri.EscapeDataString(eventId)}/attendees?limit=100";
            if (!isFullScan)
            {
                // Day-granular filter; one day of overlap so nothing slips between syncs.
                // Re-fetched attendees just overwrite their cache entry (idempotent).
                attendeesUrl += $"&updated_after={cache.SyncedThroughUtc.AddDays(-1):yyyy-MM-dd}";
            }

            var fetchedCount = 0;
            var sampleLogged = false;
            await api.FetchPagedAsync(client, baseUrl, attendeesUrl,
                item =>
                {
                    fetchedCount++;
                    if (!sampleLogged)
                    {
                        // Field-discovery aid: Billetto's attendee schema is not fully
                        // documented, so surface one raw sample per fetch at Debug level
                        _logger.LogDebug("Billetto: sample attendee JSON: {Json}", item.ToJsonString());
                        sampleLogged = true;
                    }

                    var id = item["id"]?.ToString();
                    if (string.IsNullOrWhiteSpace(id)) return;

                    var parsed = ParseAttendee(item, cache.TypeNames);
                    cache.Attendees[id] = new CachedAttendee
                    {
                        Type = parsed.TypeKey,
                        Cancelled = parsed.Cancelled,
                        CheckedIn = parsed.CheckedIn,
                    };
                },
                _ => UpdateProgress(p => p.AttendeesFetched = fetchedCount));

            cache.SyncedThroughUtc = syncStartedUtc;
            SaveCacheFile(cache);

            var result = AggregateFromCache(cache);
            _logger.LogInformation(
                "Billetto: sales sync fetched {Count} attendees ({Mode}) for event {EventId} ({Sold} sold, {CheckedIn} checked in, {Cancelled} cancelled)",
                fetchedCount, isFullScan ? "full" : "incremental", eventId, result.TotalSold, result.TotalCheckedIn, result.CancelledCount);
            return result;
        }
        finally
        {
            UpdateProgress(p => p.Active = false);
        }
    }

    private BillettoSalesResult AggregateFromCache(SalesCacheFile cache)
    {
        var result = new BillettoSalesResult { FetchedAt = DateTime.Now };
        var salesByType = new Dictionary<string, BillettoTicketTypeSales>();

        foreach (var attendee in cache.Attendees.Values)
        {
            if (attendee.Cancelled)
            {
                result.CancelledCount++;
                continue;
            }

            var typeKey = attendee.Type ?? UnknownTypeKey;
            if (!salesByType.TryGetValue(typeKey, out var typeSales))
            {
                var name = typeKey == UnknownTypeKey
                    ? UnknownTypeName
                    : cache.TypeNames.TryGetValue(typeKey, out var known) ? known : typeKey;
                typeSales = new BillettoTicketTypeSales { Id = typeKey, Name = name };
                salesByType[typeKey] = typeSales;
            }

            typeSales.Sold++;
            result.TotalSold++;

            if (attendee.CheckedIn != null)
            {
                result.CheckInDataAvailable = true;
                if (attendee.CheckedIn == true)
                {
                    typeSales.CheckedIn++;
                    result.TotalCheckedIn++;
                }
            }
        }

        // Sold-descending, unknown bucket last
        result.TicketTypes = salesByType.Values
            .OrderBy(t => t.Id == UnknownTypeKey ? 1 : 0)
            .ThenByDescending(t => t.Sold)
            .ToList();
        return result;
    }

    // Persisted sync state — one file per event id under umbraco/Data
    private sealed class SalesCacheFile
    {
        public string EventId { get; set; } = string.Empty;
        public DateTime SyncedThroughUtc { get; set; }
        public Dictionary<string, string> TypeNames { get; set; } = new();
        public Dictionary<string, CachedAttendee> Attendees { get; set; } = new();
    }

    private sealed class CachedAttendee
    {
        public string? Type { get; set; }
        public bool Cancelled { get; set; }
        public bool? CheckedIn { get; set; }
    }

    private string GetCacheFilePath(string eventId) =>
        _hostingEnvironment.MapPathContentRoot($"~/umbraco/Data/BillettoSalesCache-{eventId}.json");

    private SalesCacheFile? LoadCacheFile(string eventId)
    {
        try
        {
            var path = GetCacheFilePath(eventId);
            if (!File.Exists(path)) return null;
            var cache = JsonSerializer.Deserialize<SalesCacheFile>(File.ReadAllText(path));
            return cache?.EventId == eventId && cache.SyncedThroughUtc != default ? cache : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Billetto: could not read sales cache file, doing a full scan");
            return null;
        }
    }

    private void SaveCacheFile(SalesCacheFile cache)
    {
        try
        {
            var path = GetCacheFilePath(cache.EventId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch (Exception ex)
        {
            // Non-fatal: next refresh just falls back to a full scan
            _logger.LogWarning(ex, "Billetto: could not write sales cache file");
        }
    }

    private async Task<Dictionary<string, string>> FetchTicketTypeNamesAsync(BillettoApiClient api, HttpClient client, string baseUrl, string eventId)
    {
        var names = new Dictionary<string, string>();
        try
        {
            // NB: ticket_types is NOT nested under events — the nested URL 404s
            await api.FetchPagedAsync(client, baseUrl,
                $"{baseUrl}/api/v3/organiser/ticket_types?event_id={Uri.EscapeDataString(eventId)}&limit=100",
                item =>
                {
                    var id = item["id"]?.ToString();
                    if (string.IsNullOrWhiteSpace(id)) return;
                    var name = item["name"]?.GetValue<string>() ?? item["title"]?.GetValue<string>();
                    names[id] = string.IsNullOrWhiteSpace(name) ? id : name;
                });
        }
        catch (Exception ex)
        {
            // Non-fatal: names then come from attendee-embedded data or fall back to ids
            _logger.LogWarning(ex, "Billetto: could not fetch ticket types for event {EventId}", eventId);
        }
        return names;
    }

    private static readonly string[] CancelledStates = { "cancelled", "canceled", "refunded", "void", "voided" };
    private static readonly string[] CheckedInStates = { "checked_in", "used", "attended" };
    private static readonly string[] NotCheckedInStates = { "unused", "valid", "confirmed" };
    private static readonly string[] CheckInTimestampFields = { "checked_in_at", "used_at", "check_in_at" };

    // The attendee schema is undocumented; probe candidate fields and degrade
    // gracefully (unknown type bucket, tri-state check-in) instead of failing.
    private static ParsedAttendee ParseAttendee(JsonNode item, Dictionary<string, string> typeNames)
    {
        var (typeKey, typeName) = ResolveTicketType(item, typeNames);
        var state = (item["state"]?.ToString() ?? item["status"]?.ToString())?.Trim().ToLowerInvariant();

        var cancelled = state != null && CancelledStates.Contains(state);

        bool? checkedIn = null;
        var checkedInNode = item["checked_in"];
        if (checkedInNode is JsonValue boolValue && boolValue.TryGetValue<bool>(out var b))
        {
            checkedIn = b;
        }
        // Scan count is the reliable signal in practice: attendees embed a
        // scannings list, and a scanned ticket means checked in
        else if (item["scannings"]?["total"] is JsonValue scanValue && scanValue.TryGetValue<int>(out var scans))
        {
            checkedIn = scans > 0;
        }
        else if (CheckInTimestampFields.Any(f => !string.IsNullOrWhiteSpace(item[f]?.ToString())))
        {
            checkedIn = true;
        }
        else if (state != null && CheckedInStates.Contains(state))
        {
            checkedIn = true;
        }
        else if (state != null && NotCheckedInStates.Contains(state))
        {
            checkedIn = false;
        }

        return new ParsedAttendee(typeKey, typeName, cancelled, checkedIn);
    }

    private static (string Key, string Name) ResolveTicketType(JsonNode item, Dictionary<string, string> typeNames)
    {
        string? id = null;
        string? embeddedName = null;

        var typeNode = item["ticket_type"] ?? item["ticket"]?["ticket_type"];
        if (typeNode is JsonObject typeObject)
        {
            id = typeObject["id"]?.ToString();
            embeddedName = typeObject["name"]?.GetValue<string>() ?? typeObject["title"]?.GetValue<string>();
        }
        else if (typeNode != null)
        {
            id = typeNode.ToString();
        }
        id ??= item["ticket_type_id"]?.ToString();

        if (string.IsNullOrWhiteSpace(id))
        {
            return (UnknownTypeKey, UnknownTypeName);
        }

        var name = !string.IsNullOrWhiteSpace(embeddedName)
            ? embeddedName
            : typeNames.TryGetValue(id, out var known) ? known : id;
        return (id, name);
    }

    private sealed record ParsedAttendee(string TypeKey, string TypeName, bool Cancelled, bool? CheckedIn);
}
