using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.Company
{
    /// <inheritdoc />
    public partial class StandardContractorIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bdo",
                schema: "core",
                table: "Contractors",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Krs",
                schema: "core",
                table: "Contractors",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pesel",
                schema: "core",
                table: "Contractors",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Regon",
                schema: "core",
                table: "Contractors",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bdo",
                schema: "core",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Krs",
                schema: "core",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Pesel",
                schema: "core",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "Regon",
                schema: "core",
                table: "Contractors");
        }
    }
}
