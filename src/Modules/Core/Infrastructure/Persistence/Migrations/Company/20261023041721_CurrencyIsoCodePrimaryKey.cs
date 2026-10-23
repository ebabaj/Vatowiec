using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1814
namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.Company;

// Demo-only reset: no copying, identifier mapping or changes to other modules.
public partial class CurrencyIsoCodePrimaryKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ExchangeRates", schema: "core");
        migrationBuilder.DropTable(name: "CurrencyOperations", schema: "audit");
        migrationBuilder.DropTable(name: "Currencies", schema: "ref");

        migrationBuilder.CreateTable(
            name: "Currencies",
            schema: "ref",
            columns: table => new
            {
                Code = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                DownloadA = table.Column<bool>(type: "bit", nullable: false),
                DownloadB = table.Column<bool>(type: "bit", nullable: false),
                DownloadC = table.Column<bool>(type: "bit", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Currencies", x => x.Code);
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

        migrationBuilder.InsertData(
            schema: "ref",
            table: "Currencies",
            columns: new[] { "Code", "DownloadA", "DownloadB", "DownloadC", "IsActive", "Name" },
            values: new object[,]
            {
                    { "PLN", false, false, false, true, "złoty polski" },
                    { "THB", false, false, false, true, "bat tajlandzki" },
                    { "USD", true, false, false, true, "dolar amerykański" },
                    { "AUD", false, false, false, true, "dolar australijski" },
                    { "HKD", false, false, false, true, "dolar Hongkongu" },
                    { "CAD", false, false, false, true, "dolar kanadyjski" },
                    { "NZD", false, false, false, true, "dolar nowozelandzki" },
                    { "SGD", false, false, false, true, "dolar singapurski" },
                    { "EUR", true, false, false, true, "euro" },
                    { "HUF", false, false, false, true, "forint węgierski" },
                    { "CHF", true, false, false, true, "frank szwajcarski" },
                    { "GBP", true, false, false, true, "funt szterling" },
                    { "UAH", false, false, false, true, "hrywna ukraińska" },
                    { "JPY", false, false, false, true, "jen japoński" },
                    { "CZK", false, false, false, true, "korona czeska" },
                    { "DKK", false, false, false, true, "korona duńska" },
                    { "ISK", false, false, false, true, "korona islandzka" },
                    { "NOK", false, false, false, true, "korona norweska" },
                    { "SEK", false, false, false, true, "korona szwedzka" },
                    { "RON", false, false, false, true, "lej rumuński" },
                    { "TRY", false, false, false, true, "lira turecka" },
                    { "ILS", false, false, false, true, "szekel izraelski" },
                    { "CLP", false, false, false, true, "peso chilijskie" },
                    { "PHP", false, false, false, true, "peso filipińskie" },
                    { "MXN", false, false, false, true, "peso meksykańskie" },
                    { "ZAR", false, false, false, true, "rand południowoafrykański" },
                    { "BRL", false, false, false, true, "real brazylijski" },
                    { "MYR", false, false, false, true, "ringgit malezyjski" },
                    { "IDR", false, false, false, true, "rupia indonezyjska" },
                    { "INR", false, false, false, true, "rupia indyjska" },
                    { "KRW", false, false, false, true, "won południowokoreański" },
                    { "CNY", false, false, false, true, "juan chiński" },
                    { "XDR", false, false, false, true, "SDR — jednostka rozrachunkowa MFW" },
                    { "AFN", false, false, false, true, "afgani afgańskie" },
                    { "MGA", false, false, false, true, "ariary malgaski" },
                    { "PAB", false, false, false, true, "balboa panamska" },
                    { "ETB", false, false, false, true, "birr etiopski" },
                    { "VES", false, false, false, true, "boliwar wenezuelski" },
                    { "BOB", false, false, false, true, "boliwiano boliwijskie" },
                    { "CRC", false, false, false, true, "colon kostarykański" },
                    { "SVC", false, false, false, true, "colon salwadorski" },
                    { "NIO", false, false, false, true, "cordoba nikaraguańska" },
                    { "GMD", false, false, false, true, "dalasi gambijskie" },
                    { "MKD", false, false, false, true, "denar macedoński" },
                    { "DZD", false, false, false, true, "dinar algierski" },
                    { "BHD", false, false, false, true, "dinar bahrajski" },
                    { "IQD", false, false, false, true, "dinar iracki" },
                    { "JOD", false, false, false, true, "dinar jordański" },
                    { "KWD", false, false, false, true, "dinar kuwejcki" },
                    { "LYD", false, false, false, true, "dinar libijski" },
                    { "RSD", false, false, false, true, "dinar serbski" },
                    { "TND", false, false, false, true, "dinar tunezyjski" },
                    { "MAD", false, false, false, true, "dirham marokański" },
                    { "AED", false, false, false, true, "dirham Zjednoczonych Emiratów Arabskich" },
                    { "STN", false, false, false, true, "dobra Wysp Świętego Tomasza i Książęcej" },
                    { "BSD", false, false, false, true, "dolar bahamski" },
                    { "BBD", false, false, false, true, "dolar barbadoski" },
                    { "BZD", false, false, false, true, "dolar belizeński" },
                    { "BND", false, false, false, true, "dolar brunejski" },
                    { "FJD", false, false, false, true, "dolar Fidżi" },
                    { "GYD", false, false, false, true, "dolar gujański" },
                    { "JMD", false, false, false, true, "dolar jamajski" },
                    { "LRD", false, false, false, true, "dolar liberyjski" },
                    { "NAD", false, false, false, true, "dolar namibijski" },
                    { "SRD", false, false, false, true, "dolar surinamski" },
                    { "TTD", false, false, false, true, "dolar Trynidadu i Tobago" },
                    { "XCD", false, false, false, true, "dolar wschodniokaraibski" },
                    { "SBD", false, false, false, true, "dolar Wysp Salomona" },
                    { "VND", false, false, false, true, "dong wietnamski" },
                    { "AMD", false, false, false, true, "dram armeński" },
                    { "CVE", false, false, false, true, "escudo Zielonego Przylądka" },
                    { "AWG", false, false, false, true, "florin arubański" },
                    { "BIF", false, false, false, true, "frank burundyjski" },
                    { "XOF", false, false, false, true, "frank CFA BCEAO" },
                    { "XAF", false, false, false, true, "frank CFA BEAC" },
                    { "XPF", false, false, false, true, "frank CFP" },
                    { "DJF", false, false, false, true, "frank Dżibuti" },
                    { "GNF", false, false, false, true, "frank gwinejski" },
                    { "KMF", false, false, false, true, "frank Komorów" },
                    { "CDF", false, false, false, true, "frank kongijski" },
                    { "RWF", false, false, false, true, "frank rwandyjski" },
                    { "EGP", false, false, false, true, "funt egipski" },
                    { "GIP", false, false, false, true, "funt gibraltarski" },
                    { "LBP", false, false, false, true, "funt libański" },
                    { "SSP", false, false, false, true, "funt południowosudański" },
                    { "SDG", false, false, false, true, "funt sudański" },
                    { "SYP", false, false, false, true, "funt syryjski" },
                    { "GHS", false, false, false, true, "cedi ghański" },
                    { "HTG", false, false, false, true, "gourde haitańskie" },
                    { "PYG", false, false, false, true, "guarani paragwajskie" },
                    { "XCG", false, false, false, true, "gulden karaibski" },
                    { "PGK", false, false, false, true, "kina papuaska" },
                    { "LAK", false, false, false, true, "kip laotański" },
                    { "MWK", false, false, false, true, "kwacha malawijska" },
                    { "ZMW", false, false, false, true, "kwacha zambijska" },
                    { "AOA", false, false, false, true, "kwanza angolska" },
                    { "MMK", false, false, false, true, "kiat birmański" },
                    { "GEL", false, false, false, true, "lari gruzińskie" },
                    { "MDL", false, false, false, true, "lej mołdawski" },
                    { "ALL", false, false, false, true, "lek albański" },
                    { "HNL", false, false, false, true, "lempira honduraska" },
                    { "SLE", false, false, false, true, "leone Sierra Leone" },
                    { "SZL", false, false, false, true, "lilangeni Eswatini" },
                    { "LSL", false, false, false, true, "loti Lesotho" },
                    { "AZN", false, false, false, true, "manat azerbejdżański" },
                    { "MZN", false, false, false, true, "metical mozambicki" },
                    { "NGN", false, false, false, true, "naira nigeryjska" },
                    { "ERN", false, false, false, true, "nakfa erytrejska" },
                    { "TWD", false, false, false, true, "dolar tajwański" },
                    { "TMT", false, false, false, true, "manat turkmeński" },
                    { "MRU", false, false, false, true, "ouguiya mauretańska" },
                    { "TOP", false, false, false, true, "paʻanga tongijska" },
                    { "MOP", false, false, false, true, "pataca Makau" },
                    { "ARS", false, false, false, true, "peso argentyńskie" },
                    { "DOP", false, false, false, true, "peso dominikańskie" },
                    { "COP", false, false, false, true, "peso kolumbijskie" },
                    { "CUP", false, false, false, true, "peso kubańskie" },
                    { "UYU", false, false, false, true, "peso urugwajskie" },
                    { "BWP", false, false, false, true, "pula botswańska" },
                    { "GTQ", false, false, false, true, "quetzal gwatemalski" },
                    { "IRR", false, false, false, true, "rial irański" },
                    { "YER", false, false, false, true, "rial jemeński" },
                    { "QAR", false, false, false, true, "rial katarski" },
                    { "OMR", false, false, false, true, "rial omański" },
                    { "SAR", false, false, false, true, "rial saudyjski" },
                    { "KHR", false, false, false, true, "riel kambodżański" },
                    { "BYN", false, false, false, true, "rubel białoruski" },
                    { "RUB", false, false, false, true, "rubel rosyjski" },
                    { "LKR", false, false, false, true, "rupia lankijska" },
                    { "MVR", false, false, false, true, "rupia malediwska" },
                    { "MUR", false, false, false, true, "rupia Mauritiusu" },
                    { "NPR", false, false, false, true, "rupia nepalska" },
                    { "PKR", false, false, false, true, "rupia pakistańska" },
                    { "SCR", false, false, false, true, "rupia seszelska" },
                    { "PEN", false, false, false, true, "sol peruwiański" },
                    { "KGS", false, false, false, true, "som kirgiski" },
                    { "TJS", false, false, false, true, "somoni tadżyckie" },
                    { "UZS", false, false, false, true, "sum uzbecki" },
                    { "KES", false, false, false, true, "szyling kenijski" },
                    { "SOS", false, false, false, true, "szyling somalijski" },
                    { "TZS", false, false, false, true, "szyling tanzański" },
                    { "UGX", false, false, false, true, "szyling ugandyjski" },
                    { "BDT", false, false, false, true, "taka bengalska" },
                    { "WST", false, false, false, true, "tala samoańska" },
                    { "KZT", false, false, false, true, "tenge kazachskie" },
                    { "MNT", false, false, false, true, "tugrik mongolski" },
                    { "VUV", false, false, false, true, "vatu Vanuatu" },
                    { "BAM", false, false, false, true, "marka zamienna Bośni i Hercegowiny" },
                    { "ZWG", false, false, false, true, "złoto Zimbabwe" }
            });

        migrationBuilder.CreateIndex(
            name: "IX_CurrencyOperations_ActorId_ActorIssuerId_OperationId",
            schema: "audit",
            table: "CurrencyOperations",
            columns: new[] { "ActorId", "ActorIssuerId", "OperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ExchangeRates_CurrencyCode_Source_TableType_EffectiveDate",
            schema: "core",
            table: "ExchangeRates",
            columns: new[] { "CurrencyCode", "Source", "TableType", "EffectiveDate" },
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
        migrationBuilder.Sql("""
            CREATE TRIGGER ref.TR_Currencies_CodeImmutable
            ON ref.Currencies AFTER UPDATE AS
            BEGIN
                SET NOCOUNT ON;
                IF UPDATE(Code)
                    THROW 51004, N'Kod ISO jest niezmiennym identyfikatorem waluty.', 1;
            END;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Powrót do numerycznego klucza waluty nie jest obsługiwany. Użyj zweryfikowanej kopii demonstratora.");
}
