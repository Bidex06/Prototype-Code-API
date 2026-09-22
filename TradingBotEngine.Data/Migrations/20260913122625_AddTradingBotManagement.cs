using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TradingBotEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTradingBotManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TradingBots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Strategy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Timeframe = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BrokerConnectionId = table.Column<int>(type: "integer", nullable: true),
                    UseFutures = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsRunning = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastStoppedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingBots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradingBots_BrokerConnections_BrokerConnectionId",
                        column: x => x.BrokerConnectionId,
                        principalTable: "BrokerConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TradingBots_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradingBotTrackedSymbols",
                columns: table => new
                {
                    TradingBotId = table.Column<int>(type: "integer", nullable: false),
                    TrackedSymbolId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingBotTrackedSymbols", x => new { x.TradingBotId, x.TrackedSymbolId });
                    table.ForeignKey(
                        name: "FK_TradingBotTrackedSymbols_TrackedSymbols_TrackedSymbolId",
                        column: x => x.TrackedSymbolId,
                        principalTable: "TrackedSymbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TradingBotTrackedSymbols_TradingBots_TradingBotId",
                        column: x => x.TradingBotId,
                        principalTable: "TradingBots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TradingBots_BrokerConnectionId",
                table: "TradingBots",
                column: "BrokerConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_TradingBots_UserId_IsEnabled_IsRunning",
                table: "TradingBots",
                columns: new[] { "UserId", "IsEnabled", "IsRunning" });

            migrationBuilder.CreateIndex(
                name: "IX_TradingBots_UserId_Name",
                table: "TradingBots",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradingBotTrackedSymbols_TrackedSymbolId",
                table: "TradingBotTrackedSymbols",
                column: "TrackedSymbolId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradingBotTrackedSymbols");

            migrationBuilder.DropTable(
                name: "TradingBots");
        }
    }
}
