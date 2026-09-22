using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBotEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixEmergencyKillSwitchActivatedAtType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "RiskSettings"
                ALTER COLUMN "EmergencyKillSwitchActivatedAt" DROP DEFAULT;

                ALTER TABLE "RiskSettings"
                ALTER COLUMN "EmergencyKillSwitchActivatedAt" DROP NOT NULL;

                ALTER TABLE "RiskSettings"
                ALTER COLUMN "EmergencyKillSwitchActivatedAt"
                TYPE timestamp with time zone
                USING (
                    CASE
                        WHEN "EmergencyKillSwitchActivatedAt" = TRUE
                            THEN CURRENT_TIMESTAMP
                        ELSE NULL
                    END
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "RiskSettings"
                ALTER COLUMN "EmergencyKillSwitchActivatedAt"
                TYPE boolean
                USING ("EmergencyKillSwitchActivatedAt" IS NOT NULL);

                ALTER TABLE "RiskSettings"
                ALTER COLUMN "EmergencyKillSwitchActivatedAt"
                SET DEFAULT FALSE;

                ALTER TABLE "RiskSettings"
                ALTER COLUMN "EmergencyKillSwitchActivatedAt"
                SET NOT NULL;
                """);
        }
    }
}