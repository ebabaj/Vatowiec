using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.Company
{
    /// <inheritdoc />
    public partial class CurrenciesAndNbpRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Currencies",
                schema: "ref",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "char(3)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DownloadA = table.Column<bool>(type: "bit", nullable: false),
                    DownloadB = table.Column<bool>(type: "bit", nullable: false),
                    DownloadC = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                    table.CheckConstraint("CK_Currencies_Active", "[IsActive]=1 OR ([DownloadA]=0 AND [DownloadB]=0 AND [DownloadC]=0)");
                    table.CheckConstraint("CK_Currencies_Code", "[Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%' AND LEN([Code])=3");
                    table.CheckConstraint("CK_Currencies_PLN", "[Code] <> 'PLN' OR ([IsActive]=1 AND [DownloadA]=0 AND [DownloadB]=0 AND [DownloadC]=0)");
                });

            migrationBuilder.CreateTable(
                name: "CurrencyOperations",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorIssuerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CurrencyId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    TableType = table.Column<string>(type: "char(1)", nullable: false),
                    TableNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TradingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Mid = table.Column<decimal>(type: "decimal(24,12)", precision: 24, scale: 12, nullable: true),
                    Bid = table.Column<decimal>(type: "decimal(24,12)", precision: 24, scale: 12, nullable: true),
                    Ask = table.Column<decimal>(type: "decimal(24,12)", precision: 24, scale: 12, nullable: true),
                    RetrievedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                    table.CheckConstraint("CK_ExchangeRates_Values", "[Source]='NBP' AND (([TableType] IN ('A','B') AND [Mid]>0 AND [Mid] IS NOT NULL AND [Bid] IS NULL AND [Ask] IS NULL AND [TradingDate] IS NULL) OR ([TableType]='C' AND [Mid] IS NULL AND [Bid]>0 AND [Ask]>0 AND [Bid] IS NOT NULL AND [Ask] IS NOT NULL AND [Bid]<=[Ask] AND [TradingDate] IS NOT NULL AND [TradingDate]<=[EffectiveDate]))");
                    table.ForeignKey(
                        name: "FK_ExchangeRates_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalSchema: "ref",
                        principalTable: "Currencies",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "ref",
                table: "Currencies",
                columns: new[] { "Id", "Code", "DownloadA", "DownloadB", "DownloadC", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "PLN", false, false, false, true, "złoty polski" },
                    { 2, "THB", false, false, false, true, "bat tajlandzki" },
                    { 3, "USD", true, false, false, true, "dolar amerykański" },
                    { 4, "AUD", false, false, false, true, "dolar australijski" },
                    { 5, "HKD", false, false, false, true, "dolar Hongkongu" },
                    { 6, "CAD", false, false, false, true, "dolar kanadyjski" },
                    { 7, "NZD", false, false, false, true, "dolar nowozelandzki" },
                    { 8, "SGD", false, false, false, true, "dolar singapurski" },
                    { 9, "EUR", true, false, false, true, "euro" },
                    { 10, "HUF", false, false, false, true, "forint węgierski" },
                    { 11, "CHF", true, false, false, true, "frank szwajcarski" },
                    { 12, "GBP", true, false, false, true, "funt szterling" },
                    { 13, "UAH", false, false, false, true, "hrywna ukraińska" },
                    { 14, "JPY", false, false, false, true, "jen japoński" },
                    { 15, "CZK", false, false, false, true, "korona czeska" },
                    { 16, "DKK", false, false, false, true, "korona duńska" },
                    { 17, "ISK", false, false, false, true, "korona islandzka" },
                    { 18, "NOK", false, false, false, true, "korona norweska" },
                    { 19, "SEK", false, false, false, true, "korona szwedzka" },
                    { 20, "RON", false, false, false, true, "lej rumuński" },
                    { 21, "TRY", false, false, false, true, "lira turecka" },
                    { 22, "ILS", false, false, false, true, "szekel izraelski" },
                    { 23, "CLP", false, false, false, true, "peso chilijskie" },
                    { 24, "PHP", false, false, false, true, "peso filipińskie" },
                    { 25, "MXN", false, false, false, true, "peso meksykańskie" },
                    { 26, "ZAR", false, false, false, true, "rand południowoafrykański" },
                    { 27, "BRL", false, false, false, true, "real brazylijski" },
                    { 28, "MYR", false, false, false, true, "ringgit malezyjski" },
                    { 29, "IDR", false, false, false, true, "rupia indonezyjska" },
                    { 30, "INR", false, false, false, true, "rupia indyjska" },
                    { 31, "KRW", false, false, false, true, "won południowokoreański" },
                    { 32, "CNY", false, false, false, true, "juan chiński" },
                    { 33, "XDR", false, false, false, true, "SDR — jednostka rozrachunkowa MFW" },
                    { 34, "AFN", false, false, false, true, "afgani afgańskie" },
                    { 35, "MGA", false, false, false, true, "ariary malgaski" },
                    { 36, "PAB", false, false, false, true, "balboa panamska" },
                    { 37, "ETB", false, false, false, true, "birr etiopski" },
                    { 38, "VES", false, false, false, true, "boliwar wenezuelski" },
                    { 39, "BOB", false, false, false, true, "boliwiano boliwijskie" },
                    { 40, "CRC", false, false, false, true, "colon kostarykański" },
                    { 41, "SVC", false, false, false, true, "colon salwadorski" },
                    { 42, "NIO", false, false, false, true, "cordoba nikaraguańska" },
                    { 43, "GMD", false, false, false, true, "dalasi gambijskie" },
                    { 44, "MKD", false, false, false, true, "denar macedoński" },
                    { 45, "DZD", false, false, false, true, "dinar algierski" },
                    { 46, "BHD", false, false, false, true, "dinar bahrajski" },
                    { 47, "IQD", false, false, false, true, "dinar iracki" },
                    { 48, "JOD", false, false, false, true, "dinar jordański" },
                    { 49, "KWD", false, false, false, true, "dinar kuwejcki" },
                    { 50, "LYD", false, false, false, true, "dinar libijski" },
                    { 51, "RSD", false, false, false, true, "dinar serbski" },
                    { 52, "TND", false, false, false, true, "dinar tunezyjski" },
                    { 53, "MAD", false, false, false, true, "dirham marokański" },
                    { 54, "AED", false, false, false, true, "dirham Zjednoczonych Emiratów Arabskich" },
                    { 55, "STN", false, false, false, true, "dobra Wysp Świętego Tomasza i Książęcej" },
                    { 56, "BSD", false, false, false, true, "dolar bahamski" },
                    { 57, "BBD", false, false, false, true, "dolar barbadoski" },
                    { 58, "BZD", false, false, false, true, "dolar belizeński" },
                    { 59, "BND", false, false, false, true, "dolar brunejski" },
                    { 60, "FJD", false, false, false, true, "dolar Fidżi" },
                    { 61, "GYD", false, false, false, true, "dolar gujański" },
                    { 62, "JMD", false, false, false, true, "dolar jamajski" },
                    { 63, "LRD", false, false, false, true, "dolar liberyjski" },
                    { 64, "NAD", false, false, false, true, "dolar namibijski" },
                    { 65, "SRD", false, false, false, true, "dolar surinamski" },
                    { 66, "TTD", false, false, false, true, "dolar Trynidadu i Tobago" },
                    { 67, "XCD", false, false, false, true, "dolar wschodniokaraibski" },
                    { 68, "SBD", false, false, false, true, "dolar Wysp Salomona" },
                    { 69, "VND", false, false, false, true, "dong wietnamski" },
                    { 70, "AMD", false, false, false, true, "dram armeński" },
                    { 71, "CVE", false, false, false, true, "escudo Zielonego Przylądka" },
                    { 72, "AWG", false, false, false, true, "florin arubański" },
                    { 73, "BIF", false, false, false, true, "frank burundyjski" },
                    { 74, "XOF", false, false, false, true, "frank CFA BCEAO" },
                    { 75, "XAF", false, false, false, true, "frank CFA BEAC" },
                    { 76, "XPF", false, false, false, true, "frank CFP" },
                    { 77, "DJF", false, false, false, true, "frank Dżibuti" },
                    { 78, "GNF", false, false, false, true, "frank gwinejski" },
                    { 79, "KMF", false, false, false, true, "frank Komorów" },
                    { 80, "CDF", false, false, false, true, "frank kongijski" },
                    { 81, "RWF", false, false, false, true, "frank rwandyjski" },
                    { 82, "EGP", false, false, false, true, "funt egipski" },
                    { 83, "GIP", false, false, false, true, "funt gibraltarski" },
                    { 84, "LBP", false, false, false, true, "funt libański" },
                    { 85, "SSP", false, false, false, true, "funt południowosudański" },
                    { 86, "SDG", false, false, false, true, "funt sudański" },
                    { 87, "SYP", false, false, false, true, "funt syryjski" },
                    { 88, "GHS", false, false, false, true, "cedi ghański" },
                    { 89, "HTG", false, false, false, true, "gourde haitańskie" },
                    { 90, "PYG", false, false, false, true, "guarani paragwajskie" },
                    { 91, "XCG", false, false, false, true, "gulden karaibski" },
                    { 92, "PGK", false, false, false, true, "kina papuaska" },
                    { 93, "LAK", false, false, false, true, "kip laotański" },
                    { 94, "MWK", false, false, false, true, "kwacha malawijska" },
                    { 95, "ZMW", false, false, false, true, "kwacha zambijska" },
                    { 96, "AOA", false, false, false, true, "kwanza angolska" },
                    { 97, "MMK", false, false, false, true, "kiat birmański" },
                    { 98, "GEL", false, false, false, true, "lari gruzińskie" },
                    { 99, "MDL", false, false, false, true, "lej mołdawski" },
                    { 100, "ALL", false, false, false, true, "lek albański" },
                    { 101, "HNL", false, false, false, true, "lempira honduraska" },
                    { 102, "SLE", false, false, false, true, "leone Sierra Leone" },
                    { 103, "SZL", false, false, false, true, "lilangeni Eswatini" },
                    { 104, "LSL", false, false, false, true, "loti Lesotho" },
                    { 105, "AZN", false, false, false, true, "manat azerbejdżański" },
                    { 106, "MZN", false, false, false, true, "metical mozambicki" },
                    { 107, "NGN", false, false, false, true, "naira nigeryjska" },
                    { 108, "ERN", false, false, false, true, "nakfa erytrejska" },
                    { 109, "TWD", false, false, false, true, "dolar tajwański" },
                    { 110, "TMT", false, false, false, true, "manat turkmeński" },
                    { 111, "MRU", false, false, false, true, "ouguiya mauretańska" },
                    { 112, "TOP", false, false, false, true, "paʻanga tongijska" },
                    { 113, "MOP", false, false, false, true, "pataca Makau" },
                    { 114, "ARS", false, false, false, true, "peso argentyńskie" },
                    { 115, "DOP", false, false, false, true, "peso dominikańskie" },
                    { 116, "COP", false, false, false, true, "peso kolumbijskie" },
                    { 117, "CUP", false, false, false, true, "peso kubańskie" },
                    { 118, "UYU", false, false, false, true, "peso urugwajskie" },
                    { 119, "BWP", false, false, false, true, "pula botswańska" },
                    { 120, "GTQ", false, false, false, true, "quetzal gwatemalski" },
                    { 121, "IRR", false, false, false, true, "rial irański" },
                    { 122, "YER", false, false, false, true, "rial jemeński" },
                    { 123, "QAR", false, false, false, true, "rial katarski" },
                    { 124, "OMR", false, false, false, true, "rial omański" },
                    { 125, "SAR", false, false, false, true, "rial saudyjski" },
                    { 126, "KHR", false, false, false, true, "riel kambodżański" },
                    { 127, "BYN", false, false, false, true, "rubel białoruski" },
                    { 128, "RUB", false, false, false, true, "rubel rosyjski" },
                    { 129, "LKR", false, false, false, true, "rupia lankijska" },
                    { 130, "MVR", false, false, false, true, "rupia malediwska" },
                    { 131, "MUR", false, false, false, true, "rupia Mauritiusu" },
                    { 132, "NPR", false, false, false, true, "rupia nepalska" },
                    { 133, "PKR", false, false, false, true, "rupia pakistańska" },
                    { 134, "SCR", false, false, false, true, "rupia seszelska" },
                    { 135, "PEN", false, false, false, true, "sol peruwiański" },
                    { 136, "KGS", false, false, false, true, "som kirgiski" },
                    { 137, "TJS", false, false, false, true, "somoni tadżyckie" },
                    { 138, "UZS", false, false, false, true, "sum uzbecki" },
                    { 139, "KES", false, false, false, true, "szyling kenijski" },
                    { 140, "SOS", false, false, false, true, "szyling somalijski" },
                    { 141, "TZS", false, false, false, true, "szyling tanzański" },
                    { 142, "UGX", false, false, false, true, "szyling ugandyjski" },
                    { 143, "BDT", false, false, false, true, "taka bengalska" },
                    { 144, "WST", false, false, false, true, "tala samoańska" },
                    { 145, "KZT", false, false, false, true, "tenge kazachskie" },
                    { 146, "MNT", false, false, false, true, "tugrik mongolski" },
                    { 147, "VUV", false, false, false, true, "vatu Vanuatu" },
                    { 148, "BAM", false, false, false, true, "marka zamienna Bośni i Hercegowiny" },
                    { 149, "ZWG", false, false, false, true, "złoto Zimbabwe" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Code",
                schema: "ref",
                table: "Currencies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyOperations_ActorId_ActorIssuerId_OperationId",
                schema: "audit",
                table: "CurrencyOperations",
                columns: new[] { "ActorId", "ActorIssuerId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_CurrencyId_Source_TableType_EffectiveDate",
                schema: "core",
                table: "ExchangeRates",
                columns: new[] { "CurrencyId", "Source", "TableType", "EffectiveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_EffectiveDate",
                schema: "core",
                table: "ExchangeRates",
                column: "EffectiveDate");
            migrationBuilder.Sql("""
                CREATE TRIGGER audit.TR_CurrencyOperations_Immutable
                ON audit.CurrencyOperations AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51003, N'Historia operacji walutowych jest niezmienna.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurrencyOperations",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "ExchangeRates",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Currencies",
                schema: "ref");
        }
    }
}
