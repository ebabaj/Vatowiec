using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class CompanyRestore : Migration
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
                name: "CK_CompanyBackups_Kind",
                schema: "ops",
                table: "CompanyBackups");

            migrationBuilder.AddColumn<string>(
                name: "PreviousDatabaseName",
                schema: "ops",
                table: "CompanyOperations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RestoreMode",
                schema: "ops",
                table: "CompanyOperations",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkingDatabaseName",
                schema: "ops",
                table: "CompanyOperations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RestoreImportFiles",
                schema: "ops",
                columns: table => new
                {
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceInstallationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ScannedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreImportFiles", x => x.FileName);
                    table.CheckConstraint("CK_RestoreImportFiles_Size", "[SizeBytes] > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyOperations_ActiveRestore",
                schema: "ops",
                table: "CompanyOperations",
                column: "CompanyId",
                unique: true,
                filter: "[Kind] = N'Restore' AND [Status] IN (N'Pending', N'Running', N'Failed', N'NeedsReconciliation')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_BackupFile",
                schema: "ops",
                table: "CompanyOperations",
                sql: "([Kind] IN (N'Backup', N'Restore') AND [BackupFileName] IS NOT NULL) OR ([Kind] = N'Provision' AND [BackupFileName] IS NULL) OR ([Kind] = N'Delete' AND [CreateArchive] IS NOT NULL AND (([CreateArchive] = 1 AND [BackupFileName] IS NOT NULL) OR ([CreateArchive] = 0 AND [BackupFileName] IS NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_Kind",
                schema: "ops",
                table: "CompanyOperations",
                sql: "[Kind] IN (N'Provision', N'Backup', N'Delete', N'Restore')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_Restore",
                schema: "ops",
                table: "CompanyOperations",
                sql: "([Kind] = N'Restore' AND [RestoreMode] IN (N'Existing', N'Deleted', N'Import') AND [WorkingDatabaseName] IS NOT NULL AND (([RestoreMode] = N'Existing' AND [PreviousDatabaseName] IS NOT NULL) OR ([RestoreMode] <> N'Existing' AND [PreviousDatabaseName] IS NULL))) OR ([Kind] <> N'Restore' AND [RestoreMode] IS NULL AND [WorkingDatabaseName] IS NULL AND [PreviousDatabaseName] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_Stage",
                schema: "ops",
                table: "CompanyOperations",
                sql: "[Stage] IN (N'Requested', N'DatabaseCreated', N'SchemaApplied', N'ProfileInitialized', N'BackupWritten', N'BackupVerified', N'CompanyDeactivated', N'ArchiveWritten', N'ArchiveVerified', N'DatabaseDropped', N'DatabaseRestored', N'RestoreVerified', N'SchemaUpgraded', N'Switched', N'Ready')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyBackups_Kind",
                schema: "ops",
                table: "CompanyBackups",
                sql: "[Kind] IN (N'Regular', N'PreRestore')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RestoreImportFiles",
                schema: "ops");

            migrationBuilder.DropIndex(
                name: "IX_CompanyOperations_ActiveRestore",
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
                name: "CK_CompanyOperations_Restore",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyOperations_Stage",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyBackups_Kind",
                schema: "ops",
                table: "CompanyBackups");

            migrationBuilder.DropColumn(
                name: "PreviousDatabaseName",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropColumn(
                name: "RestoreMode",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropColumn(
                name: "WorkingDatabaseName",
                schema: "ops",
                table: "CompanyOperations");

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
                name: "CK_CompanyBackups_Kind",
                schema: "ops",
                table: "CompanyBackups",
                sql: "[Kind] IN (N'Regular')");
        }
    }
}
