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

[TableName("BbvCrewMessage")]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class CrewMessageSchema
{
    [Column("Id")]
    public int Id { get; set; }

    [Column("CrewId")]
    public int CrewId { get; set; }

    [Column("AuthorEmail")]
    public string AuthorEmail { get; set; } = string.Empty;

    [Column("AuthorName")]
    public string AuthorName { get; set; } = string.Empty;

    [Column("MessageText")]
    public string MessageText { get; set; } = string.Empty;

    [Column("CreatedUtc")]
    public DateTime CreatedUtc { get; set; }
}

public class AddCrewMessageTableMigration : MigrationBase
{
    public AddCrewMessageTableMigration(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        if (!TableExists("BbvCrewMessage"))
        {
            Create.Table("BbvCrewMessage")
                .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
                .WithColumn("CrewId").AsInt32().NotNullable()
                .WithColumn("AuthorEmail").AsString(255).NotNullable()
                .WithColumn("AuthorName").AsString(255).NotNullable()
                .WithColumn("MessageText").AsString(4000).NotNullable()
                .WithColumn("CreatedUtc").AsDateTime().NotNullable()
                .Do();

            Create.Index("IX_BbvCrewMessage_CrewId_CreatedUtc")
                .OnTable("BbvCrewMessage")
                .OnColumn("CrewId").Ascending()
                .OnColumn("CreatedUtc").Descending()
                .WithOptions().NonClustered()
                .Do();
        }
    }
}

public class RecreateCrewMessageTableMigration : MigrationBase
{
    public RecreateCrewMessageTableMigration(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        // Use raw SQL to guarantee IDENTITY is set correctly
        Execute.Sql("IF OBJECT_ID('dbo.BbvCrewMessage', 'U') IS NOT NULL DROP TABLE dbo.BbvCrewMessage").Do();

        Execute.Sql(@"
            CREATE TABLE dbo.BbvCrewMessage (
                Id INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
                CrewId INT NOT NULL,
                AuthorEmail NVARCHAR(255) NOT NULL,
                AuthorName NVARCHAR(255) NOT NULL,
                MessageText NVARCHAR(4000) NOT NULL,
                CreatedUtc DATETIME NOT NULL
            )").Do();

        Execute.Sql(@"
            CREATE NONCLUSTERED INDEX IX_BbvCrewMessage_CrewId_CreatedUtc
            ON dbo.BbvCrewMessage (CrewId ASC, CreatedUtc DESC)").Do();
    }
}

public class RecreateCrewMessageTableRawSqlMigration : MigrationBase
{
    public RecreateCrewMessageTableRawSqlMigration(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        // Ensure table has IDENTITY — drop and recreate via raw SQL
        Execute.Sql("IF OBJECT_ID('dbo.BbvCrewMessage', 'U') IS NOT NULL DROP TABLE dbo.BbvCrewMessage").Do();

        Execute.Sql(@"
            CREATE TABLE dbo.BbvCrewMessage (
                Id INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
                CrewId INT NOT NULL,
                AuthorEmail NVARCHAR(255) NOT NULL,
                AuthorName NVARCHAR(255) NOT NULL,
                MessageText NVARCHAR(4000) NOT NULL,
                CreatedUtc DATETIME NOT NULL
            )").Do();

        Execute.Sql(@"
            CREATE NONCLUSTERED INDEX IX_BbvCrewMessage_CrewId_CreatedUtc
            ON dbo.BbvCrewMessage (CrewId ASC, CreatedUtc DESC)").Do();
    }
}

public class CrewMessageMigrationComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<CrewMessageMigrationComponent>();
        builder.Components().Append<CrewMessageMigrationComponent>();
    }
}

public class CrewMessageMigrationComponent : IComponent
{
    private readonly ICoreScopeProvider _coreScopeProvider;
    private readonly IMigrationPlanExecutor _migrationPlanExecutor;
    private readonly IKeyValueService _keyValueService;
    private readonly IRuntimeState _runtimeState;

    public CrewMessageMigrationComponent(
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

        var migrationPlan = new MigrationPlan("BbvCrewMessage");
        migrationPlan.From(string.Empty)
            .To<AddCrewMessageTableMigration>("bbv-crew-message-001")
            .To<RecreateCrewMessageTableMigration>("bbv-crew-message-002")
            .To<RecreateCrewMessageTableRawSqlMigration>("bbv-crew-message-003");

        var upgrader = new Upgrader(migrationPlan);
        upgrader.Execute(_migrationPlanExecutor, _coreScopeProvider, _keyValueService);
    }

    public void Terminate()
    {
    }
}
