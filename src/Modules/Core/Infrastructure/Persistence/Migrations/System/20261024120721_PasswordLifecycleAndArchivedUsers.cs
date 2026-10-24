using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class PasswordLifecycleAndArchivedUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAtUtc",
                schema: "security",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedByName",
                schema: "security",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                schema: "security",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoricalNormalizedUserName",
                schema: "security",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HistoricalUserName",
                schema: "security",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                schema: "security",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsLoginReleased",
                schema: "security",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordChangedAtUtc",
                schema: "security",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedUserId",
                schema: "audit",
                table: "UserAdministrationChanges",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnforcePasswordExpiration",
                schema: "security",
                table: "PasswordPolicy",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordExpirationEnabledAtUtc",
                schema: "security",
                table: "PasswordPolicy",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PasswordValidityDays",
                schema: "security",
                table: "PasswordPolicy",
                type: "int",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<bool>(
                name: "PreventPasswordReuse",
                schema: "security",
                table: "PasswordPolicy",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PreviousPasswordCount",
                schema: "security",
                table: "PasswordPolicy",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.CreateTable(
                name: "UserPasswordHistory",
                schema: "security",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ReplacedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPasswordHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPasswordHistory_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PasswordPolicy_Lifetime",
                schema: "security",
                table: "PasswordPolicy",
                sql: "[PreviousPasswordCount] BETWEEN 1 AND 20 AND [PasswordValidityDays] BETWEEN 1 AND 3650");

            migrationBuilder.CreateIndex(
                name: "IX_UserPasswordHistory_UserId_Id",
                schema: "security",
                table: "UserPasswordHistory",
                columns: new[] { "UserId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPasswordHistory",
                schema: "security");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PasswordPolicy_Lifetime",
                schema: "security",
                table: "PasswordPolicy");

            migrationBuilder.DropColumn(
                name: "ArchivedAtUtc",
                schema: "security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ArchivedByName",
                schema: "security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                schema: "security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HistoricalNormalizedUserName",
                schema: "security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HistoricalUserName",
                schema: "security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                schema: "security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsLoginReleased",
                schema: "security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAtUtc",
                schema: "security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RelatedUserId",
                schema: "audit",
                table: "UserAdministrationChanges");

            migrationBuilder.DropColumn(
                name: "EnforcePasswordExpiration",
                schema: "security",
                table: "PasswordPolicy");

            migrationBuilder.DropColumn(
                name: "PasswordExpirationEnabledAtUtc",
                schema: "security",
                table: "PasswordPolicy");

            migrationBuilder.DropColumn(
                name: "PasswordValidityDays",
                schema: "security",
                table: "PasswordPolicy");

            migrationBuilder.DropColumn(
                name: "PreventPasswordReuse",
                schema: "security",
                table: "PasswordPolicy");

            migrationBuilder.DropColumn(
                name: "PreviousPasswordCount",
                schema: "security",
                table: "PasswordPolicy");
        }
    }
}
