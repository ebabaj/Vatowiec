using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class CompanyIntegrationPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyAccess_Permissions",
                schema: "security",
                table: "CompanyAccess");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyAccess_Permissions",
                schema: "security",
                table: "CompanyAccess",
                sql: "[Permissions] > 0 AND [Permissions] <= 63");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyAccess_Permissions",
                schema: "security",
                table: "CompanyAccess");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyAccess_Permissions",
                schema: "security",
                table: "CompanyAccess",
                sql: "[Permissions] > 0 AND [Permissions] <= 31");
        }
    }
}
