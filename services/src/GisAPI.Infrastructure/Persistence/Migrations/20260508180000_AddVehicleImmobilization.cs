using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds a vehicle-level immobilisation flag so the operator can mute
/// every automatic alert service on a vehicle that is knowingly out of
/// service (mechanic intervention, long-term parking, boîtier removed
/// for maintenance…).
///
/// <para><b>Why</b>: when a vehicle is on a tow-truck or sitting in a
/// garage with the boîtier disconnected, the GPS device produces garbage
/// — wild MEMS readings that look like accidents, fake "speed > 200 km/h"
/// when on a flatbed, no GSM signal at all. Today the operator has to
/// physically delete the vehicle from the system to stop the noise, then
/// re-create it when service resumes. With this flag they just tick a
/// box and untick it later.</para>
///
/// <para>Idempotent (<c>ADD COLUMN IF NOT EXISTS</c>) so the migration
/// can replay on a partially-migrated environment without erroring.</para>
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260508180000_AddVehicleImmobilization")]
public partial class AddVehicleImmobilization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE vehicles
    ADD COLUMN IF NOT EXISTS is_immobilized                    BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS immobilization_reason             VARCHAR(200) NULL,
    ADD COLUMN IF NOT EXISTS immobilization_started_at         TIMESTAMP WITH TIME ZONE NULL,
    ADD COLUMN IF NOT EXISTS immobilization_started_by_user_id INTEGER NULL;

CREATE INDEX IF NOT EXISTS idx_vehicles_is_immobilized
    ON vehicles (is_immobilized)
    WHERE is_immobilized = TRUE;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DROP INDEX IF EXISTS idx_vehicles_is_immobilized;

ALTER TABLE vehicles
    DROP COLUMN IF EXISTS immobilization_started_by_user_id,
    DROP COLUMN IF EXISTS immobilization_started_at,
    DROP COLUMN IF EXISTS immobilization_reason,
    DROP COLUMN IF EXISTS is_immobilized;
");
    }
}
