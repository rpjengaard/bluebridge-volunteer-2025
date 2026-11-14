using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Code.Migrations;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Code.Services;

// [CHANGE: web push notifications] Related: Code/Services/IPushNotificationService.cs, Code/Migrations/AddPushSubscriptionTableMigration.cs, Code/Services/PushNotificationQueue.cs
public class PushNotificationService : IPushNotificationService
{
    public const string HttpClientName = "WebPush";

    private const string CrewContentTypeAlias = "bbvCrewPage";
    private const int MaxParallelSends = 8;
    private const int SqlInChunkSize = 1000; // SQL Server allows ~2100 parameters per command
    private static readonly int TimeToLiveSeconds = (int)TimeSpan.FromHours(24).TotalSeconds;

    // Browser push services. The server POSTs to whatever endpoint a browser hands us,
    // so anything else is rejected to avoid server-side request forgery.
    private static readonly string[] AllowedEndpointHosts =
    {
        "fcm.googleapis.com",           // Chrome, Edge on Android, Samsung Internet, Opera, Brave (FCM)
        "android.googleapis.com",       // legacy FCM/GCM endpoints
    };

    private static readonly string[] AllowedEndpointHostSuffixes =
    {
        ".push.apple.com",              // Safari on iOS/iPadOS (installed web app) and macOS
        ".notify.windows.com",          // Edge on Windows (WNS)
        ".push.services.mozilla.com",   // Firefox (autopush)
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IScopeProvider _scopeProvider;
    private readonly IMemberService _memberService;
    private readonly IContentService _contentService;
    private readonly IContentTypeService _contentTypeService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebPushSettings _settings;
    private readonly ILogger<PushNotificationService> _logger;

    public PushNotificationService(
        IScopeProvider scopeProvider,
        IMemberService memberService,
        IContentService contentService,
        IContentTypeService contentTypeService,
        IHttpClientFactory httpClientFactory,
        IOptions<WebPushSettings> settings,
        ILogger<PushNotificationService> logger)
    {
        _scopeProvider = scopeProvider;
        _memberService = memberService;
        _contentService = contentService;
        _contentTypeService = contentTypeService;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsConfigured => _settings.IsConfigured;

    public string? PublicKey => IsConfigured ? _settings.PublicKey.Trim() : null;

    public bool IsAllowedEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > PushSubscriptionSchema.MaxEndpointLength)
            return false;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort)
            return false;

