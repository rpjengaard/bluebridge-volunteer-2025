namespace Code.Services;

// [CHANGE: web push notifications] Related: Code/Services/PushNotificationService.cs, Code/Services/PushNotificationQueue.cs, Web/Controllers/PushApiController.cs, Web/Controllers/PushStatsController.cs
public interface IPushNotificationService
{
    /// <summary>True when VAPID keys are configured. When false, nothing is shown or sent.</summary>
    bool IsConfigured { get; }

    /// <summary>The VAPID public key the browser needs for pushManager.subscribe(), or null when not configured.</summary>
    string? PublicKey { get; }

    /// <summary>Stores or refreshes a browser subscription. Re-subscribing the same endpoint moves it to this member.</summary>
    Task SaveSubscriptionAsync(Guid memberKey, string endpoint, string p256dh, string auth, string? userAgent);

    /// <summary>Removes the subscription when it belongs to the member. Returns false when nothing was removed.</summary>
    Task<bool> RemoveSubscriptionAsync(Guid memberKey, string endpoint);

    /// <summary>Removes every subscription of a member (used when the member is deleted).</summary>
    Task RemoveAllForMemberAsync(Guid memberKey);

    Task<int> GetDeviceCountAsync(Guid memberKey);

    /// <summary>Keys of the members who receive a crew's notifications: assigned to the crew, not rejected or cancelled.</summary>
    IReadOnlyCollection<Guid> GetCrewMemberKeys(int crewId);

    /// <summary>Push stats for one crew, for the scheduler view on the crew page. Null when the crew does not exist.</summary>
    Task<CrewPushStats?> GetCrewStatsAsync(int crewId);

    /// <summary>Push stats for every crew, for the backoffice dashboard.</summary>
    Task<PushOverviewStats> GetOverviewAsync();

    /// <summary>Sends to every device of the given members and prunes subscriptions the push service reports as gone.</summary>
    Task<PushSendResult> SendToMembersAsync(IReadOnlyCollection<Guid> memberKeys, PushPayload payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the endpoint belongs to a known browser push service. The server POSTs to the endpoint,
    /// so arbitrary URLs must be rejected (SSRF).
    /// </summary>
    bool IsAllowedEndpoint(string endpoint);
}

/// <summary>What the service worker receives and shows. Keep it short: push payloads are capped at ~4 KB.</summary>
public class PushPayload
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    /// <summary>Local path to open when the notification is clicked.</summary>
    public string Url { get; set; } = "/";
    /// <summary>Notifications with the same tag replace each other on the device.</summary>
    public string? Tag { get; set; }
}

public class PushSendResult
{
    public int Devices { get; set; }
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int Expired { get; set; }
}

public class CrewPushStats
{
    public int CrewId { get; set; }
    public string CrewName { get; set; } = string.Empty;
    /// <summary>Members who would receive the crew's notifications.</summary>
    public int MemberCount { get; set; }
    /// <summary>Of those, members with at least one device subscribed.</summary>
    public int SubscribedMemberCount { get; set; }
    public int DeviceCount { get; set; }
}

public class PushOverviewStats
{
    public bool Configured { get; set; }
    public int TotalDevices { get; set; }
    public int TotalSubscribedMembers { get; set; }
    public int DevicesIos { get; set; }
    public int DevicesAndroid { get; set; }
    public int DevicesDesktop { get; set; }
    public DateTime? LatestSubscriptionUtc { get; set; }
    public List<CrewPushStats> Crews { get; set; } = new();
}
