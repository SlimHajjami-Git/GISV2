using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Two new cooldown columns on <c>gps_devices</c> for the post-Calypso-8
/// battery-health watchers:
///
/// <list type="bullet">
///   <item><description><c>last_voltage_health_alert_at</c> — stamped by
///     <c>VoltageHealthMonitoringService</c> when it detects a sustained
///     resting-voltage decline, an anomalous charging voltage, or a
///     saturated-firmware silence pattern. Same 48h-cooldown semantics as
///     <c>last_battery_alert_at</c>: keeps a single failing battery from
///     firing the bell every cycle.</description></item>
///   <item><description><c>last_offline_alert_at</c> — stamped by
///     <c>OfflineDeviceMonitoringService</c> when a device has been silent
///     past the 90-min threshold. The existing <c>PowerCutNotificationHandler</c>
///     fans out the notification, but it expects a one-shot trigger from
///     the Rust ingest's HH02/AA02 reset frame; we need our own cooldown
///     stamp because we proactively trigger from <c>last_communication</c>
///     rather than waiting for reconnection.</description></item>
/// </list>
///
/// <para>An index on <c>last_communication</c> is added to make the
/// silence scan a single indexed range read — without it the offline
/// watcher would full-scan <c>gps_devices</c> every 10 minutes (cheap on
/// a 200-row table, but still: it's one BTREE node away).</para>
///
/// <para>Idempotent (<c>ADD COLUMN IF NOT EXISTS</c> / <c>CREATE INDEX
/// IF NOT EXISTS</c>) so a re-run on prod doesn't error if the columns
/// were hot-patched through kubectl exec, and so the migration can land
/// safely on a partially-migrated environment.</para>
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260507200000_AddBatteryHealthAlertCooldowns")]
public partial class AddBatteryHealthAlertCooldowns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE gps_devices
    ADD COLUMN IF NOT EXISTS last_voltage_health_alert_at TIMESTAMP WITH TIME ZONE NULL,
    ADD COLUMN IF NOT EXISTS last_offline_alert_at        TIMESTAMP WITH TIME ZONE NULL;

CREATE INDEX IF NOT EXISTS ix_gps_devices_last_communication
    ON gps_devices (last_communication);
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ix_gps_devices_last_communication;

ALTER TABLE gps_devices
    DROP COLUMN IF EXISTS last_offline_alert_at,
    DROP COLUMN IF EXISTS last_voltage_health_alert_at;
");
    }
}
