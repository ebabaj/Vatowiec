using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class TableViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TableViewChanges",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstallationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TableId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableViewChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TableViews",
                schema: "security",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstallationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TableId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ColumnsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    PageSize = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableViews", x => x.Id);
                    table.CheckConstraint("CK_TableViews_PageSize", "[PageSize] IS NULL OR [PageSize] IN (25,50,100,200)");
                });

            migrationBuilder.Sql("CREATE TRIGGER [audit].[TR_TableViewChanges_Immutable] ON [audit].[TableViewChanges] INSTEAD OF UPDATE, DELETE AS THROW 51000, 'Table view audit is immutable', 1;");
            migrationBuilder.CreateIndex(
                name: "IX_TableViews_InstallationId_OwnerId_TableId",
                schema: "security",
                table: "TableViews",
                columns: new[] { "InstallationId", "OwnerId", "TableId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TableViewChanges",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "TableViews",
                schema: "security");
        }
    }
}
