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

// [CHANGE: crew invitation feature - shiftadmins invite new volunteers while signup is closed]
// Related: Code/Services/CrewInvitationService.cs, Web/Controllers/CrewInvitationSurfaceController.cs, Web/Views/Crew.cshtml

[TableName("BbvCrewInvitation")]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class CrewInvitationSchema
{
    [Column("Id")]
    public int Id { get; set; }

    [Column("Email")]
    public string Email { get; set; } = string.Empty;

    [Column("FirstName")]
    public string FirstName { get; set; } = string.Empty;

    [Column("LastName")]
    public string LastName { get; set; } = string.Empty;

    [Column("CrewId")]
    public int CrewId { get; set; }

    [Column("CrewKey")]
    public Guid CrewKey { get; set; }

    [Column("Token")]
    public string Token { get; set; } = string.Empty;

    [Column("InvitedByEmail")]
    public string InvitedByEmail { get; set; } = string.Empty;

    [Column("InvitedByName")]
    public string InvitedByName { get; set; } = string.Empty;

    [Column("SentDate")]
    public DateTime SentDate { get; set; }

    [Column("AcceptedDate")]
    public DateTime? AcceptedDate { get; set; }

    [Column("CanceledDate")]
    public DateTime? CanceledDate { get; set; }

    [Column("CreatedUtc")]
    public DateTime CreatedUtc { get; set; }
}

public class AddCrewInvitationTableMigration : MigrationBase
{
    public AddCrewInvitationTableMigration(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        Execute.Sql(@"
            CREATE TABLE dbo.BbvCrewInvitation (
                Id INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
                Email NVARCHAR(255) NOT NULL,
                FirstName NVARCHAR(255) NOT NULL,
                LastName NVARCHAR(255) NOT NULL,
                CrewId INT NOT NULL,
                CrewKey UNIQUEIDENTIFIER NOT NULL,
                Token NVARCHAR(64) NOT NULL,
                InvitedByEmail NVARCHAR(255) NOT NULL,
                InvitedByName NVARCHAR(255) NOT NULL,
                SentDate DATETIME NOT NULL,
                AcceptedDate DATETIME NULL,
                CanceledDate DATETIME NULL,
                CreatedUtc DATETIME NOT NULL DEFAULT GETUTCDATE()
            )").Do();

        Execute.Sql(@"
            CREATE NONCLUSTERED INDEX IX_BbvCrewInvitation_CrewId
            ON dbo.BbvCrewInvitation (CrewId ASC)").Do();

        Execute.Sql(@"
            CREATE NONCLUSTERED INDEX IX_BbvCrewInvitation_Token
            ON dbo.BbvCrewInvitation (Token ASC)").Do();
    }
}

public class CrewInvitationMigrationComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<CrewInvitationMigrationComponent>();
        builder.Components().Append<CrewInvitationMigrationComponent>();
    }
}

public class CrewInvitationMigrationComponent : IComponent
{
    private readonly ICoreScopeProvider _coreScopeProvider;
    private readonly IMigrationPlanExecutor _migrationPlanExecutor;
    private readonly IKeyValueService _keyValueService;
    private readonly IRuntimeState _runtimeState;

    public CrewInvitationMigrationComponent(
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

        var migrationPlan = new MigrationPlan("BbvCrewInvitation");
        migrationPlan.From(string.Empty)
            .To<AddCrewInvitationTableMigration>("bbv-crew-invitation-001");

        var upgrader = new Upgrader(migrationPlan);
        upgrader.Execute(_migrationPlanExecutor, _coreScopeProvider, _keyValueService);
    }

    public void Terminate()
    {
    }
}
