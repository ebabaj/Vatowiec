using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.Company
{
    /// <inheritdoc />
    public partial class CurrenciesBgnAng : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "ref",
                table: "Currencies",
                columns: new[] { "Code", "IsActive", "Name" },
                values: new object[,]
                {
                    { "ANG", false, "gulden Antyli Holenderskich" },
                    { "BGN", false, "lew bułgarski" }
                });
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
