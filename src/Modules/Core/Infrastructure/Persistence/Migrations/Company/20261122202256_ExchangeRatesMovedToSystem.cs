using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.Company
{
    /// <inheritdoc />
    public partial class ExchangeRatesMovedToSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-048: kursy NBP przechodzą do wspólnej bazy System. Historia importów i konfliktów z bazy firmy
            // jest usuwana (bazy developerskie, kursy nie są jeszcze dowodem księgowym); zostaje historia aktywności walut.
            migrationBuilder.Sql("DISABLE TRIGGER audit.TR_CurrencyOperations_Immutable ON audit.CurrencyOperations;");
            migrationBuilder.Sql("DELETE FROM audit.CurrencyOperations WHERE Kind IN (N'Import', N'ResolveConflict');");
            migrationBuilder.Sql("ENABLE TRIGGER audit.TR_CurrencyOperations_Immutable ON audit.CurrencyOperations;");

            migrationBuilder.DropTable(
                name: "ExchangeRates",
                schema: "core");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Currencies_Active",
                schema: "ref",
                table: "Currencies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Currencies_PLN",
                schema: "ref",
                table: "Currencies");

            migrationBuilder.DropColumn(
                name: "DownloadA",
                schema: "ref",
                table: "Currencies");

            migrationBuilder.DropColumn(
                name: "DownloadB",
                schema: "ref",
                table: "Currencies");

            migrationBuilder.DropColumn(
                name: "DownloadC",
                schema: "ref",
                table: "Currencies");

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AED",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AFN",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ALL",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AMD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AOA",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ARS",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AUD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AWG",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AZN",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BAM",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BBD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BDT",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BHD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BIF",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BND",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BOB",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BRL",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BSD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BWP",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BYN",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BZD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CAD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CDF",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CLP",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CNY",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "COP",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CRC",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CUP",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CVE",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CZK",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "DJF",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "DKK",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "DOP",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "DZD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "EGP",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ERN",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ETB",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "FJD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GEL",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GHS",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GIP",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GMD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GNF",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GTQ",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GYD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "HKD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "HNL",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "HTG",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "HUF",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "IDR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ILS",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "INR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "IQD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "IRR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ISK",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "JMD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "JOD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "JPY",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KES",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KGS",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KHR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KMF",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KRW",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KWD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KZT",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "LAK",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "LBP",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "LKR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "LRD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "LSL",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "LYD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MAD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MDL",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MGA",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MKD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MMK",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MNT",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MOP",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MRU",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MUR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MVR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MWK",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MXN",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MYR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MZN",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "NAD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "NGN",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "NIO",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "NOK",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "NPR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "NZD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "OMR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "PAB",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "PEN",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "PGK",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "PHP",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "PKR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "PYG",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "QAR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "RON",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "RSD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "RUB",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "RWF",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SAR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SBD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SCR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SDG",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SEK",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SGD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SLE",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SOS",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SRD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SSP",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "STN",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SVC",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SYP",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SZL",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "THB",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TJS",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TMT",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TND",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TOP",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TRY",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TTD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TWD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TZS",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "UAH",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "UGX",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "UYU",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "UZS",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "VES",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "VND",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "VUV",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "WST",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "XAF",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "XCD",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "XCG",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "XDR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "XOF",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "XPF",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "YER",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ZAR",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ZMW",
                column: "IsActive",
                value: false);

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ZWG",
                column: "IsActive",
                value: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Currencies_PLN",
                schema: "ref",
                table: "Currencies",
                sql: "[Code] <> 'PLN' OR [IsActive]=1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Currencies_PLN",
                schema: "ref",
                table: "Currencies");

            migrationBuilder.AddColumn<bool>(
                name: "DownloadA",
                schema: "ref",
                table: "Currencies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DownloadB",
                schema: "ref",
                table: "Currencies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DownloadC",
                schema: "ref",
                table: "Currencies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ask = table.Column<decimal>(type: "decimal(24,12)", precision: 24, scale: 12, nullable: true),
                    Bid = table.Column<decimal>(type: "decimal(24,12)", precision: 24, scale: 12, nullable: true),
                    CurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Mid = table.Column<decimal>(type: "decimal(24,12)", precision: 24, scale: 12, nullable: true),
                    RetrievedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    Source = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    TableNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TableType = table.Column<string>(type: "char(1)", nullable: false),
                    TradingDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                    table.CheckConstraint("CK_ExchangeRates_Values", "[Source]='NBP' AND (([TableType] IN ('A','B') AND [Mid]>0 AND [Mid] IS NOT NULL AND [Bid] IS NULL AND [Ask] IS NULL AND [TradingDate] IS NULL) OR ([TableType]='C' AND [Mid] IS NULL AND [Bid]>0 AND [Ask]>0 AND [Bid] IS NOT NULL AND [Ask] IS NOT NULL AND [Bid]<=[Ask] AND ([TradingDate] IS NULL OR [TradingDate]<=[EffectiveDate])))");
                    table.ForeignKey(
                        name: "FK_ExchangeRates_Currencies_CurrencyCode",
                        column: x => x.CurrencyCode,
                        principalSchema: "ref",
                        principalTable: "Currencies",
                        principalColumn: "Code");
                });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AED",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AFN",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ALL",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AMD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AOA",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ARS",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AUD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AWG",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "AZN",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BAM",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BBD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BDT",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BHD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BIF",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BND",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BOB",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BRL",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BSD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BWP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BYN",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BZD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CAD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CDF",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CHF",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC" },
                values: new object[] { true, false, false });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CLP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CNY",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "COP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CRC",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CUP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CVE",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "CZK",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "DJF",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "DKK",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "DOP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "DZD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "EGP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ERN",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ETB",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "EUR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC" },
                values: new object[] { true, false, false });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "FJD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GBP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC" },
                values: new object[] { true, false, false });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GEL",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GHS",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GIP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GMD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GNF",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GTQ",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "GYD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "HKD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "HNL",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "HTG",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "HUF",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "IDR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ILS",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "INR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "IQD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "IRR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ISK",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "JMD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "JOD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "JPY",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KES",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KGS",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KHR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KMF",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KRW",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KWD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "KZT",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "LAK",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "LBP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "LKR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "LRD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "LSL",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "LYD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MAD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MDL",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MGA",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MKD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MMK",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MNT",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MOP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MRU",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MUR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MVR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MWK",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MXN",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MYR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "MZN",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "NAD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "NGN",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "NIO",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "NOK",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "NPR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "NZD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "OMR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "PAB",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "PEN",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "PGK",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "PHP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "PKR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "PLN",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC" },
                values: new object[] { false, false, false });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "PYG",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "QAR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "RON",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "RSD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "RUB",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "RWF",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SAR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SBD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SCR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SDG",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SEK",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SGD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SLE",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SOS",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SRD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SSP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "STN",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SVC",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SYP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "SZL",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "THB",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TJS",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TMT",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TND",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TOP",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TRY",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TTD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TWD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "TZS",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "UAH",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "UGX",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "USD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC" },
                values: new object[] { true, false, false });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "UYU",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "UZS",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "VES",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "VND",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "VUV",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "WST",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "XAF",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "XCD",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "XCG",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "XDR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "XOF",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "XPF",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "YER",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ZAR",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ZMW",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.UpdateData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ZWG",
                columns: new[] { "DownloadA", "DownloadB", "DownloadC", "IsActive" },
                values: new object[] { false, false, false, true });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Currencies_Active",
                schema: "ref",
                table: "Currencies",
                sql: "[IsActive]=1 OR ([DownloadA]=0 AND [DownloadB]=0 AND [DownloadC]=0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Currencies_PLN",
                schema: "ref",
                table: "Currencies",
                sql: "[Code] <> 'PLN' OR ([IsActive]=1 AND [DownloadA]=0 AND [DownloadB]=0 AND [DownloadC]=0)");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_CurrencyCode_Source_TableType_EffectiveDate",
                schema: "core",
                table: "ExchangeRates",
                columns: new[] { "CurrencyCode", "Source", "TableType", "EffectiveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_EffectiveDate",
                schema: "core",
                table: "ExchangeRates",
                column: "EffectiveDate");
        }
    }
}
