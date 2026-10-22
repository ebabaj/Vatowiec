using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class InstallationIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Installation",
                schema: "security",
                columns: table => new
                {
                    SingletonKey = table.Column<byte>(type: "tinyint", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Installation", x => x.SingletonKey);
                    table.CheckConstraint("CK_Installation_Singleton", "[SingletonKey] = 1 AND [Id] <> '00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Installation_Id",
                schema: "security",
                table: "Installation",
                column: "Id",
                unique: true);

            // NEWID osobno w każdej instalacji. Wartość pozostaje stała przy aktualizacjach i restore.
            migrationBuilder.Sql("INSERT [security].[Installation] ([SingletonKey], [Id]) VALUES (1, NEWID());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Installation",
                schema: "security");
        }
    }
}
