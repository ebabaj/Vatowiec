using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class ReferenceCurrenciesBgnAng : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "ref",
                table: "Currencies",
                columns: new[] { "Code", "Name" },
                values: new object[,]
                {
                    { "ANG", "gulden Antyli Holenderskich" },
                    { "BGN", "lew bułgarski" }
                });

            // ADR-048: nowe kody w słowniku wymagają dociągnięcia ich historii. Zerujemy punkty kontrolne —
            // usługa Operations pobierze tabele od daty początkowej, a zapisane kursy pominie jako identyczne.
            migrationBuilder.Sql("UPDATE ops.ExchangeRateSync SET CheckedThroughA = NULL, CheckedThroughB = NULL, CheckedThroughC = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "ANG");

            migrationBuilder.DeleteData(
                schema: "ref",
                table: "Currencies",
                keyColumn: "Code",
                keyValue: "BGN");
        }
    }
}
