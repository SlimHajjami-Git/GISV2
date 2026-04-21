using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Creates the <c>accident_events</c> table that backs
/// <see cref="GisAPI.Domain.Entities.AccidentEvent"/>.
///
/// The table already exists on production (it was created out-of-band when
/// the retroactive <c>AccidentNotificationSeeder</c> shipped), but no
/// migration had formalised it yet. This migration therefore uses
/// <c>CREATE TABLE IF NOT EXISTS</c> / <c>CREATE INDEX IF NOT EXISTS</c> so
/// it is a no-op on prod and a fresh create everywhere else (dev, CI,
/// staging, new tenants). The schema mirrors
/// <see cref="GisAPI.Infrastructure.Persistence.Configurations.AccidentEventConfiguration"/>
/// verbatim.
///
/// This migration ships alongside the new server-side
/// <c>AccidentDetectionService</c>, which populates the table from the
/// real MEMS accelerometer stream (V7 filter).
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260421160000_AddAccidentEventsTable")]
public partial class AddAccidentEventsTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Idempotent create — safe on environments where the table was
        // manually bootstrapped by the earlier seeder.
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS accident_events (
    id                   SERIAL PRIMARY KEY,
    company_id           INTEGER NOT NULL,
    vehicle_id           INTEGER NULL,
    gps_device_id        INTEGER NULL,
    device_uid           VARCHAR(50) NOT NULL,
    incident_at          TIMESTAMP WITH TIME ZONE NOT NULL,
    latitude             DOUBLE PRECISION NOT NULL,
    longitude            DOUBLE PRECISION NOT NULL,
    reference_code       VARCHAR(100) NULL,
    vehicle_label        VARCHAR(100) NULL,
    location_commune     VARCHAR(100) NULL,
    location_governorate VARCHAR(100) NULL,
    location_road_type   VARCHAR(100) NULL,
    synthesis_text       TEXT NULL,
    confidence           INTEGER NOT NULL DEFAULT 90,
    story                JSONB NULL,
    reasons              JSONB NULL,
    indicators           JSONB NULL,
    created_at           TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at           TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
");

        // Indexes — same shape as AccidentEventConfiguration.
        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ix_accident_events_company_id
    ON accident_events (company_id);
CREATE INDEX IF NOT EXISTS ix_accident_events_vehicle_id
    ON accident_events (vehicle_id);
CREATE INDEX IF NOT EXISTS ix_accident_events_incident_at_desc
    ON accident_events (incident_at DESC);
");

        // Uniqueness guard used by AccidentDetectionService for idempotent
        // inserts: the same (vehicle, minute-bucket) never produces two rows
        // even if two scan cycles overlap on the same candidate frame.
        //
        // PostgreSQL forbids non-IMMUTABLE functions in index expressions.
        // Both `date_trunc(text, timestamptz)` and `EXTRACT(EPOCH FROM
        // timestamptz)` are marked STABLE in pg_proc (generic labeling; the
        // actual output is UTC-deterministic but the catalog is
        // conservative). We therefore wrap the bucket computation in our
        // own SQL function marked IMMUTABLE — the standard Postgres idiom
        // for indexing on expressions the catalog under-classifies.
        migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION accident_minute_bucket(ts timestamp with time zone)
RETURNS bigint
LANGUAGE sql
IMMUTABLE
STRICT
PARALLEL SAFE
AS $$
    SELECT (EXTRACT(EPOCH FROM ts)::bigint) / 60;
$$;
");

        migrationBuilder.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS ux_accident_events_vehicle_minute
    ON accident_events (vehicle_id, accident_minute_bucket(incident_at))
    WHERE vehicle_id IS NOT NULL;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS ux_accident_events_vehicle_minute;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS accident_minute_bucket(timestamp with time zone);");
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_accident_events_incident_at_desc;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_accident_events_vehicle_id;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_accident_events_company_id;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS accident_events;");
    }
}
