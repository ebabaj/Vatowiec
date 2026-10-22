using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.Company
{
    /// <inheritdoc />
    public partial class ContractorDetailAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ContractorCreateOperations",
                schema: "core",
                table: "ContractorCreateOperations");

            migrationBuilder.AlterColumn<int>(
                name: "ContractorId",
                schema: "core",
                table: "ContractorCreateOperations",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "OperationKind",
                schema: "core",
                table: "ContractorCreateOperations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Contractor");

            migrationBuilder.AlterColumn<int>(
                name: "ContractorId",
                schema: "audit",
                table: "ContractorChanges",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "RecordId",
                schema: "audit",
                table: "ContractorChanges",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RecordType",
                schema: "audit",
                table: "ContractorChanges",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Contractor");

            migrationBuilder.AddColumn<string>(
                name: "WarningJson",
                schema: "audit",
                table: "ContractorChanges",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ContractorCreateOperations",
                schema: "core",
                table: "ContractorCreateOperations",
                columns: new[] { "ActorIssuerId", "ActorId", "OperationKind", "OperationId" });
            // Uzupełnienie nowej metadanej istniejących zdarzeń; bez zmiany wartości historycznych.
            migrationBuilder.Sql("UPDATE audit.ContractorChanges SET RecordId=ContractorId WHERE ContractorId IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ContractorCreateOperations",
                schema: "core",
                table: "ContractorCreateOperations");

            migrationBuilder.DropColumn(
                name: "OperationKind",
                schema: "core",
                table: "ContractorCreateOperations");

            migrationBuilder.DropColumn(
                name: "RecordId",
                schema: "audit",
                table: "ContractorChanges");

            migrationBuilder.DropColumn(
                name: "RecordType",
                schema: "audit",
                table: "ContractorChanges");

            migrationBuilder.DropColumn(
                name: "WarningJson",
                schema: "audit",
                table: "ContractorChanges");

            migrationBuilder.AlterColumn<int>(
                name: "ContractorId",
                schema: "core",
                table: "ContractorCreateOperations",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ContractorId",
                schema: "audit",
                table: "ContractorChanges",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ContractorCreateOperations",
                schema: "core",
                table: "ContractorCreateOperations",
                columns: new[] { "ActorIssuerId", "ActorId", "OperationId" });
        }
    }
}
