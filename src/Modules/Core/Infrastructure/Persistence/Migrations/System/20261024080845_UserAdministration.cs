using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class UserAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                schema: "security",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                schema: "security",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "UserAdministrationChanges",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorIssuerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    PermissionsBefore = table.Column<int>(type: "int", nullable: true),
                    PermissionsAfter = table.Column<int>(type: "int", nullable: true),
                    BlockedBefore = table.Column<bool>(type: "bit", nullable: true),
                    BlockedAfter = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAdministrationChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAdministrationChanges_TargetId_Id",
                schema: "audit",
                table: "UserAdministrationChanges",
                columns: new[] { "TargetId", "Id" });
            migrationBuilder.Sql("""
                CREATE TRIGGER [audit].[TR_UserAdministrationChanges_Immutable]
                ON [audit].[UserAdministrationChanges] INSTEAD OF UPDATE, DELETE
                AS BEGIN SET NOCOUNT ON; THROW 51000, 'User administration history is immutable', 1; END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAdministrationChanges",
                schema: "audit");

            migrationBuilder.DropColumn(
                name: "IsBlocked",
                schema: "security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                schema: "security",
                table: "Users");
        }
    }
}
