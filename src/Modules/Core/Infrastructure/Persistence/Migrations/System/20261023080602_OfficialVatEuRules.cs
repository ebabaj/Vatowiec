using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class OfficialVatEuRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "ref",
                table: "TaxIdentifierSchemes",
                keyColumn: "Id",
                keyValue: 2,
                column: "ValidTo",
                value: new DateOnly(2026, 10, 22));

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "TaxIdentifierSchemes",
                keyColumn: "Id",
                keyValue: 4,
                column: "ValidTo",
                value: new DateOnly(2026, 10, 22));

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "TaxIdentifierSchemes",
                keyColumn: "Id",
                keyValue: 9,
                column: "ValidTo",
                value: new DateOnly(2026, 10, 22));

            migrationBuilder.InsertData(
                schema: "ref",
                table: "TaxIdentifierSchemes",
                columns: new[] { "Id", "AlgorithmKey", "CountryCode", "FormatPattern", "IdentifierType", "IsActive", "SourceUrl", "ValidFrom", "ValidTo", "VatPrefix", "Version" },
                values: new object[,]
                {
                    { 30, "format-only-v1", "BE", "^BE[01][0-9]{9}$", "VatEuId", true, "https://news.economie.fgov.be/228779-les-numeros-d-entreprise-passent-au-1/", new DateOnly(2026, 10, 23), null, "BE", "2026-10-23.1" },
                    { 31, "fi-vat-mod11-v1", "FI", "^FI[0-9]{8}$", "VatEuId", true, "https://www.vero.fi/globalassets/tietoa-verohallinnosta/ohjelmistokehittajille/finnish-tax-administration_the-control-character-for-verifying-the-authenticity-of-finnish-business-ids-and-personal-identity-codes.pdf", new DateOnly(2026, 10, 23), null, "FI", "2026-10-23.1" },
                    { 32, "hr-vat-iso7064-mod11-10-v1", "HR", "^HR[0-9]{11}$", "VatEuId", true, "https://regos.hr/app/uploads/2018/07/KONTROLA-OIB-a.pdf", new DateOnly(2026, 10, 23), null, "HR", "2026-10-23.1" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Historia reguł VAT UE nie jest usuwana. Wycofaj decyzję przez nową wersję reguły.");
    }
}
