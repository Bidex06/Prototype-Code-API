using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TradingBotEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompletePhase0DatabaseIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Signals_Trades_TradeId",
                table: "Signals");

            migrationBuilder.DropIndex(
                name: "IX_Trades_UserId",
                table: "Trades");

            migrationBuilder.DropIndex(
                name: "IX_Signals_UserId",
                table: "Signals");

            migrationBuilder.DropIndex(
                name: "IX_Positions_UserId",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Payments_UserId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "RefreshToken",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RefreshTokenExpiry",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SubscriptionId",
                table: "Users");

            migrationBuilder.AddColumn<bool>(
                name: "IsAutoTradeEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Trades",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsTestnet",
                table: "BrokerConnections",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AddColumn<bool>(
                name: "IsLiveTradingEnabled",
                table: "BrokerConnections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LiveTradingOptInAt",
                table: "BrokerConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackedSymbols",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Exchange = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedSymbols", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedSymbols_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_IsActive_IsAutoTradeEnabled",
                table: "Users",
                columns: new[] { "IsActive", "IsAutoTradeEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_UserId_IdempotencyKey",
                table: "Trades",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_UserId_Status_EntryTime",
                table: "Trades",
                columns: new[] { "UserId", "Status", "EntryTime" });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_UserId_Symbol_OrderId",
                table: "Trades",
                columns: new[] { "UserId", "Symbol", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_IsActive_EndDate",
                table: "Subscriptions",
                columns: new[] { "IsActive", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Signals_UserId_Symbol_GeneratedAt",
                table: "Signals",
                columns: new[] { "UserId", "Symbol", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Positions_UserId_Status",
                table: "Positions",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Gateway_TransactionId",
                table: "Payments",
                columns: new[] { "Gateway", "TransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UserId_CreatedAt",
                table: "Payments",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BrokerConnections_UserId_IsActive",
                table: "BrokerConnections",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_ExpiresAt",
                table: "RefreshTokens",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedSymbols_UserId_Exchange_Symbol",
                table: "TrackedSymbols",
                columns: new[] { "UserId", "Exchange", "Symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedSymbols_UserId_IsEnabled",
                table: "TrackedSymbols",
                columns: new[] { "UserId", "IsEnabled" });

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Signals_Trades_TradeId",
                table: "Signals",
                column: "TradeId",
                principalTable: "Trades",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Signals_Trades_TradeId",
                table: "Signals");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "TrackedSymbols");

            migrationBuilder.DropIndex(
                name: "IX_Users_IsActive_IsAutoTradeEnabled",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Trades_UserId_IdempotencyKey",
                table: "Trades");

            migrationBuilder.DropIndex(
                name: "IX_Trades_UserId_Status_EntryTime",
                table: "Trades");

            migrationBuilder.DropIndex(
                name: "IX_Trades_UserId_Symbol_OrderId",
                table: "Trades");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_IsActive_EndDate",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Signals_UserId_Symbol_GeneratedAt",
                table: "Signals");

            migrationBuilder.DropIndex(
                name: "IX_Positions_UserId_Status",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Payments_Gateway_TransactionId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_UserId_CreatedAt",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_BrokerConnections_UserId_IsActive",
                table: "BrokerConnections");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserId_CreatedAt",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "IsAutoTradeEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "IsLiveTradingEnabled",
                table: "BrokerConnections");

            migrationBuilder.DropColumn(
                name: "LiveTradingOptInAt",
                table: "BrokerConnections");

            migrationBuilder.AddColumn<string>(
                name: "RefreshToken",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefreshTokenExpiry",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsTestnet",
                table: "BrokerConnections",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trades_UserId",
                table: "Trades",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Signals_UserId",
                table: "Signals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_UserId",
                table: "Positions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UserId",
                table: "Payments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Signals_Trades_TradeId",
                table: "Signals",
                column: "TradeId",
                principalTable: "Trades",
                principalColumn: "Id");
        }
    }
}
