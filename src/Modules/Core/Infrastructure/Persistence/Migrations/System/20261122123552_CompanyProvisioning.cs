using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class CompanyProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.CreateTable(
                name: "CompanyOperations",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OperationKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RequestedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ActorIssuerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ServerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ConnectionProfileKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    LastErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LeaseOwner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyOperations", x => x.Id);
                    table.CheckConstraint("CK_CompanyOperations_Attempt", "[Attempt] >= 0");
                    table.CheckConstraint("CK_CompanyOperations_Completed", "([Status] = N'Completed' AND [Stage] = N'Ready' AND [CompletedAtUtc] IS NOT NULL) OR ([Status] <> N'Completed' AND [CompletedAtUtc] IS NULL)");
                    table.CheckConstraint("CK_CompanyOperations_Kind", "[Kind] IN (N'Provision')");
                    table.CheckConstraint("CK_CompanyOperations_Lease", "([Status] = N'Running' AND [LeaseOwner] IS NOT NULL AND [LeaseExpiresAtUtc] IS NOT NULL) OR ([Status] <> N'Running' AND [LeaseOwner] IS NULL AND [LeaseExpiresAtUtc] IS NULL)");
                    table.CheckConstraint("CK_CompanyOperations_Stage", "[Stage] IN (N'Requested', N'DatabaseCreated', N'SchemaApplied', N'ProfileInitialized', N'Ready')");
                    table.CheckConstraint("CK_CompanyOperations_Status", "[Status] IN (N'Pending', N'Running', N'Failed', N'NeedsReconciliation', N'Completed')");
                });

            migrationBuilder.CreateTable(
                name: "CompanyOperationEvents",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActorKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyOperationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyOperationEvents_CompanyOperations_OperationId",
                        column: x => x.OperationId,
                        principalSchema: "ops",
                        principalTable: "CompanyOperations",
                        principalColumn: "Id");
                });
            migrationBuilder.Sql("CREATE TRIGGER [ops].[TR_CompanyOperationEvents_Immutable] ON [ops].[CompanyOperationEvents] INSTEAD OF UPDATE, DELETE AS THROW 51000, 'Company operation history is immutable', 1;");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyOperationEvents_OperationId_Id",
                schema: "ops",
                table: "CompanyOperationEvents",
                columns: new[] { "OperationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyOperations_CompanyId",
                schema: "ops",
                table: "CompanyOperations",
                column: "CompanyId",
                unique: true,
                filter: "[Kind] = N'Provision'");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyOperations_RequestedById_OperationKey",
                schema: "ops",
                table: "CompanyOperations",
                columns: new[] { "RequestedById", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyOperations_ServerName_DatabaseName",
                schema: "ops",
                table: "CompanyOperations",
                columns: new[] { "ServerName", "DatabaseName" },
                unique: true,
                filter: "[Kind] = N'Provision'");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyOperations_Status_NextAttemptAtUtc",
                schema: "ops",
                table: "CompanyOperations",
                columns: new[] { "Status", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyOperationEvents",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "CompanyOperations",
                schema: "ops");
        }
    }
}
