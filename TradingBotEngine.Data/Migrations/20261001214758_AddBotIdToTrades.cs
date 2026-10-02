using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBotEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBotIdToTrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BotId",
                table: "Trades",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BotId",
                table: "Trades");
        }
    }
}
