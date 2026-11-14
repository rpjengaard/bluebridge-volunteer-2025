// [CHANGE: web push notifications] Related: Code/Services/PushNotificationService.cs, Web/wwwroot/js/push.js, Web/Views/Partials/_PushOptIn.cshtml
using Code.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Umbraco.Cms.Core.Security;

namespace Web.Controllers;

/// <summary>
/// Frontend JSON endpoints for a member's own push subscriptions. Plain [Route] controller like
/// TicketSalesApiController. All state-changing calls are POST + antiforgery (header
/// "RequestVerificationToken").
/// </summary>
[Route("umbraco/surface/push")]
public class PushApiController : Controller
{
    private static readonly TimeSpan TestCooldown = TimeSpan.FromSeconds(30);

    private readonly IPushNotificationService _pushService;
    private readonly IPushNotificationQueue _pushQueue;
    private readonly IMemberManager _memberManager;
    private readonly IMemberImpersonationService _impersonationService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<PushApiController> _logger;

    public PushApiController(
        IPushNotificationService pushService,
        IPushNotificationQueue pushQueue,
        IMemberManager memberManager,
        IMemberImpersonationService impersonationService,
        IMemoryCache memoryCache,
        ILogger<PushApiController> logger)
    {
        _pushService = pushService;
        _pushQueue = pushQueue;
        _memberManager = memberManager;
        _impersonationService = impersonationService;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public class SubscriptionKeysModel
    {
        public string? P256dh { get; set; }
        public string? Auth { get; set; }
    }

    // Shape of PushSubscription.toJSON() in the browser
    public class SubscribeModel
    {
        public string? Endpoint { get; set; }
        public SubscriptionKeysModel? Keys { get; set; }
    }

    public class UnsubscribeModel
    {
        public string? Endpoint { get; set; }
    }

    // POST /umbraco/surface/push/subscribe
    [HttpPost("subscribe")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeModel model)
    {
        var (memberKey, denied) = await AuthorizeAsync(blockImpersonation: true);
        if (denied != null) return denied;

        if (string.IsNullOrWhiteSpace(model.Endpoint) || model.Keys?.P256dh == null || model.Keys.Auth == null)
            return BadRequest(new { success = false, message = "Ugyldigt abonnement." });

        if (!_pushService.IsAllowedEndpoint(model.Endpoint))
            return BadRequest(new { success = false, message = "Ukendt push-tjeneste." });

        try
        {
            await _pushService.SaveSubscriptionAsync(memberKey, model.Endpoint, model.Keys.P256dh, model.Keys.Auth,
                Request.Headers.UserAgent.ToString());
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }

        return Json(new { success = true, devices = await _pushService.GetDeviceCountAsync(memberKey) });
    }

    // POST /umbraco/surface/push/unsubscribe
    [HttpPost("unsubscribe")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeModel model)
    {
        // Unsubscribing is allowed while impersonating: it can only remove the member's own rows for this browser
        var (memberKey, denied) = await AuthorizeAsync(blockImpersonation: false);
        if (denied != null) return denied;

        if (!string.IsNullOrWhiteSpace(model.Endpoint))
            await _pushService.RemoveSubscriptionAsync(memberKey, model.Endpoint);

        return Json(new { success = true, devices = await _pushService.GetDeviceCountAsync(memberKey) });
    }

    // POST /umbraco/surface/push/test — sends a test notification to the member's own devices
    [HttpPost("test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTest()
    {
        var (memberKey, denied) = await AuthorizeAsync(blockImpersonation: true);
        if (denied != null) return denied;

        var cacheKey = $"push-test:{memberKey}";
        if (_memoryCache.TryGetValue(cacheKey, out _))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { success = false, message = "Vent lidt før du sender endnu en test." });
        _memoryCache.Set(cacheKey, true, TestCooldown);

        var queued = _pushQueue.Enqueue(new PushJob(
            new PushPayload
            {
                Title = "Blue Bridge Frivillig",
                Body = "Det virker! Du får nu besked her, når dit crew skriver til dig.",
                Url = "/",
                Tag = "test"
            },
            MemberKeys: new[] { memberKey }));

        return Json(new { success = queued });
    }

    private async Task<(Guid MemberKey, IActionResult? Denied)> AuthorizeAsync(bool blockImpersonation)
    {
        if (!_pushService.IsConfigured)
            return (Guid.Empty, StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { success = false, message = "Push-notifikationer er ikke sat op." }));

        var member = await _memberManager.GetCurrentMemberAsync();
        if (member == null)
            return (Guid.Empty, Unauthorized(new { success = false, message = "Du er ikke logget ind." }));

        // An admin impersonating a member must not register the admin's browser as the member's device
        if (blockImpersonation && _impersonationService.IsImpersonating())
            return (Guid.Empty, StatusCode(StatusCodes.Status403Forbidden,
                new { success = false, message = "Push kan ikke slås til under impersonation." }));

        return (member.Key, null);
    }
}
