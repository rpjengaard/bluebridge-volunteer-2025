using Code.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Code.Notifications;

// [CHANGE: web push notifications] Related: Code/Services/PushNotificationService.cs, Code/Services/PushNotificationQueue.cs, Code/Migrations/AddPushSubscriptionTableMigration.cs
public class PushNotificationComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<WebPushSettings>(builder.Config.GetSection(WebPushSettings.SectionName));

        builder.Services.AddHttpClient(PushNotificationService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();
        builder.Services.AddSingleton<PushNotificationQueue>();
        builder.Services.AddSingleton<IPushNotificationQueue>(sp => sp.GetRequiredService<PushNotificationQueue>());
        builder.Services.AddHostedService<PushNotificationWorker>();

        builder.AddNotificationAsyncHandler<MemberDeletedNotification, PushMemberDeletedHandler>();
    }
}

/// <summary>Deleted members must not keep receiving pushes on their devices.</summary>
public class PushMemberDeletedHandler : INotificationAsyncHandler<MemberDeletedNotification>
{
    private readonly IPushNotificationService _pushService;
    private readonly ILogger<PushMemberDeletedHandler> _logger;

    public PushMemberDeletedHandler(IPushNotificationService pushService, ILogger<PushMemberDeletedHandler> logger)
    {
        _pushService = pushService;
        _logger = logger;
    }

    public async Task HandleAsync(MemberDeletedNotification notification, CancellationToken cancellationToken)
    {
        foreach (var member in notification.DeletedEntities)
        {
            try
            {
                await _pushService.RemoveAllForMemberAsync(member.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove push subscriptions for deleted member {MemberKey}", member.Key);
            }
        }
    }
}
