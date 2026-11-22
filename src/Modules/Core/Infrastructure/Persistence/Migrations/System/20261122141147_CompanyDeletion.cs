using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class CompanyDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyOperations_BackupFile",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyOperations_Kind",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyOperations_Stage",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyOperations_Status",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.AddColumn<bool>(
                name: "CreateArchive",
                schema: "ops",
                table: "CompanyOperations",
                type: "bit",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeletedCompanies",
                schema: "ops",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Nip = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Regon = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: true),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeletedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeletedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ArchiveFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    ArchiveSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ArchiveSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeletedCompanies", x => x.CompanyId);
                    table.CheckConstraint("CK_DeletedCompanies_Archive", "([ArchiveFileName] IS NULL AND [ArchiveSizeBytes] IS NULL AND [ArchiveSha256] IS NULL) OR ([ArchiveFileName] IS NOT NULL AND [ArchiveSizeBytes] > 0 AND [ArchiveSha256] IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyOperations_ActiveDelete",
                schema: "ops",
                table: "CompanyOperations",
                column: "CompanyId",
                unique: true,
                filter: "[Kind] = N'Delete' AND [Status] <> N'Cancelled'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_BackupFile",
                schema: "ops",
                table: "CompanyOperations",
                sql: "([Kind] = N'Backup' AND [BackupFileName] IS NOT NULL) OR ([Kind] = N'Provision' AND [BackupFileName] IS NULL) OR ([Kind] = N'Delete' AND [CreateArchive] IS NOT NULL AND (([CreateArchive] = 1 AND [BackupFileName] IS NOT NULL) OR ([CreateArchive] = 0 AND [BackupFileName] IS NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_Kind",
                schema: "ops",
                table: "CompanyOperations",
                sql: "[Kind] IN (N'Provision', N'Backup', N'Delete')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_Stage",
                schema: "ops",
                table: "CompanyOperations",
                sql: "[Stage] IN (N'Requested', N'DatabaseCreated', N'SchemaApplied', N'ProfileInitialized', N'BackupWritten', N'BackupVerified', N'CompanyDeactivated', N'ArchiveWritten', N'ArchiveVerified', N'DatabaseDropped', N'Ready')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_Status",
                schema: "ops",
                table: "CompanyOperations",
                sql: "[Status] IN (N'Pending', N'Running', N'Failed', N'NeedsReconciliation', N'Completed', N'Cancelled')");

            // ADR-045: historia pozostaje niezmienna (UPDATE zawsze odrzucony). DELETE jest dozwolony wyłącznie dla wierszy
            // usuwanej firmy, gdy usługa operacji ustawi SESSION_CONTEXT 'Vatowiec.PurgeCompanyId' na czas transakcji czyszczenia.
            migrationBuilder.Sql("""
                ALTER TRIGGER [audit].[TR_UserAdministrationChanges_Immutable]
                ON [audit].[UserAdministrationChanges] INSTEAD OF UPDATE, DELETE
                AS BEGIN
                    SET NOCOUNT ON;
                    DECLARE @purge uniqueidentifier = TRY_CONVERT(uniqueidentifier, SESSION_CONTEXT(N'Vatowiec.PurgeCompanyId'));
                    IF EXISTS (SELECT 1 FROM inserted) OR @purge IS NULL OR EXISTS (SELECT 1 FROM deleted WHERE [CompanyId] IS NULL OR [CompanyId] <> @purge)
                        THROW 51000, 'User administration history is immutable', 1;
                    DELETE t FROM [audit].[UserAdministrationChanges] t JOIN deleted d ON d.[Id] = t.[Id];
                END
                """);
            migrationBuilder.Sql("""
                ALTER TRIGGER [ops].[TR_BackupSettingChanges_Immutable]
                ON [ops].[BackupSettingChanges] INSTEAD OF UPDATE, DELETE
                AS BEGIN
                    SET NOCOUNT ON;
                    DECLARE @purge uniqueidentifier = TRY_CONVERT(uniqueidentifier, SESSION_CONTEXT(N'Vatowiec.PurgeCompanyId'));
                    IF EXISTS (SELECT 1 FROM inserted) OR @purge IS NULL OR EXISTS (SELECT 1 FROM deleted WHERE [CompanyId] IS NULL OR [CompanyId] <> @purge)
                        THROW 51000, 'Backup settings history is immutable', 1;
                    DELETE t FROM [ops].[BackupSettingChanges] t JOIN deleted d ON d.[Id] = t.[Id];
                END
                """);
            migrationBuilder.Sql("""
                ALTER TRIGGER [ops].[TR_CompanyOperationEvents_Immutable]
                ON [ops].[CompanyOperationEvents] INSTEAD OF UPDATE, DELETE
                AS BEGIN
                    SET NOCOUNT ON;
                    DECLARE @purge uniqueidentifier = TRY_CONVERT(uniqueidentifier, SESSION_CONTEXT(N'Vatowiec.PurgeCompanyId'));
                    IF EXISTS (SELECT 1 FROM inserted) OR @purge IS NULL OR EXISTS (
                        SELECT 1 FROM deleted d LEFT JOIN [ops].[CompanyOperations] o ON o.[Id] = d.[OperationId]
                        WHERE o.[Id] IS NULL OR o.[CompanyId] <> @purge)
                        THROW 51000, 'Company operation history is immutable', 1;
                    DELETE t FROM [ops].[CompanyOperationEvents] t JOIN deleted d ON d.[Id] = t.[Id];
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TRIGGER [audit].[TR_UserAdministrationChanges_Immutable] ON [audit].[UserAdministrationChanges] INSTEAD OF UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51000, 'User administration history is immutable', 1; END");
            migrationBuilder.Sql("ALTER TRIGGER [ops].[TR_BackupSettingChanges_Immutable] ON [ops].[BackupSettingChanges] INSTEAD OF UPDATE, DELETE AS THROW 51000, 'Backup settings history is immutable', 1;");
            migrationBuilder.Sql("ALTER TRIGGER [ops].[TR_CompanyOperationEvents_Immutable] ON [ops].[CompanyOperationEvents] INSTEAD OF UPDATE, DELETE AS THROW 51000, 'Company operation history is immutable', 1;");
            migrationBuilder.DropTable(
                name: "DeletedCompanies",
                schema: "ops");

            migrationBuilder.DropIndex(
                name: "IX_CompanyOperations_ActiveDelete",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyOperations_BackupFile",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyOperations_Kind",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyOperations_Stage",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyOperations_Status",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropColumn(
                name: "CreateArchive",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_BackupFile",
                schema: "ops",
                table: "CompanyOperations",
                sql: "([Kind] = N'Backup' AND [BackupFileName] IS NOT NULL) OR ([Kind] <> N'Backup' AND [BackupFileName] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_Kind",
                schema: "ops",
                table: "CompanyOperations",
                sql: "[Kind] IN (N'Provision', N'Backup')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_Stage",
                schema: "ops",
                table: "CompanyOperations",
                sql: "[Stage] IN (N'Requested', N'DatabaseCreated', N'SchemaApplied', N'ProfileInitialized', N'BackupWritten', N'BackupVerified', N'Ready')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_Status",
                schema: "ops",
                table: "CompanyOperations",
                sql: "[Status] IN (N'Pending', N'Running', N'Failed', N'NeedsReconciliation', N'Completed')");
        }
    }
}
