using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class TaxIdentifierRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyAccess_Permissions",
                schema: "security",
                table: "CompanyAccess");

            migrationBuilder.EnsureSchema(
                name: "ref");

            migrationBuilder.CreateTable(
                name: "TaxIdentifierSchemes",
                schema: "ref",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryCode = table.Column<string>(type: "char(2)", nullable: false),
                    IdentifierType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    VatPrefix = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    FormatPattern = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    AlgorithmKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxIdentifierSchemes", x => x.Id);
                    table.CheckConstraint("CK_TaxIdentifierSchemes_Dates", "[ValidTo] IS NULL OR [ValidTo]>=[ValidFrom]");
                });

            migrationBuilder.InsertData(
                schema: "ref",
                table: "TaxIdentifierSchemes",
                columns: new[] { "Id", "AlgorithmKey", "CountryCode", "FormatPattern", "IdentifierType", "IsActive", "SourceUrl", "ValidFrom", "ValidTo", "VatPrefix", "Version" },
                values: new object[,]
                {
                    { 1, "format-only-v1", "AT", "^ATU[0-9]{8}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "AT", "2026-10-22.1" },
                    { 2, "format-only-v1", "BE", "^BE[0-9]{10}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "BE", "2026-10-22.1" },
                    { 3, "format-only-v1", "BG", "^BG[0-9]{9,10}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "BG", "2026-10-22.1" },
                    { 4, "format-only-v1", "HR", "^HR[0-9]{11}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "HR", "2026-10-22.1" },
                    { 5, "format-only-v1", "CY", "^CY[0-9]{8}[A-Z]$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "CY", "2026-10-22.1" },
                    { 6, "format-only-v1", "CZ", "^CZ[0-9]{8,10}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "CZ", "2026-10-22.1" },
                    { 7, "format-only-v1", "DK", "^DK[0-9]{8}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "DK", "2026-10-22.1" },
                    { 8, "format-only-v1", "EE", "^EE[0-9]{9}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "EE", "2026-10-22.1" },
                    { 9, "format-only-v1", "FI", "^FI[0-9]{8}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "FI", "2026-10-22.1" },
                    { 10, "format-only-v1", "FR", "^FR[A-Z0-9]{2}[0-9]{9}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "FR", "2026-10-22.1" },
                    { 11, "format-only-v1", "DE", "^DE[0-9]{9}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "DE", "2026-10-22.1" },
                    { 12, "format-only-v1", "GR", "^EL[0-9]{9}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "EL", "2026-10-22.1" },
                    { 13, "format-only-v1", "HU", "^HU[0-9]{8}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "HU", "2026-10-22.1" },
                    { 14, "format-only-v1", "IE", "^IE([0-9][A-Z0-9+*][0-9]{5}[A-Z]|[0-9]{7}[A-Z]{1,2})$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "IE", "2026-10-22.1" },
                    { 15, "format-only-v1", "IT", "^IT[0-9]{11}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "IT", "2026-10-22.1" },
                    { 16, "format-only-v1", "LV", "^LV[0-9]{11}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "LV", "2026-10-22.1" },
                    { 17, "format-only-v1", "LT", "^LT([0-9]{9}|[0-9]{12})$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "LT", "2026-10-22.1" },
                    { 18, "format-only-v1", "LU", "^LU[0-9]{8}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "LU", "2026-10-22.1" },
                    { 19, "format-only-v1", "MT", "^MT[0-9]{8}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "MT", "2026-10-22.1" },
                    { 20, "format-only-v1", "NL", "^NL[0-9]{9}B[0-9]{2}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "NL", "2026-10-22.1" },
                    { 21, "format-only-v1", "PT", "^PT[0-9]{9}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "PT", "2026-10-22.1" },
                    { 22, "pl-nip-mod11-v1", "PL", "^PL[0-9]{10}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "PL", "2026-10-22.1" },
                    { 23, "format-only-v1", "RO", "^RO[0-9]{2,10}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "RO", "2026-10-22.1" },
                    { 24, "format-only-v1", "SE", "^SE[0-9]{10}01$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "SE", "2026-10-22.1" },
                    { 25, "format-only-v1", "SK", "^SK[0-9]{10}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "SK", "2026-10-22.1" },
                    { 26, "format-only-v1", "SI", "^SI[0-9]{8}$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "SI", "2026-10-22.1" },
                    { 27, "format-only-v1", "ES", "^ES([A-Z][0-9]{7}[A-Z0-9]|[0-9]{8}[A-Z])$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "ES", "2026-10-22.1" },
                    { 28, "format-only-v1", "GB", "^XI([0-9]{9}|[0-9]{12}|(GD|HA)[0-9]{3})$", "VatEuId", true, "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/", new DateOnly(2026, 10, 22), null, "XI", "2026-10-22.1" },
                    { 29, "pl-nip-mod11-v1", "PL", "^[0-9]{10}$", "TaxId", true, "https://github.com/CIRFMF/ksef-client-csharp/blob/main/KSeF.Client.Tests.Utils/MiscellaneousUtils.cs", new DateOnly(2026, 10, 22), null, null, "2026-10-22.1" }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyAccess_Permissions",
                schema: "security",
                table: "CompanyAccess",
                sql: "[Permissions] > 0 AND [Permissions] <= 31");

            migrationBuilder.CreateIndex(
                name: "IX_TaxIdentifierSchemes_CountryCode_IdentifierType_Version",
                schema: "ref",
                table: "TaxIdentifierSchemes",
                columns: new[] { "CountryCode", "IdentifierType", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxIdentifierSchemes",
                schema: "ref");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyAccess_Permissions",
                schema: "security",
                table: "CompanyAccess");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyAccess_Permissions",
                schema: "security",
                table: "CompanyAccess",
                sql: "[Permissions] > 0 AND [Permissions] <= 15");
        }
    }
}
