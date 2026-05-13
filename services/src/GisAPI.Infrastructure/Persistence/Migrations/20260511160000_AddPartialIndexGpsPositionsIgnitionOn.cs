using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds a partial index on <c>gps_positions(device_id, recorded_at DESC)</c>
/// restricted to <c>ignition_on = true</c>. Used exclusively by the
/// "engine off since" lookup in <c>GetVehiclesWithPositionsQueryHandler</c>,
/// which needs the most recent ignition-on frame per device over a 30-day
/// window.
///
/// <para><b>Why partial?</b> Most rows in <c>gps_positions</c> have
/// <c>ignition_on = false</c> (idle / parked). A partial index that only
/// indexes the rows we actually want to find keeps the index tiny
/// (~10–30 % of full size) and lets Postgres satisfy the
/// <c>SELECT DISTINCT ON (device_id) ... ORDER BY device_id, recorded_at DESC</c>
/// pattern via a single index scan per device — instead of scanning
/// 30 days of frames per device and filtering in memory.</para>
///
/// <para>Idempotent: <c>CREATE INDEX IF NOT EXISTS</c> with the
/// <c>CONCURRENTLY</c> hint dropped because EF migrations can't run
/// inside a transaction with CONCURRENTLY. On a large prod table the
/// initial creation will lock briefly — acceptable during a deploy.</para>
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260511160000_AddPartialIndexGpsPositionsIgnitionOn")]
public partial class AddPartialIndexGpsPositionsIgnitionOn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ix_gps_positions_ignition_on_recent
    ON gps_positions (device_id, recorded_at DESC)
    WHERE ignition_on = true;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ix_gps_positions_ignition_on_recent;
");
    }
}