        var host = uri.Host.ToLowerInvariant();
        return AllowedEndpointHosts.Contains(host)
            || AllowedEndpointHostSuffixes.Any(suffix =>
                host.EndsWith(suffix, StringComparison.Ordinal) || host == suffix[1..]);
    }

    public Task SaveSubscriptionAsync(Guid memberKey, string endpoint, string p256dh, string auth, string? userAgent)
    {
        if (!IsAllowedEndpoint(endpoint))
            throw new ArgumentException("Ukendt push-tjeneste.");
        if (string.IsNullOrWhiteSpace(p256dh) || p256dh.Length > 200 || string.IsNullOrWhiteSpace(auth) || auth.Length > 100)
            throw new ArgumentException("Ugyldige krypteringsnøgler.");

        if (userAgent?.Length > 400)
            userAgent = userAgent[..400];

        var now = DateTime.UtcNow;

        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var db = scope.Database;

        var existing = db.SingleOrDefault<PushSubscriptionSchema>(
            "SELECT * FROM BbvPushSubscription WHERE Endpoint = @0", endpoint);

        if (existing != null)
        {
            // Same browser re-subscribing (page load sync, key rotation, or another member logging in on a shared device)
            existing.MemberKey = memberKey;
            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.UserAgent = userAgent;
            existing.LastSeenUtc = now;
            db.Update(existing);
        }
        else
        {
            db.Insert(new PushSubscriptionSchema
            {
                MemberKey = memberKey,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                UserAgent = userAgent,
                CreatedUtc = now,
                LastSeenUtc = now
            });
        }

        return Task.CompletedTask;
    }

    public Task<bool> RemoveSubscriptionAsync(Guid memberKey, string endpoint)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var removed = scope.Database.Execute(
            "DELETE FROM BbvPushSubscription WHERE Endpoint = @0 AND MemberKey = @1", endpoint, memberKey);
        return Task.FromResult(removed > 0);
    }

    public Task RemoveAllForMemberAsync(Guid memberKey)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        scope.Database.Execute("DELETE FROM BbvPushSubscription WHERE MemberKey = @0", memberKey);
        return Task.CompletedTask;
    }

    public Task<int> GetDeviceCountAsync(Guid memberKey)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var count = scope.Database.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM BbvPushSubscription WHERE MemberKey = @0", memberKey);
        return Task.FromResult(count);
    }

    public IReadOnlyCollection<Guid> GetCrewMemberKeys(int crewId)
    {
        var crew = _contentService.GetById(crewId);
        if (crew == null)
            return Array.Empty<Guid>();

        return GetActiveMembers()
            .Where(m => m.CrewKeys.Contains(crew.Key))
            .Select(m => m.Key)
            .ToList();
    }

    public Task<CrewPushStats?> GetCrewStatsAsync(int crewId)
    {
        var crew = _contentService.GetById(crewId);
        if (crew == null)
            return Task.FromResult<CrewPushStats?>(null);

        var memberKeys = GetActiveMembers()
            .Where(m => m.CrewKeys.Contains(crew.Key))
            .Select(m => m.Key)
            .ToHashSet();

        var devicesByMember = GetDeviceCountsByMember();

        return Task.FromResult<CrewPushStats?>(BuildCrewStats(crew.Id, crew.Name ?? string.Empty, memberKeys, devicesByMember));
    }

    public Task<PushOverviewStats> GetOverviewAsync()
    {
        var result = new PushOverviewStats { Configured = IsConfigured };

        List<PushSubscriptionSchema> subscriptions;
        using (var scope = _scopeProvider.CreateScope(autoComplete: true))
        {
            subscriptions = scope.Database.Fetch<PushSubscriptionSchema>(
                "SELECT Id, MemberKey, UserAgent, CreatedUtc FROM BbvPushSubscription");
        }

        var members = GetActiveMembers();
        var activeKeys = members.Select(m => m.Key).ToHashSet();

        // Only count devices of members that still exist and are active
        var live = subscriptions.Where(s => activeKeys.Contains(s.MemberKey)).ToList();
        result.TotalDevices = live.Count;
        result.TotalSubscribedMembers = live.Select(s => s.MemberKey).Distinct().Count();
        result.LatestSubscriptionUtc = live.Count > 0 ? live.Max(s => s.CreatedUtc) : null;
        foreach (var s in live)
        {
            switch (ClassifyPlatform(s.UserAgent))
            {
                case "ios": result.DevicesIos++; break;
                case "android": result.DevicesAndroid++; break;
                default: result.DevicesDesktop++; break;
            }
        }

        var devicesByMember = live
            .GroupBy(s => s.MemberKey)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var crew in GetAllCrews())
        {
            var memberKeys = members
                .Where(m => m.CrewKeys.Contains(crew.Key))
                .Select(m => m.Key)
                .ToHashSet();
            result.Crews.Add(BuildCrewStats(crew.Id, crew.Name ?? string.Empty, memberKeys, devicesByMember));
        }

        result.Crews = result.Crews.OrderBy(c => c.CrewName, StringComparer.Create(new System.Globalization.CultureInfo("da-DK"), true)).ToList();
        return Task.FromResult(result);
    }

    public async Task<PushSendResult> SendToMembersAsync(IReadOnlyCollection<Guid> memberKeys, PushPayload payload, CancellationToken cancellationToken = default)
    {
        var result = new PushSendResult();
        if (!IsConfigured || memberKeys.Count == 0)
            return result;

        var subscriptions = new List<PushSubscriptionSchema>();
        using (var scope = _scopeProvider.CreateScope(autoComplete: true))
        {
            foreach (var chunk in memberKeys.Distinct().Chunk(SqlInChunkSize))
            {
                subscriptions.AddRange(scope.Database.Fetch<PushSubscriptionSchema>(
                    "SELECT * FROM BbvPushSubscription WHERE MemberKey IN (@0)", chunk));
            }
        }

        result.Devices = subscriptions.Count;
        if (subscriptions.Count == 0)
            return result;

        VapidAuthentication vapid;
        try
        {
            vapid = new VapidAuthentication(_settings.PublicKey.Trim(), _settings.PrivateKey.Trim())
            {
                Subject = _settings.Subject.Trim()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web push VAPID configuration is invalid — check the WebPush section in appsettings");
            result.Failed = subscriptions.Count;
            return result;
        }

        var content = JsonSerializer.Serialize(payload, JsonOptions);
        var expiredIds = new ConcurrentBag<int>();
        var sent = 0;
        var failed = 0;

        using (vapid)
        {
            var client = new PushServiceClient(_httpClientFactory.CreateClient(HttpClientName))
            {
                DefaultAuthentication = vapid,
                AutoRetryAfter = true,
                MaxRetriesAfter = 2
            };

            await Parallel.ForEachAsync(
                subscriptions,
                new ParallelOptions { MaxDegreeOfParallelism = MaxParallelSends, CancellationToken = cancellationToken },
                async (row, token) =>
                {
                    var subscription = new PushSubscription
                    {
                        Endpoint = row.Endpoint,
                        Keys = new Dictionary<string, string>
                        {
                            ["p256dh"] = row.P256dh,
                            ["auth"] = row.Auth
                        }
                    };

                    var message = new PushMessage(content)
                    {
                        TimeToLive = TimeToLiveSeconds,
                        Urgency = PushMessageUrgency.High
                    };

                    try
                    {
                        await client.RequestPushMessageDeliveryAsync(subscription, message, token);
                        Interlocked.Increment(ref sent);
                    }
                    catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
                    {
                        // Browser unsubscribed, app removed from home screen, or permission revoked
                        expiredIds.Add(row.Id);
                    }
                    catch (PushServiceClientException ex)
                    {
                        Interlocked.Increment(ref failed);
                        _logger.LogWarning("Push to subscription {Id} failed: {Status} {Body}", row.Id, (int)ex.StatusCode, ex.Body);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Interlocked.Increment(ref failed);
                        _logger.LogWarning(ex, "Push to subscription {Id} failed", row.Id);
                    }
                });
        }

        if (!expiredIds.IsEmpty)
        {
            using var scope = _scopeProvider.CreateScope(autoComplete: true);
            foreach (var chunk in expiredIds.Chunk(SqlInChunkSize))
            {
                scope.Database.Execute("DELETE FROM BbvPushSubscription WHERE Id IN (@0)", chunk);
            }
        }

        result.Sent = sent;
        result.Failed = failed;
        result.Expired = expiredIds.Count;
        return result;
    }

    /// <summary>Creates a new VAPID key pair (base64url) for the "WebPush" config section.</summary>
    public static (string PublicKey, string PrivateKey) GenerateVapidKeys()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: true);

        var publicKey = new byte[65];
        publicKey[0] = 0x04; // uncompressed point
        parameters.Q.X!.CopyTo(publicKey, 1);
        parameters.Q.Y!.CopyTo(publicKey, 33);

        return (Base64UrlEncode(publicKey), Base64UrlEncode(parameters.D!));
    }

    /// <summary>Plain-text, length-capped notification body from rendered message HTML.</summary>
    public static string ToPlainSnippet(string? html, int maxLength = 140)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = System.Text.RegularExpressions.Regex.Replace(html, @"<(br|/p|/li)\s*/?>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]*>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        return text.Length <= maxLength ? text : text[..(maxLength - 1)].TrimEnd() + "…";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string ClassifyPlatform(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return "desktop";
        if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase))
            return "ios";
        if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
            return "android";
        return "desktop";
    }

    private static CrewPushStats BuildCrewStats(int crewId, string crewName, HashSet<Guid> memberKeys, Dictionary<Guid, int> devicesByMember)
    {
        var stats = new CrewPushStats
        {
            CrewId = crewId,
            CrewName = crewName,
            MemberCount = memberKeys.Count
        };

        foreach (var key in memberKeys)
        {
            if (devicesByMember.TryGetValue(key, out var devices) && devices > 0)
            {
                stats.SubscribedMemberCount++;
                stats.DeviceCount += devices;
            }
        }

        return stats;
    }

    private Dictionary<Guid, int> GetDeviceCountsByMember()
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        return scope.Database
            .Fetch<MemberDeviceCount>("SELECT MemberKey, COUNT(*) AS Devices FROM BbvPushSubscription GROUP BY MemberKey")
            .ToDictionary(r => r.MemberKey, r => r.Devices);
    }

    private sealed record ActiveMember(Guid Key, HashSet<Guid> CrewKeys);

    /// <summary>
    /// Members that can receive crew notifications: not rejected and not cancelled.
    /// Same exclusions as IsMemberAssignedToCrewAsync in CrewMessageService.
    /// </summary>
    private List<ActiveMember> GetActiveMembers()
    {
        var result = new List<ActiveMember>();
        foreach (var member in _memberService.GetAllMembers())
        {
            if (member.GetValue<bool>("rejected") || member.GetValue<bool>("cancelation"))
                continue;

            result.Add(new ActiveMember(member.Key, ParseCrewKeys(member.GetValue<string>("crews"))));
        }
        return result;
    }

    private static HashSet<Guid> ParseCrewKeys(string? crewsValue)
    {
        var keys = new HashSet<Guid>();
        if (string.IsNullOrWhiteSpace(crewsValue))
            return keys;

        foreach (var part in crewsValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("umb://document/", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(trimmed["umb://document/".Length..], out var key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private IEnumerable<IContent> GetAllCrews()
    {
        var crewType = _contentTypeService.Get(CrewContentTypeAlias);
        if (crewType == null)
            return Enumerable.Empty<IContent>();

        return _contentService
            .GetPagedOfType(crewType.Id, 0, 10_000, out _, null!)
            .Where(c => !c.Trashed);
    }

    // NPoco projection for the GROUP BY query
    public sealed class MemberDeviceCount
    {
        public Guid MemberKey { get; set; }
        public int Devices { get; set; }
    }
}
