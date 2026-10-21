using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.Company
{
    /// <inheritdoc />
    public partial class InitialCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.CreateTable(
                name: "CompanyProfile",
                schema: "core",
                columns: table => new
                {
                    SingletonKey = table.Column<byte>(type: "tinyint", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Nip = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Regon = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfile", x => x.SingletonKey);
                    table.UniqueConstraint("AK_CompanyProfile_CompanyId", x => x.CompanyId);
                    table.CheckConstraint("CK_CompanyProfile_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_CompanyProfile_Dates", "[UpdatedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_CompanyProfile_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_CompanyProfile_Singleton", "[SingletonKey] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Contractors",
                schema: "core",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    TaxIdentifier = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contractors", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_Contractors_Code", "LEN(LTRIM(RTRIM([Code]))) > 0");
                    table.CheckConstraint("CK_Contractors_CountryCode", "LEN([CountryCode]) = 2 AND [CountryCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
                    table.CheckConstraint("CK_Contractors_Dates", "[UpdatedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_Contractors_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Contractors_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.ForeignKey(
                        name: "FK_Contractors_CompanyProfile_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "core",
                        principalTable: "CompanyProfile",
                        principalColumn: "CompanyId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contractors_CompanyId_Code",
                schema: "core",
                table: "Contractors",
                columns: new[] { "CompanyId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Contractors",
                schema: "core");

            migrationBuilder.DropTable(
                name: "CompanyProfile",
                schema: "core");
        }
    }
}
