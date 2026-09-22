using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBotEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmergencyKillSwitch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmergencyKillSwitchActivatedAt",
                table: "RiskSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsEmergencyKillSwitch",
                table: "RiskSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmergencyKillSwitchActivatedAt",
                table: "RiskSettings");

            migrationBuilder.DropColumn(
                name: "IsEmergencyKillSwitch",
                table: "RiskSettings");
        }
    }
}
