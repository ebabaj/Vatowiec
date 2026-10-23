using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vatowiec.Core.Infrastructure.Persistence.Migrations.Company
{
    /// <inheritdoc />
    public partial class OptionalNbpTradingDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ExchangeRates_Values",
                schema: "core",
                table: "ExchangeRates");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExchangeRates_Values",
                schema: "core",
                table: "ExchangeRates",
                sql: "[Source]='NBP' AND (([TableType] IN ('A','B') AND [Mid]>0 AND [Mid] IS NOT NULL AND [Bid] IS NULL AND [Ask] IS NULL AND [TradingDate] IS NULL) OR ([TableType]='C' AND [Mid] IS NULL AND [Bid]>0 AND [Ask]>0 AND [Bid] IS NOT NULL AND [Ask] IS NOT NULL AND [Bid]<=[Ask] AND ([TradingDate] IS NULL OR [TradingDate]<=[EffectiveDate])))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ExchangeRates_Values",
                schema: "core",
                table: "ExchangeRates");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExchangeRates_Values",
                schema: "core",
                table: "ExchangeRates",
                sql: "[Source]='NBP' AND (([TableType] IN ('A','B') AND [Mid]>0 AND [Mid] IS NOT NULL AND [Bid] IS NULL AND [Ask] IS NULL AND [TradingDate] IS NULL) OR ([TableType]='C' AND [Mid] IS NULL AND [Bid]>0 AND [Ask]>0 AND [Bid] IS NOT NULL AND [Ask] IS NOT NULL AND [Bid]<=[Ask] AND [TradingDate] IS NOT NULL AND [TradingDate]<=[EffectiveDate]))");
        }
    }
}
