namespace Code.Services;

public interface IMemberImpersonationService
{
    /// <summary>
    /// Start impersonating a member
    /// </summary>
    Task<bool> StartImpersonationAsync(string memberEmail);

    /// <summary>
    /// Stop impersonating and return to admin session
    /// </summary>
    Task StopImpersonationAsync();

    /// <summary>
    /// Check if currently impersonating
    /// </summary>
    bool IsImpersonating();

    /// <summary>
    /// Get the email of the member being impersonated
    /// </summary>
    string? GetImpersonatedMemberEmail();

    /// <summary>
    /// Get the email of the original admin who started impersonation
    /// </summary>
    string? GetOriginalAdminEmail();
}
