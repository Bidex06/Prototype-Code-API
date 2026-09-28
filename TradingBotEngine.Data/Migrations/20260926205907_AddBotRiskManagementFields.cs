using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBotEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBotRiskManagementFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoTradeEnabled",
                table: "TradingBots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StopLossMode",
                table: "TradingBots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "StopLossPercent",
                table: "TradingBots",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TakeProfitMode",
                table: "TradingBots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TakeProfitPercent",
                table: "TradingBots",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoTradeEnabled",
                table: "TradingBots");

            migrationBuilder.DropColumn(
                name: "StopLossMode",
                table: "TradingBots");

            migrationBuilder.DropColumn(
                name: "StopLossPercent",
                table: "TradingBots");

            migrationBuilder.DropColumn(
                name: "TakeProfitMode",
                table: "TradingBots");

            migrationBuilder.DropColumn(
                name: "TakeProfitPercent",
                table: "TradingBots");
        }
    }
}
