using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.Company
{
    /// <inheritdoc />
    public partial class ExternalVerifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalVerifications",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractorId = table.Column<int>(type: "int", nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TaxId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VatEuId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AccountNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StateDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ServiceStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OfficeReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdapterVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RuleVersion = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Executed = table.Column<bool>(type: "bit", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorIssuerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalVerifications", x => x.Id);
                    table.CheckConstraint("CK_ExternalVerifications_Actor", "[ActorId]<>'00000000-0000-0000-0000-000000000000' AND [ActorIssuerId]<>'00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ExternalVerifications_Environment", "[Environment] IN ('Test','Production','Mock')");
                    table.CheckConstraint("CK_ExternalVerifications_Json", "ISJSON([DataJson])=1");
                    table.ForeignKey(
                        name: "FK_ExternalVerifications_ContractorBankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalSchema: "core",
                        principalTable: "ContractorBankAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ExternalVerifications_Contractors_ContractorId",
                        column: x => x.ContractorId,
                        principalSchema: "core",
                        principalTable: "Contractors",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalVerifications_ActorIssuerId_ActorId_OperationId",
                schema: "audit",
                table: "ExternalVerifications",
                columns: new[] { "ActorIssuerId", "ActorId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalVerifications_BankAccountId",
                schema: "audit",
                table: "ExternalVerifications",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalVerifications_ContractorId_StartedAtUtc",
                schema: "audit",
                table: "ExternalVerifications",
                columns: new[] { "ContractorId", "StartedAtUtc" });
            migrationBuilder.Sql("""
                CREATE TRIGGER audit.TR_ExternalVerifications_Immutable
                ON audit.ExternalVerifications AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51002, N'Historia weryfikacji jest niezmienna.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalVerifications",
                schema: "audit");
        }
    }
}
