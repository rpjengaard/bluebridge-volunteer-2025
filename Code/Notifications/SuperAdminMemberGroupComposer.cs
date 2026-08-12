// [CHANGE: SuperAdmin ticket sales page] Related: Code/Services/SuperAdminService.cs, Web/Controllers/BbvTicketSalesController.cs, Web/Controllers/TicketSalesApiController.cs
using Code.Services;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Code.Notifications;

public class SuperAdminMemberGroupComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, SuperAdminMemberGroupHandler>();
    }
}

/// <summary>
/// Creates the SuperAdmin member group on startup if it does not exist, so
/// fresh databases (dev/prod) get it without manual backoffice setup.
/// Members are assigned to the group manually in the backoffice.
/// </summary>
public class SuperAdminMemberGroupHandler : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private readonly IMemberGroupService _memberGroupService;
    private readonly IRuntimeState _runtimeState;
    private readonly ILogger<SuperAdminMemberGroupHandler> _logger;

    public SuperAdminMemberGroupHandler(
        IMemberGroupService memberGroupService,
        IRuntimeState runtimeState,
        ILogger<SuperAdminMemberGroupHandler> logger)
    {
        _memberGroupService = memberGroupService;
        _runtimeState = runtimeState;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        // Skip while installing/upgrading — member tables may not be ready
        if (_runtimeState.Level != RuntimeLevel.Run) return;

        try
        {
            var existing = await _memberGroupService.GetByNameAsync(SuperAdminService.GroupName);
            if (existing != null) return;

            var attempt = await _memberGroupService.CreateAsync(new MemberGroup { Name = SuperAdminService.GroupName });
            if (attempt.Success)
            {
                _logger.LogInformation("Created member group '{GroupName}'", SuperAdminService.GroupName);
            }
            else
            {
                _logger.LogWarning("Could not create member group '{GroupName}': {Status}",
                    SuperAdminService.GroupName, attempt.Status);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: the group can still be created manually in the backoffice
            _logger.LogError(ex, "Failed ensuring member group '{GroupName}' exists", SuperAdminService.GroupName);
        }
    }
}
