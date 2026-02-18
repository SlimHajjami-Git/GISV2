using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Optimize GPS playback query performance:
/// 1. Add ASC covering index for history queries (index-only scan, no heap lookups)
/// 2. Drop redundant DESC covering index (replaced by the ASC one)
/// 3. Add pg_trgm extension and partial indexes for common query patterns
/// </summary>
public partial class OptimizeGpsPlaybackPerformance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Add ASC covering index that matches the playback query direction exactly:
        //    WHERE device_id = X AND recorded_at >= from AND recorded_at <= to ORDER BY recorded_at ASC
        //    INCLUDE covers all columns in the SELECT projection → index-only scan
        migrationBuilder.Sql(@"
            CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_gps_positions_device_recorded_asc_cover
            ON gps_positions (device_id, recorded_at ASC)
            INCLUDE (latitude, longitude, speed_kph, course_deg, ignition_on, 
                     fuel_raw, temperature_c, address, odometer_km, is_real_time, created_at);
        ");

        // 2. Drop the old DESC covering index (now redundant - the ASC one handles both directions)
        migrationBuilder.Sql(@"
            DROP INDEX IF EXISTS idx_gps_positions_device_recorded_desc;
        ");

        // 3. Drop redundant single-column device_id index (covered by composite indexes)
        migrationBuilder.Sql(@"
            DROP INDEX IF EXISTS ""IX_gps_positions_device_id"";
        ");

        // 4. Update table statistics for query planner
        migrationBuilder.Sql(@"
            ANALYZE gps_positions;
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restore dropped indexes
        migrationBuilder.Sql(@"
            CREATE INDEX IF NOT EXISTS ""IX_gps_positions_device_id"" ON gps_positions (device_id);
        ");

        migrationBuilder.Sql(@"
            CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_gps_positions_device_recorded_desc
            ON gps_positions (device_id, recorded_at DESC)
            INCLUDE (latitude, longitude, speed_kph, course_deg, ignition_on, 
                     fuel_raw, temperature_c, address, odometer_km);
        ");

        // Drop the new ASC covering index
        migrationBuilder.Sql(@"
            DROP INDEX IF EXISTS idx_gps_positions_device_recorded_asc_cover;
        ");
    }
}
