using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class InstallationPasswordPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PasswordPolicy",
                schema: "security",
                columns: table => new
                {
                    SingletonKey = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MinimumLength = table.Column<int>(type: "int", nullable: false),
                    RequireUppercase = table.Column<bool>(type: "bit", nullable: false),
                    RequireLowercase = table.Column<bool>(type: "bit", nullable: false),
                    RequireDigit = table.Column<bool>(type: "bit", nullable: false),
                    RequireSpecialCharacter = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordPolicy", x => x.SingletonKey);
                    table.CheckConstraint("CK_PasswordPolicy_Values", "[SingletonKey]=1 AND [MinimumLength] BETWEEN 1 AND 1024");
                });

            migrationBuilder.CreateTable(
                name: "PasswordPolicyChanges",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorIssuerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordPolicyChanges", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "security",
                table: "PasswordPolicy",
                columns: new[] { "SingletonKey", "IsEnabled", "MinimumLength", "RequireDigit", "RequireLowercase", "RequireSpecialCharacter", "RequireUppercase" },
                values: new object[] { 1, true, 12, true, true, true, true });
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [audit].[TR_PasswordPolicyChanges_Immutable]
                ON [audit].[PasswordPolicyChanges] INSTEAD OF UPDATE, DELETE
                AS BEGIN SET NOCOUNT ON; THROW 51000, ''Password policy history is immutable'', 1; END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordPolicy",
                schema: "security");

            migrationBuilder.DropTable(
                name: "PasswordPolicyChanges",
                schema: "audit");
        }
    }
}
