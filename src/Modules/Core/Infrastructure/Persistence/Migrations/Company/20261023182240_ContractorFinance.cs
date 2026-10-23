using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.Company
{
    /// <inheritdoc />
    public partial class ContractorFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContractorCreditLimits",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractorId = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<string>(type: "varchar(8)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Amount = table.Column<decimal>(type: "decimal(24,8)", precision: 24, scale: 8, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractorCreditLimits", x => x.Id);
                    table.CheckConstraint("CK_CreditLimit_Amount", "[Amount]>=0");
                    table.CheckConstraint("CK_CreditLimit_Currency", "LEN([CurrencyCode])=3 AND [CurrencyCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
                    table.CheckConstraint("CK_CreditLimit_Direction", "[Direction] COLLATE Latin1_General_100_BIN2 IN ('Sale','Purchase')");
                    table.ForeignKey(
                        name: "FK_ContractorCreditLimits_Contractors_ContractorId",
                        column: x => x.ContractorId,
                        principalSchema: "core",
                        principalTable: "Contractors",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContractorCreditLimits_Currencies_CurrencyCode",
                        column: x => x.CurrencyCode,
                        principalSchema: "ref",
                        principalTable: "Currencies",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                schema: "ref",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(32)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethods", x => x.Id);
                    table.UniqueConstraint("AK_PaymentMethods_Code", x => x.Code);
                    table.CheckConstraint("CK_PaymentMethods_Code", "[Code] COLLATE Latin1_General_100_BIN2 LIKE '[A-Z]%' AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%'");
                    table.CheckConstraint("CK_PaymentMethods_Name", "LEN(LTRIM(RTRIM([Name])))>0");
                });

            migrationBuilder.CreateTable(
                name: "ContractorTradeTerms",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractorId = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<string>(type: "varchar(8)", nullable: false),
                    PaymentDays = table.Column<int>(type: "int", nullable: true),
                    PaymentMethodCode = table.Column<string>(type: "varchar(32)", nullable: true),
                    CurrencyCode = table.Column<string>(type: "char(3)", nullable: true, collation: "Latin1_General_100_BIN2"),
                    DiscountPercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractorTradeTerms", x => x.Id);
                    table.CheckConstraint("CK_TradeTerms_Currency", "[CurrencyCode] IS NULL OR (LEN([CurrencyCode])=3 AND [CurrencyCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%')");
                    table.CheckConstraint("CK_TradeTerms_Days", "[PaymentDays] IS NULL OR [PaymentDays] BETWEEN 0 AND 3650");
                    table.CheckConstraint("CK_TradeTerms_Direction", "[Direction] COLLATE Latin1_General_100_BIN2 IN ('Sale','Purchase')");
                    table.CheckConstraint("CK_TradeTerms_Discount", "[DiscountPercent] IS NULL OR [DiscountPercent] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_ContractorTradeTerms_Contractors_ContractorId",
                        column: x => x.ContractorId,
                        principalSchema: "core",
                        principalTable: "Contractors",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContractorTradeTerms_Currencies_CurrencyCode",
                        column: x => x.CurrencyCode,
                        principalSchema: "ref",
                        principalTable: "Currencies",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_ContractorTradeTerms_PaymentMethods_PaymentMethodCode",
                        column: x => x.PaymentMethodCode,
                        principalSchema: "ref",
                        principalTable: "PaymentMethods",
                        principalColumn: "Code");
                });

            migrationBuilder.InsertData(
                schema: "ref",
                table: "PaymentMethods",
                columns: new[] { "Id", "Code", "CreatedAtUtc", "IsActive", "Name", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { 1, "TRANSFER", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Przelew", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 2, "CASH", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Gotówka", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 3, "CARD", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Karta", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 4, "OFFSET", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Kompensata", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { 5, "OTHER", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Inny", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractorCreditLimits_ContractorId_Direction_CurrencyCode",
                schema: "core",
                table: "ContractorCreditLimits",
                columns: new[] { "ContractorId", "Direction", "CurrencyCode" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ContractorCreditLimits_CurrencyCode",
                schema: "core",
                table: "ContractorCreditLimits",
                column: "CurrencyCode");

            migrationBuilder.CreateIndex(
                name: "IX_ContractorTradeTerms_ContractorId_Direction",
                schema: "core",
                table: "ContractorTradeTerms",
                columns: new[] { "ContractorId", "Direction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractorTradeTerms_CurrencyCode",
                schema: "core",
                table: "ContractorTradeTerms",
                column: "CurrencyCode");

            migrationBuilder.CreateIndex(
                name: "IX_ContractorTradeTerms_PaymentMethodCode",
                schema: "core",
                table: "ContractorTradeTerms",
                column: "PaymentMethodCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContractorCreditLimits",
                schema: "core");

            migrationBuilder.DropTable(
                name: "ContractorTradeTerms",
                schema: "core");

            migrationBuilder.DropTable(
                name: "PaymentMethods",
                schema: "ref");
        }
    }
}
