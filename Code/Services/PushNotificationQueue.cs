using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Code.Services;

// [CHANGE: web push notifications] Related: Code/Services/PushNotificationService.cs, Code/Notifications/PushNotificationComposer.cs, Web/Controllers/CrewSurfaceController.cs
/// <summary>
/// A push to send in the background. Either a whole crew (resolved when the job runs, so the
/// request returns immediately) or an explicit set of members.
/// </summary>
public sealed record PushJob(
    PushPayload Payload,
    int? CrewId = null,
    IReadOnlyCollection<Guid>? MemberKeys = null,
    Guid? ExcludeMemberKey = null);

public interface IPushNotificationQueue
{
    /// <summary>Queues a push. Returns false when push is not configured or the queue is full.</summary>
    bool Enqueue(PushJob job);
}

/// <summary>
/// In-memory queue drained by <see cref="PushNotificationWorker"/>. Sending to a big crew means
/// one HTTP request per device; doing that on the request thread (or in Task.Run with
/// request-scoped services) would slow down or break the page post.
/// Jobs still queued when the app restarts are lost — acceptable for notifications.
/// </summary>
public class PushNotificationQueue : IPushNotificationQueue
{
    private readonly Channel<PushJob> _channel = Channel.CreateBounded<PushJob>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private readonly IOptions<WebPushSettings> _settings;
    private readonly ILogger<PushNotificationQueue> _logger;

    public PushNotificationQueue(IOptions<WebPushSettings> settings, ILogger<PushNotificationQueue> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    internal ChannelReader<PushJob> Reader => _channel.Reader;

    public bool Enqueue(PushJob job)
    {
        if (!_settings.Value.IsConfigured)
            return false;

        if (_channel.Writer.TryWrite(job))
            return true;

        _logger.LogWarning("Push queue is full — dropped push \"{Title}\"", job.Payload.Title);
        return false;
    }
}

public class PushNotificationWorker : BackgroundService
{
    private readonly PushNotificationQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PushNotificationWorker> _logger;

    public PushNotificationWorker(PushNotificationQueue queue, IServiceScopeFactory scopeFactory, ILogger<PushNotificationWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

                IEnumerable<Guid> recipients = job.CrewId.HasValue
                    ? pushService.GetCrewMemberKeys(job.CrewId.Value)
                    : job.MemberKeys ?? Array.Empty<Guid>();

                if (job.ExcludeMemberKey.HasValue)
                    recipients = recipients.Where(k => k != job.ExcludeMemberKey.Value);

                var result = await pushService.SendToMembersAsync(recipients.ToList(), job.Payload, stoppingToken);

                _logger.LogInformation(
                    "Push \"{Title}\" (crew {CrewId}): {Sent}/{Devices} devices delivered, {Failed} failed, {Expired} expired subscriptions removed",
                    job.Payload.Title, job.CrewId, result.Sent, result.Devices, result.Failed, result.Expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Push job \"{Title}\" failed", job.Payload.Title);
            }
        }
    }
}
