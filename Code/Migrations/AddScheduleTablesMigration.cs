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

[TableName("BbvSchedule")]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class ScheduleSchema
{
    [Column("Id")]
    public int Id { get; set; }

    [Column("CrewId")]
    public int CrewId { get; set; }

    [Column("CrewKey")]
    public Guid CrewKey { get; set; }

    [Column("Name")]
    public string Name { get; set; } = string.Empty;

    [Column("ScheduleDate")]
    public DateTime ScheduleDate { get; set; }

    [Column("IsPublished")]
    public bool IsPublished { get; set; }

    // [CHANGE: overview grid view + manual schedule ordering] Related: IScheduleService.cs, ScheduleService.cs, Web/Controllers/ScheduleGetController.cs, Web/Views/CrewSchedule.cshtml
    [Column("SortOrder")]
    public int SortOrder { get; set; }

    [Column("CreatedUtc")]
    public DateTime CreatedUtc { get; set; }
}

[TableName("BbvShift")]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class ShiftSchema
{
    [Column("Id")]
    public int Id { get; set; }

    [Column("ScheduleId")]
    public int ScheduleId { get; set; }

    [Column("StartTime")]
    public string StartTime { get; set; } = string.Empty;

    [Column("EndTime")]
    public string EndTime { get; set; } = string.Empty;

    [Column("AssignedMemberKey")]
    public Guid? AssignedMemberKey { get; set; }

    [Column("AssignedMemberName")]
    public string? AssignedMemberName { get; set; }

    // [CHANGE: internal bookings without a member] Related: IScheduleService.cs, ScheduleService.cs, Web/Controllers/ScheduleGetController.cs, Web/Views/CrewSchedule.cshtml
    [Column("IsInternal")]
    public bool IsInternal { get; set; }

    [Column("Title")]
    public string? Title { get; set; }

    [Column("CreatedUtc")]
    public DateTime CreatedUtc { get; set; }
}

public class AddScheduleTablesMigration : MigrationBase
{
    public AddScheduleTablesMigration(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        Execute.Sql(@"
            CREATE TABLE dbo.BbvSchedule (
                Id INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
                CrewId INT NOT NULL,
                CrewKey UNIQUEIDENTIFIER NOT NULL,
                Name NVARCHAR(255) NOT NULL,
                ScheduleDate DATE NOT NULL,
                IsPublished BIT NOT NULL DEFAULT 0,
                CreatedUtc DATETIME NOT NULL DEFAULT GETUTCDATE()
            )").Do();

        Execute.Sql(@"
            CREATE NONCLUSTERED INDEX IX_BbvSchedule_CrewId
            ON dbo.BbvSchedule (CrewId ASC)").Do();

        Execute.Sql(@"
            CREATE TABLE dbo.BbvShift (
                Id INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
                ScheduleId INT NOT NULL,
                StartTime NVARCHAR(5) NOT NULL,
                EndTime NVARCHAR(5) NOT NULL,
                AssignedMemberKey UNIQUEIDENTIFIER NULL,
                AssignedMemberName NVARCHAR(255) NULL,
                CreatedUtc DATETIME NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT FK_BbvShift_BbvSchedule FOREIGN KEY (ScheduleId)
                    REFERENCES dbo.BbvSchedule(Id) ON DELETE CASCADE
            )").Do();

        Execute.Sql(@"
            CREATE NONCLUSTERED INDEX IX_BbvShift_ScheduleId
            ON dbo.BbvShift (ScheduleId ASC)").Do();

        Execute.Sql(@"
            CREATE NONCLUSTERED INDEX IX_BbvShift_AssignedMemberKey
            ON dbo.BbvShift (AssignedMemberKey ASC)
            WHERE AssignedMemberKey IS NOT NULL").Do();
    }
}

// [CHANGE: internal bookings without a member] Related: IScheduleService.cs, ScheduleService.cs, Web/Controllers/ScheduleGetController.cs, Web/Views/CrewSchedule.cshtml
// AssignedMemberKey stays NULL for internal bookings so member exports/queries ignore them.
public class AddInternalBookingColumnsMigration : MigrationBase
{
    public AddInternalBookingColumnsMigration(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        Execute.Sql(@"
            ALTER TABLE dbo.BbvShift ADD
                IsInternal BIT NOT NULL CONSTRAINT DF_BbvShift_IsInternal DEFAULT 0,
                Title NVARCHAR(100) NULL").Do();
    }
}

// [CHANGE: overview grid view + manual schedule ordering] Related: IScheduleService.cs, ScheduleService.cs, Web/Controllers/ScheduleGetController.cs, Web/Views/CrewSchedule.cshtml
// Existing rows get SortOrder 0; ordering falls back to ScheduleDate/Name until editors reorder.
public class AddScheduleSortOrderMigration : MigrationBase
{
    public AddScheduleSortOrderMigration(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        Execute.Sql(@"
            ALTER TABLE dbo.BbvSchedule ADD
                SortOrder INT NOT NULL CONSTRAINT DF_BbvSchedule_SortOrder DEFAULT 0").Do();
    }
}

public class ScheduleMigrationComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<ScheduleMigrationComponent>();
        builder.Components().Append<ScheduleMigrationComponent>();
    }
}

public class ScheduleMigrationComponent : IComponent
{
    private readonly ICoreScopeProvider _coreScopeProvider;
    private readonly IMigrationPlanExecutor _migrationPlanExecutor;
    private readonly IKeyValueService _keyValueService;
    private readonly IRuntimeState _runtimeState;

    public ScheduleMigrationComponent(
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

        var migrationPlan = new MigrationPlan("BbvSchedule");
        migrationPlan.From(string.Empty)
            .To<AddScheduleTablesMigration>("bbv-schedule-001")
            .To<AddInternalBookingColumnsMigration>("bbv-schedule-002")
            .To<AddScheduleSortOrderMigration>("bbv-schedule-003");

        var upgrader = new Upgrader(migrationPlan);
        upgrader.Execute(_migrationPlanExecutor, _coreScopeProvider, _keyValueService);
    }

    public void Terminate()
    {
    }
}
