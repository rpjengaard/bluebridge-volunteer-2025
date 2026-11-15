using Microsoft.Extensions.DependencyInjection;
using NPoco;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;

namespace Code.Migrations;

// [CHANGE: web push notifications] Related: Code/Services/PushNotificationService.cs, Web/Controllers/PushApiController.cs
/// <summary>
/// One row per browser/device push subscription. A member can have several (phone + laptop).
/// The endpoint is unique: re-subscribing the same browser updates the row instead of adding one.
/// </summary>
[TableName("BbvPushSubscription")]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class PushSubscriptionSchema
{
    public const int MaxEndpointLength = 800;

    [Column("Id")]
    public int Id { get; set; }

    [Column("MemberKey")]
    public Guid MemberKey { get; set; }

    [Column("Endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [Column("P256dh")]
    public string P256dh { get; set; } = string.Empty;

    [Column("Auth")]
    public string Auth { get; set; } = string.Empty;

    [Column("UserAgent")]
    public string? UserAgent { get; set; }

    [Column("CreatedUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("LastSeenUtc")]
    public DateTime LastSeenUtc { get; set; }
}

public class AddPushSubscriptionTableMigration : MigrationBase
{
    public AddPushSubscriptionTableMigration(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        // Raw SQL like the crew message migrations (IDENTITY must be set reliably on Azure SQL).
        // NVARCHAR(800) keeps the unique index under SQL Server's 1700-byte key limit.
        Execute.Sql(@"
            IF OBJECT_ID('dbo.BbvPushSubscription', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.BbvPushSubscription (
                    Id INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    MemberKey UNIQUEIDENTIFIER NOT NULL,
                    Endpoint NVARCHAR(800) NOT NULL,
                    P256dh NVARCHAR(200) NOT NULL,
                    Auth NVARCHAR(100) NOT NULL,
                    UserAgent NVARCHAR(400) NULL,
                    CreatedUtc DATETIME NOT NULL,
                    LastSeenUtc DATETIME NOT NULL
                );
                CREATE UNIQUE NONCLUSTERED INDEX UX_BbvPushSubscription_Endpoint
                    ON dbo.BbvPushSubscription (Endpoint);
                CREATE NONCLUSTERED INDEX IX_BbvPushSubscription_MemberKey
                    ON dbo.BbvPushSubscription (MemberKey);
            END").Do();
    }
}

public class PushSubscriptionMigrationComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Components().Append<PushSubscriptionMigrationComponent>();
    }
}

public class PushSubscriptionMigrationComponent : IComponent
{
    private readonly ICoreScopeProvider _coreScopeProvider;
    private readonly IMigrationPlanExecutor _migrationPlanExecutor;
    private readonly IKeyValueService _keyValueService;
    private readonly IRuntimeState _runtimeState;

    public PushSubscriptionMigrationComponent(
        ICoreScopeProvider coreScopeProvider,
        IMigrationPlanExecutor migrationPlanExecutor,
        IKeyValueService keyValueService,
        IRuntimeState runtimeState)
    {
        _coreScopeProvider = coreScopeProvider;
        _migrationPlanExecutor = migrationPlanExecutor;
        _keyValueService = keyValueService;
        _runtimeState = runtimeState;
    }

    public void Initialize()
    {
        if (_runtimeState.Level < RuntimeLevel.Run)
            return;

        var migrationPlan = new MigrationPlan("BbvPushSubscription");
        migrationPlan.From(string.Empty)
            .To<AddPushSubscriptionTableMigration>("bbv-push-subscription-001");

        var upgrader = new Upgrader(migrationPlan);
        upgrader.Execute(_migrationPlanExecutor, _coreScopeProvider, _keyValueService);
    }

    public void Terminate()
    {
    }
}
