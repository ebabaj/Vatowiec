using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.System
{
    /// <inheritdoc />
    public partial class PublicExchangeRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Currencies",
                schema: "ref",
                columns: table => new
                {
                    Code = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Code);
                    table.CheckConstraint("CK_Currencies_Code", "[Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%' AND LEN([Code])=3");
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRateOperations",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRateOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRateSync",
                schema: "ops",
                columns: table => new
                {
                    SingletonKey = table.Column<byte>(type: "tinyint", nullable: false),
                    HistoryStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CheckedThroughA = table.Column<DateOnly>(type: "date", nullable: true),
                    CheckedThroughB = table.Column<DateOnly>(type: "date", nullable: true),
                    CheckedThroughC = table.Column<DateOnly>(type: "date", nullable: true),
                    RunRequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastAttemptUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSuccessUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastErrorCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRateSync", x => x.SingletonKey);
                    table.CheckConstraint("CK_ExchangeRateSync_HistoryStart", "[HistoryStartDate] IS NULL OR [HistoryStartDate] >= '2002-01-02'");
                    table.CheckConstraint("CK_ExchangeRateSync_Singleton", "[SingletonKey] = 1");
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                schema: "ref",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
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
                    table.CheckConstraint("CK_ExchangeRates_Values", "[Source]='NBP' AND (([TableType] IN ('A','B') AND [Mid]>0 AND [Mid] IS NOT NULL AND [Bid] IS NULL AND [Ask] IS NULL AND [TradingDate] IS NULL) OR ([TableType]='C' AND [Mid] IS NULL AND [Bid]>0 AND [Ask]>0 AND [Bid] IS NOT NULL AND [Ask] IS NOT NULL AND [Bid]<=[Ask] AND ([TradingDate] IS NULL OR [TradingDate]<=[EffectiveDate])))");
                    table.ForeignKey(
                        name: "FK_ExchangeRates_Currencies_CurrencyCode",
                        column: x => x.CurrencyCode,
                        principalSchema: "ref",
                        principalTable: "Currencies",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRateConflicts",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RateId = table.Column<long>(type: "bigint", nullable: false),
                    IncomingHash = table.Column<string>(type: "char(64)", unicode: false, nullable: false),
                    IncomingTableNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IncomingTradingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IncomingMid = table.Column<decimal>(type: "decimal(24,12)", precision: 24, scale: 12, nullable: true),
                    IncomingBid = table.Column<decimal>(type: "decimal(24,12)", precision: 24, scale: 12, nullable: true),
                    IncomingAsk = table.Column<decimal>(type: "decimal(24,12)", precision: 24, scale: 12, nullable: true),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Decision = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ResolvedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRateConflicts", x => x.Id);
                    table.CheckConstraint("CK_ExchangeRateConflicts_Decision", "([ResolvedAtUtc] IS NULL AND [Decision] IS NULL) OR ([ResolvedAtUtc] IS NOT NULL AND [Decision] IN (N'AcceptIncoming', N'KeepExisting'))");
                    table.ForeignKey(
                        name: "FK_ExchangeRateConflicts_ExchangeRates_RateId",
                        column: x => x.RateId,
                        principalSchema: "ref",
                        principalTable: "ExchangeRates",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "ref",
                table: "Currencies",
                columns: new[] { "Code", "Name" },
                values: new object[,]
                {
                    { "AED", "dirham Zjednoczonych Emiratów Arabskich" },
                    { "AFN", "afgani afgańskie" },
                    { "ALL", "lek albański" },
                    { "AMD", "dram armeński" },
                    { "AOA", "kwanza angolska" },
                    { "ARS", "peso argentyńskie" },
                    { "AUD", "dolar australijski" },
                    { "AWG", "florin arubański" },
                    { "AZN", "manat azerbejdżański" },
                    { "BAM", "marka zamienna Bośni i Hercegowiny" },
                    { "BBD", "dolar barbadoski" },
                    { "BDT", "taka bengalska" },
                    { "BHD", "dinar bahrajski" },
                    { "BIF", "frank burundyjski" },
                    { "BND", "dolar brunejski" },
                    { "BOB", "boliwiano boliwijskie" },
                    { "BRL", "real brazylijski" },
                    { "BSD", "dolar bahamski" },
                    { "BWP", "pula botswańska" },
                    { "BYN", "rubel białoruski" },
                    { "BZD", "dolar belizeński" },
                    { "CAD", "dolar kanadyjski" },
                    { "CDF", "frank kongijski" },
                    { "CHF", "frank szwajcarski" },
                    { "CLP", "peso chilijskie" },
                    { "CNY", "juan chiński" },
                    { "COP", "peso kolumbijskie" },
                    { "CRC", "colon kostarykański" },
                    { "CUP", "peso kubańskie" },
                    { "CVE", "escudo Zielonego Przylądka" },
                    { "CZK", "korona czeska" },
                    { "DJF", "frank Dżibuti" },
                    { "DKK", "korona duńska" },
                    { "DOP", "peso dominikańskie" },
                    { "DZD", "dinar algierski" },
                    { "EGP", "funt egipski" },
                    { "ERN", "nakfa erytrejska" },
                    { "ETB", "birr etiopski" },
                    { "EUR", "euro" },
                    { "FJD", "dolar Fidżi" },
                    { "GBP", "funt szterling" },
                    { "GEL", "lari gruzińskie" },
                    { "GHS", "cedi ghański" },
                    { "GIP", "funt gibraltarski" },
                    { "GMD", "dalasi gambijskie" },
                    { "GNF", "frank gwinejski" },
                    { "GTQ", "quetzal gwatemalski" },
                    { "GYD", "dolar gujański" },
                    { "HKD", "dolar Hongkongu" },
                    { "HNL", "lempira honduraska" },
                    { "HTG", "gourde haitańskie" },
                    { "HUF", "forint węgierski" },
                    { "IDR", "rupia indonezyjska" },
                    { "ILS", "szekel izraelski" },
                    { "INR", "rupia indyjska" },
                    { "IQD", "dinar iracki" },
                    { "IRR", "rial irański" },
                    { "ISK", "korona islandzka" },
                    { "JMD", "dolar jamajski" },
                    { "JOD", "dinar jordański" },
                    { "JPY", "jen japoński" },
                    { "KES", "szyling kenijski" },
                    { "KGS", "som kirgiski" },
                    { "KHR", "riel kambodżański" },
                    { "KMF", "frank Komorów" },
                    { "KRW", "won południowokoreański" },
                    { "KWD", "dinar kuwejcki" },
                    { "KZT", "tenge kazachskie" },
                    { "LAK", "kip laotański" },
                    { "LBP", "funt libański" },
                    { "LKR", "rupia lankijska" },
                    { "LRD", "dolar liberyjski" },
                    { "LSL", "loti Lesotho" },
                    { "LYD", "dinar libijski" },
                    { "MAD", "dirham marokański" },
                    { "MDL", "lej mołdawski" },
                    { "MGA", "ariary malgaski" },
                    { "MKD", "denar macedoński" },
                    { "MMK", "kiat birmański" },
                    { "MNT", "tugrik mongolski" },
                    { "MOP", "pataca Makau" },
                    { "MRU", "ouguiya mauretańska" },
                    { "MUR", "rupia Mauritiusu" },
                    { "MVR", "rupia malediwska" },
                    { "MWK", "kwacha malawijska" },
                    { "MXN", "peso meksykańskie" },
                    { "MYR", "ringgit malezyjski" },
                    { "MZN", "metical mozambicki" },
                    { "NAD", "dolar namibijski" },
                    { "NGN", "naira nigeryjska" },
                    { "NIO", "cordoba nikaraguańska" },
                    { "NOK", "korona norweska" },
                    { "NPR", "rupia nepalska" },
                    { "NZD", "dolar nowozelandzki" },
                    { "OMR", "rial omański" },
                    { "PAB", "balboa panamska" },
                    { "PEN", "sol peruwiański" },
                    { "PGK", "kina papuaska" },
                    { "PHP", "peso filipińskie" },
                    { "PKR", "rupia pakistańska" },
                    { "PLN", "złoty polski" },
                    { "PYG", "guarani paragwajskie" },
                    { "QAR", "rial katarski" },
                    { "RON", "lej rumuński" },
                    { "RSD", "dinar serbski" },
                    { "RUB", "rubel rosyjski" },
                    { "RWF", "frank rwandyjski" },
                    { "SAR", "rial saudyjski" },
                    { "SBD", "dolar Wysp Salomona" },
                    { "SCR", "rupia seszelska" },
                    { "SDG", "funt sudański" },
                    { "SEK", "korona szwedzka" },
                    { "SGD", "dolar singapurski" },
                    { "SLE", "leone Sierra Leone" },
                    { "SOS", "szyling somalijski" },
                    { "SRD", "dolar surinamski" },
                    { "SSP", "funt południowosudański" },
                    { "STN", "dobra Wysp Świętego Tomasza i Książęcej" },
                    { "SVC", "colon salwadorski" },
                    { "SYP", "funt syryjski" },
                    { "SZL", "lilangeni Eswatini" },
                    { "THB", "bat tajlandzki" },
                    { "TJS", "somoni tadżyckie" },
                    { "TMT", "manat turkmeński" },
                    { "TND", "dinar tunezyjski" },
                    { "TOP", "paʻanga tongijska" },
                    { "TRY", "lira turecka" },
                    { "TTD", "dolar Trynidadu i Tobago" },
                    { "TWD", "dolar tajwański" },
                    { "TZS", "szyling tanzański" },
                    { "UAH", "hrywna ukraińska" },
                    { "UGX", "szyling ugandyjski" },
                    { "USD", "dolar amerykański" },
                    { "UYU", "peso urugwajskie" },
                    { "UZS", "sum uzbecki" },
                    { "VES", "boliwar wenezuelski" },
                    { "VND", "dong wietnamski" },
                    { "VUV", "vatu Vanuatu" },
                    { "WST", "tala samoańska" },
                    { "XAF", "frank CFA BEAC" },
                    { "XCD", "dolar wschodniokaraibski" },
                    { "XCG", "gulden karaibski" },
                    { "XDR", "SDR — jednostka rozrachunkowa MFW" },
                    { "XOF", "frank CFA BCEAO" },
                    { "XPF", "frank CFP" },
                    { "YER", "rial jemeński" },
                    { "ZAR", "rand południowoafrykański" },
                    { "ZMW", "kwacha zambijska" },
                    { "ZWG", "złoto Zimbabwe" }
                });

            migrationBuilder.InsertData(
                schema: "ops",
                table: "ExchangeRateSync",
                columns: new[] { "SingletonKey", "CheckedThroughA", "CheckedThroughB", "CheckedThroughC", "HistoryStartDate", "LastAttemptUtc", "LastErrorCode", "LastSuccessUtc", "RunRequestedAtUtc" },
                values: new object[] { (byte)1, null, null, null, null, null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRateConflicts_RateId_IncomingHash",
                schema: "ops",
                table: "ExchangeRateConflicts",
                columns: new[] { "RateId", "IncomingHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRateConflicts_ResolvedAtUtc",
                schema: "ops",
                table: "ExchangeRateConflicts",
                column: "ResolvedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRateOperations_AtUtc",
                schema: "audit",
                table: "ExchangeRateOperations",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_CurrencyCode_Source_TableType_EffectiveDate",
                schema: "ref",
                table: "ExchangeRates",
                columns: new[] { "CurrencyCode", "Source", "TableType", "EffectiveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_EffectiveDate",
                schema: "ref",
                table: "ExchangeRates",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_TableType_EffectiveDate",
                schema: "ref",
                table: "ExchangeRates",
                columns: new[] { "TableType", "EffectiveDate" });

            // ADR-048: niezmienna historia pobierania kursów i decyzji administratora.
            migrationBuilder.Sql("CREATE TRIGGER [audit].[TR_ExchangeRateOperations_Immutable] ON [audit].[ExchangeRateOperations] INSTEAD OF UPDATE, DELETE AS THROW 51000, 'Exchange rate history is immutable', 1;");
            // Kod ISO jest niezmiennym identyfikatorem waluty (jak w bazie firmy, ADR-024).
            migrationBuilder.Sql("""
                CREATE TRIGGER ref.TR_Currencies_CodeImmutable
                ON ref.Currencies AFTER UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF UPDATE(Code)
                        THROW 51004, N'Kod ISO jest niezmiennym identyfikatorem waluty.', 1;
                END;
                """);
            // Wycofane prawo firmy ImportExchangeRates (256): kursy pobiera usługa Operations.
            migrationBuilder.Sql("UPDATE security.CompanyAccess SET Permissions = Permissions & ~256 WHERE Permissions & 256 <> 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExchangeRateConflicts",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "ExchangeRateOperations",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "ExchangeRateSync",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "ExchangeRates",
                schema: "ref");

            migrationBuilder.DropTable(
                name: "Currencies",
                schema: "ref");
        }
    }
}
