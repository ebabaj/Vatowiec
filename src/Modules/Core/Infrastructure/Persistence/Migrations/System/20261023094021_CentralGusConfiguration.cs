using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class CentralGusConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GusConfiguration",
                schema: "security",
                columns: table => new
                {
                    SingletonKey = table.Column<int>(type: "int", nullable: false),
                    ProtectedApiKey = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GusConfiguration", x => x.SingletonKey);
                    table.CheckConstraint("CK_GusConfiguration_Singleton", "[SingletonKey] = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GusConfiguration",
                schema: "security");
        }
    }
}
