using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class CompanyBackups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompanyOperations_RequestedById_OperationKey",
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
                name: "CK_CompanyAccess_Permissions",
                schema: "security",
                table: "CompanyAccess");

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestedById",
                schema: "ops",
                table: "CompanyOperations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "BackupFileName",
                schema: "ops",
                table: "CompanyOperations",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BackupPolicy",
                schema: "ops",
                columns: table => new
                {
                    SingletonKey = table.Column<byte>(type: "tinyint", nullable: false),
                    UsersMayRequestBackups = table.Column<bool>(type: "bit", nullable: false),
                    RetainedBackupsPerCompany = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupPolicy", x => x.SingletonKey);
                    table.CheckConstraint("CK_BackupPolicy_Values", "[SingletonKey] = 1 AND [RetainedBackupsPerCompany] BETWEEN 1 AND 365");
                });

            migrationBuilder.CreateTable(
                name: "BackupReminderSnoozes",
                schema: "ops",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupReminderSnoozes", x => new { x.UserId, x.CompanyId });
                    table.ForeignKey(
                        name: "FK_BackupReminderSnoozes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BackupReminderSnoozes_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BackupSettingChanges",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSettingChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanyBackups",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsAutomatic = table.Column<bool>(type: "bit", nullable: false),
                    RequestedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RemovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RemovedReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBackups", x => x.Id);
                    table.CheckConstraint("CK_CompanyBackups_Kind", "[Kind] IN (N'Regular')");
                    table.CheckConstraint("CK_CompanyBackups_Removed", "([RemovedAtUtc] IS NULL AND [RemovedReason] IS NULL) OR ([RemovedAtUtc] IS NOT NULL AND [RemovedReason] IS NOT NULL)");
                    table.CheckConstraint("CK_CompanyBackups_Size", "[SizeBytes] > 0");
                    table.ForeignKey(
                        name: "FK_CompanyBackups_CompanyOperations_OperationId",
                        column: x => x.OperationId,
                        principalSchema: "ops",
                        principalTable: "CompanyOperations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CompanyBackupSchedules",
                schema: "ops",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PreferredHour = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBackupSchedules", x => x.CompanyId);
                    table.CheckConstraint("CK_CompanyBackupSchedules_Frequency", "[Frequency] IN (N'Never', N'Daily', N'Every7Days', N'Every30Days')");
                    table.CheckConstraint("CK_CompanyBackupSchedules_Hour", "[PreferredHour] BETWEEN 0 AND 23");
                    table.CheckConstraint("CK_CompanyBackupSchedules_Mode", "[Mode] IN (N'Reminder', N'Automatic')");
                    table.ForeignKey(
                        name: "FK_CompanyBackupSchedules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "ops",
                table: "BackupPolicy",
                columns: new[] { "SingletonKey", "RetainedBackupsPerCompany", "UsersMayRequestBackups" },
                values: new object[] { (byte)1, 7, true });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyOperations_ActiveBackup",
                schema: "ops",
                table: "CompanyOperations",
                column: "CompanyId",
                unique: true,
                filter: "[Kind] = N'Backup' AND [Status] IN (N'Pending', N'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyOperations_RequestedById_OperationKey",
                schema: "ops",
                table: "CompanyOperations",
                columns: new[] { "RequestedById", "OperationKey" },
                unique: true,
                filter: "[RequestedById] IS NOT NULL");

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
                name: "CK_CompanyOperations_Requester",
                schema: "ops",
                table: "CompanyOperations",
                sql: "[Kind] = N'Backup' OR [RequestedById] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_Stage",
                schema: "ops",
                table: "CompanyOperations",
                sql: "[Stage] IN (N'Requested', N'DatabaseCreated', N'SchemaApplied', N'ProfileInitialized', N'BackupWritten', N'BackupVerified', N'Ready')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyAccess_Permissions",
                schema: "security",
                table: "CompanyAccess",
                sql: "[Permissions] > 0 AND [Permissions] <= 2047");

            migrationBuilder.CreateIndex(
                name: "IX_BackupReminderSnoozes_CompanyId",
                schema: "ops",
                table: "BackupReminderSnoozes",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupSettingChanges_CompanyId_Id",
                schema: "ops",
                table: "BackupSettingChanges",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBackups_CompanyId_RemovedAtUtc_CreatedAtUtc",
                schema: "ops",
                table: "CompanyBackups",
                columns: new[] { "CompanyId", "RemovedAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBackups_OperationId",
                schema: "ops",
                table: "CompanyBackups",
                column: "OperationId",
                unique: true);

            migrationBuilder.Sql("CREATE TRIGGER [ops].[TR_BackupSettingChanges_Immutable] ON [ops].[BackupSettingChanges] INSTEAD OF UPDATE, DELETE AS THROW 51000, 'Backup settings history is immutable', 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupPolicy",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "BackupReminderSnoozes",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "BackupSettingChanges",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "CompanyBackups",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "CompanyBackupSchedules",
                schema: "ops");

            migrationBuilder.DropIndex(
                name: "IX_CompanyOperations_ActiveBackup",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropIndex(
                name: "IX_CompanyOperations_RequestedById_OperationKey",
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
                name: "CK_CompanyOperations_Requester",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyOperations_Stage",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyAccess_Permissions",
                schema: "security",
                table: "CompanyAccess");

            migrationBuilder.DropColumn(
                name: "BackupFileName",
                schema: "ops",
                table: "CompanyOperations");

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestedById",
                schema: "ops",
                table: "CompanyOperations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyOperations_RequestedById_OperationKey",
                schema: "ops",
                table: "CompanyOperations",
                columns: new[] { "RequestedById", "OperationKey" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_Kind",
                schema: "ops",
                table: "CompanyOperations",
                sql: "[Kind] IN (N'Provision')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyOperations_Stage",
                schema: "ops",
                table: "CompanyOperations",
                sql: "[Stage] IN (N'Requested', N'DatabaseCreated', N'SchemaApplied', N'ProfileInitialized', N'Ready')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyAccess_Permissions",
                schema: "security",
                table: "CompanyAccess",
                sql: "[Permissions] > 0 AND [Permissions] <= 1023");
        }
    }
}
