using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBotEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUnifiedProtectedExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExchangeOrders_TradeId",
                table: "ExchangeOrders");

            migrationBuilder.AddColumn<string>(
                name: "ProtectionStatus",
                table: "Trades",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
               defaultValue: "NotRequired");

            migrationBuilder.AddColumn<string>(
                name: "StopLossOrderId",
                table: "Trades",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TakeProfitOrderId",
                table: "Trades",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderRole",
                table: "ExchangeOrders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ParentExchangeOrderId",
                table: "ExchangeOrders",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeOrders_ParentExchangeOrderId",
                table: "ExchangeOrders",
                column: "ParentExchangeOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeOrders_TradeId_OrderRole",
                table: "ExchangeOrders",
                columns: new[] { "TradeId", "OrderRole" });

            migrationBuilder.AddForeignKey(
                name: "FK_ExchangeOrders_ExchangeOrders_ParentExchangeOrderId",
                table: "ExchangeOrders",
                column: "ParentExchangeOrderId",
                principalTable: "ExchangeOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExchangeOrders_ExchangeOrders_ParentExchangeOrderId",
                table: "ExchangeOrders");

            migrationBuilder.DropIndex(
                name: "IX_ExchangeOrders_ParentExchangeOrderId",
                table: "ExchangeOrders");

            migrationBuilder.DropIndex(
                name: "IX_ExchangeOrders_TradeId_OrderRole",
                table: "ExchangeOrders");

            migrationBuilder.DropColumn(
                name: "ProtectionStatus",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "StopLossOrderId",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "TakeProfitOrderId",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "OrderRole",
                table: "ExchangeOrders");

            migrationBuilder.DropColumn(
                name: "ParentExchangeOrderId",
                table: "ExchangeOrders");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeOrders_TradeId",
                table: "ExchangeOrders",
                column: "TradeId");
        }
    }
}
