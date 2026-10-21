using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class InitialSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.CreateTable(
                name: "Companies",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                    table.CheckConstraint("CK_Companies_Dates", "[UpdatedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_Companies_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Companies_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "CompanyDatabases",
                schema: "core",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ConnectionProfileKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyDatabases", x => x.CompanyId);
                    table.CheckConstraint("CK_CompanyDatabases_Dates", "[UpdatedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_CompanyDatabases_Location", "LEN(LTRIM(RTRIM([ServerName]))) > 0 AND LEN(LTRIM(RTRIM([DatabaseName]))) > 0 AND LEN(LTRIM(RTRIM([ConnectionProfileKey]))) > 0");
                    table.ForeignKey(
                        name: "FK_CompanyDatabases_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyDatabases_ServerName_DatabaseName",
                schema: "core",
                table: "CompanyDatabases",
                columns: new[] { "ServerName", "DatabaseName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyDatabases",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Companies",
                schema: "core");
        }
    }
}
