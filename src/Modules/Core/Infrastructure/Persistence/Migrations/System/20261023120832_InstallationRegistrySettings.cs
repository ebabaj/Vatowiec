using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class InstallationRegistrySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "RegistryConfigurationChanges",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "varchar(4)", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorIssuerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistryConfigurationChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegistryConfigurations",
                schema: "security",
                columns: table => new
                {
                    Source = table.Column<string>(type: "varchar(4)", nullable: false),
                    Environment = table.Column<string>(type: "varchar(10)", nullable: false),
                    TestUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    ProductionUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    TestWl2Url = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ProductionWl2Url = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistryConfigurations", x => x.Source);
                    table.CheckConstraint("CK_RegistryConfigurations_Environment", "[Environment] IN ('Test','Production')");
                    table.CheckConstraint("CK_RegistryConfigurations_Source", "[Source] IN ('GUS','MF','VIES')");
                    table.CheckConstraint("CK_RegistryConfigurations_Wl2", "([Source]='MF' AND [TestWl2Url] IS NOT NULL AND [ProductionWl2Url] IS NOT NULL) OR ([Source]<>'MF' AND [TestWl2Url] IS NULL AND [ProductionWl2Url] IS NULL)");
                });

            migrationBuilder.InsertData(
                schema: "security",
                table: "RegistryConfigurations",
                columns: new[] { "Source", "Environment", "ProductionUrl", "ProductionWl2Url", "TestUrl", "TestWl2Url" },
                values: new object[,]
                {
                    { "GUS", "Test", "https://wyszukiwarkaregon.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc", null, "https://wyszukiwarkaregontest.stat.gov.pl/wsBIR/UslugaBIRzewnPubl.svc", null },
                    { "MF", "Test", "https://wykazpodatnikowvat.mf.gov.pl/api/", "https://wykazpodatnikowvat.mf.gov.pl/api2/", "https://wykazpodatnikowvat-test.mf.gov.pl/api/", "https://wykazpodatnikowvat-test.mf.gov.pl/api2/" },
                    { "VIES", "Test", "https://ec.europa.eu/taxation_customs/vies/services/checkVatService", null, "https://ec.europa.eu/taxation_customs/vies/test-services/checkVatTestService", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegistryConfigurationChanges_Source_OccurredAtUtc",
                schema: "audit",
                table: "RegistryConfigurationChanges",
                columns: new[] { "Source", "OccurredAtUtc" });
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER audit.TR_RegistryConfigurationChanges_Immutable
                ON audit.RegistryConfigurationChanges AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, ''Registry configuration audit is immutable'', 1;
                END;');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new global::System.NotSupportedException("Audyt konfiguracji rejestrów nie jest usuwany przez downgrade. Zastosuj nową migrację zachowującą historię.");
    }
}
