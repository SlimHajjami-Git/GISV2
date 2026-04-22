using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the <c>last_battery_alert_at</c> column to <c>gps_devices</c>.
///
/// <para><b>Why</b>: <c>BatteryMonitoringService</c> runs every 5 minutes
/// and detects NEMS L boîtiers whose <c>power_voltage</c> has dropped
/// below the threshold. Without a per-device cooldown stamp the alert
/// would fire on every cycle for as long as the battery stays weak —
/// 288 notifications per admin per day per dying battery. We stamp this
/// column at alert time and skip any device that was already flagged
/// in the last 24 hours.</para>
///
/// <para>Idempotent (<c>ADD COLUMN IF NOT EXISTS</c>) so a re-run on prod
/// doesn't error if the column was hot-patched through kubectl exec.</para>
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260422120000_AddBatteryAlertCooldown")]
public partial class AddBatteryAlertCooldown : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE gps_devices
    ADD COLUMN IF NOT EXISTS last_battery_alert_at TIMESTAMP WITH TIME ZONE NULL;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE gps_devices
    DROP COLUMN IF EXISTS last_battery_alert_at;
");
    }
}
