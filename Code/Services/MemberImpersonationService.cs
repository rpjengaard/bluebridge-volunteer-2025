using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Security;

namespace Code.Services;

public class MemberImpersonationService : IMemberImpersonationService
{
    private const string ImpersonationSessionKey = "MemberImpersonation_IsActive";
    private const string ImpersonatedMemberEmailKey = "MemberImpersonation_MemberEmail";
    private const string OriginalAdminEmailKey = "MemberImpersonation_AdminEmail";

    private readonly IMemberManager _memberManager;
    private readonly IMemberSignInManager _memberSignInManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MemberImpersonationService> _logger;

    public MemberImpersonationService(
        IMemberManager memberManager,
        IMemberSignInManager memberSignInManager,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MemberImpersonationService> logger)
    {
        _memberManager = memberManager;
        _memberSignInManager = memberSignInManager;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<bool> StartImpersonationAsync(string memberEmail)
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session == null)
        {
            _logger.LogWarning("Cannot start impersonation: Session is not available");
            return false;
        }

        // Get the member to impersonate
        var targetMember = await _memberManager.FindByEmailAsync(memberEmail);
        if (targetMember == null)
        {
            _logger.LogWarning("Cannot start impersonation: Member {Email} not found", memberEmail);
            return false;
        }

        // Get current member (admin)
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        var adminEmail = currentMember?.Email ?? "unknown";

        // Sign in as the target member (without password check)
        await _memberSignInManager.SignInAsync(targetMember, isPersistent: false);

        // Store impersonation state in session
        session.SetString(ImpersonationSessionKey, "true");
        session.SetString(ImpersonatedMemberEmailKey, memberEmail);
        session.SetString(OriginalAdminEmailKey, adminEmail);

        _logger.LogInformation("Admin {AdminEmail} started impersonating member {MemberEmail}",
            adminEmail, memberEmail);

        return true;
    }

    public async Task StopImpersonationAsync()
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session == null)
        {
            _logger.LogWarning("Cannot stop impersonation: Session is not available");
            return;
        }

        var impersonatedEmail = session.GetString(ImpersonatedMemberEmailKey);
        var adminEmail = session.GetString(OriginalAdminEmailKey);

        // Sign out the impersonated member
        await _memberSignInManager.SignOutAsync();

        // Clear impersonation session data
        session.Remove(ImpersonationSessionKey);
        session.Remove(ImpersonatedMemberEmailKey);
        session.Remove(OriginalAdminEmailKey);

        _logger.LogInformation("Admin {AdminEmail} stopped impersonating member {MemberEmail}",
            adminEmail, impersonatedEmail);
    }

    public bool IsImpersonating()
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session == null) return false;

        var value = session.GetString(ImpersonationSessionKey);
        return value == "true";
    }

    public string? GetImpersonatedMemberEmail()
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session == null) return null;

        return session.GetString(ImpersonatedMemberEmailKey);
    }

    public string? GetOriginalAdminEmail()
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session == null) return null;

        return session.GetString(OriginalAdminEmailKey);
    }
}
