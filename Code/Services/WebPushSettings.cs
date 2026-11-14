namespace Code.Services;

// [CHANGE: web push notifications] Related: Code/Services/PushNotificationService.cs, Web/appsettings.json ("WebPush" section)
/// <summary>
/// VAPID configuration for Web Push. Bound from the "WebPush" section in appsettings.
/// The private key is a secret — keep it out of source control (user secrets / environment variables).
/// When any value is missing, push is disabled: the opt-in UI is hidden and nothing is sent.
/// </summary>
public class WebPushSettings
{
    public const string SectionName = "WebPush";

    /// <summary>Contact for the push services, "mailto:..." or "https://...". Apple rejects localhost URLs.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Base64url-encoded uncompressed P-256 public key (65 bytes).</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Base64url-encoded P-256 private key (32 bytes). Secret.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Subject)
        && !string.IsNullOrWhiteSpace(PublicKey)
        && !string.IsNullOrWhiteSpace(PrivateKey);
}
