using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBotEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskEquityHighWaterMark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EquityHighWaterMark",
                table: "RiskSettings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "EquityHighWaterMarkUpdatedAt",
                table: "RiskSettings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EquityHighWaterMark",
                table: "RiskSettings");

            migrationBuilder.DropColumn(
                name: "EquityHighWaterMarkUpdatedAt",
                table: "RiskSettings");
        }
    }
}
