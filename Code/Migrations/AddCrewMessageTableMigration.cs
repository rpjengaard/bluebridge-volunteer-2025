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

    // [CHANGE: crew message replies] Related: Code/Services/ICrewMessageService.cs, Code/Services/CrewMessageService.cs, Code/Services/IMemberEmailService.cs, Code/Services/MemberEmailService.cs, Web/Controllers/CrewSurfaceController.cs, Web/Views/Crew.cshtml
    [Column("RepliesClosed")]
    public bool RepliesClosed { get; set; }
}

[TableName("BbvCrewMessageReply")]
[PrimaryKey("Id", AutoIncrement = true)]
[ExplicitColumns]
public class CrewMessageReplySchema
{
    [Column("Id")]
    public int Id { get; set; }

    [Column("MessageId")]
    public int MessageId { get; set; }

    [Column("AuthorEmail")]
    public string AuthorEmail { get; set; } = string.Empty;

    [Column("AuthorName")]
    public string AuthorName { get; set; } = string.Empty;

    [Column("ReplyText")]
    public string ReplyText { get; set; } = string.Empty;

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
        // No-op: superseded by migration 002/003 which use raw SQL (compatible with Azure SQL)
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

// [CHANGE: crew message replies] Adds RepliesClosed flag + reply table (replies cascade-delete with their message)
public class AddCrewMessageRepliesMigration : MigrationBase
{
    public AddCrewMessageRepliesMigration(IMigrationContext context) : base(context)
    {
    }

    protected override void Migrate()
    {
        Execute.Sql(@"
            IF COL_LENGTH('dbo.BbvCrewMessage', 'RepliesClosed') IS NULL
                ALTER TABLE dbo.BbvCrewMessage ADD RepliesClosed BIT NOT NULL CONSTRAINT DF_BbvCrewMessage_RepliesClosed DEFAULT 0").Do();

        Execute.Sql(@"
            IF OBJECT_ID('dbo.BbvCrewMessageReply', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.BbvCrewMessageReply (
                    Id INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    MessageId INT NOT NULL,
                    AuthorEmail NVARCHAR(255) NOT NULL,
                    AuthorName NVARCHAR(255) NOT NULL,
                    ReplyText NVARCHAR(1000) NOT NULL,
                    CreatedUtc DATETIME NOT NULL,
                    CONSTRAINT FK_BbvCrewMessageReply_BbvCrewMessage FOREIGN KEY (MessageId)
                        REFERENCES dbo.BbvCrewMessage (Id) ON DELETE CASCADE
                );
                CREATE NONCLUSTERED INDEX IX_BbvCrewMessageReply_MessageId_CreatedUtc
                    ON dbo.BbvCrewMessageReply (MessageId ASC, CreatedUtc ASC);
            END").Do();
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
            .To<RecreateCrewMessageTableRawSqlMigration>("bbv-crew-message-003")
            .To<AddCrewMessageRepliesMigration>("bbv-crew-message-004");

        var upgrader = new Upgrader(migrationPlan);
        upgrader.Execute(_migrationPlanExecutor, _coreScopeProvider, _keyValueService);
    }

    public void Terminate()
    {
    }
}
