using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.Company
{
    /// <inheritdoc />
    public partial class ContractorAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "ContractorChanges",
                schema: "audit",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorIssuerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SnapshotVersion = table.Column<int>(type: "int", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractorChanges", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_ContractorChanges_Actor", "[ActorId] <> '00000000-0000-0000-0000-000000000000' AND [ActorIssuerId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ContractorChanges_Json", "ISJSON([AfterJson]) = 1 AND ([BeforeJson] IS NULL OR ISJSON([BeforeJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_ContractorChanges_CompanyProfile_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "CompanyProfile",
                        principalColumn: "CompanyId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contractors_CompanyId_IsActive_Code",
                schema: "core",
                table: "Contractors",
                columns: new[] { "CompanyId", "IsActive", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractorChanges_CompanyId_ContractorId_OccurredAtUtc",
                schema: "audit",
                table: "ContractorChanges",
                columns: new[] { "CompanyId", "ContractorId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractorChanges",
                schema: "audit");

            migrationBuilder.DropIndex(
                name: "IX_Contractors_CompanyId_IsActive_Code",
                schema: "core",
                table: "Contractors");
        }
    }
}
