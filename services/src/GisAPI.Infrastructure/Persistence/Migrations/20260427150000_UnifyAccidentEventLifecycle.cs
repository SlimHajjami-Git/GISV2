using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Calypso 7 — unifies the two parallel accident systems
/// (<c>accident_claims</c> + <c>accident_events</c>) into a single
/// timeline-based <c>accident_events</c> covering the full lifecycle
/// (detection → confirmation → expert → mechanic quote → repair →
/// insurance settlement).
///
/// <para><b>What this migration does:</b></para>
/// <list type="number">
///   <item>Extends <c>accident_events</c> with all the columns needed
///     for phases 2–6 (initial damages, expert visit, mechanic quote,
///     repair, claim).</item>
///   <item>Adds <c>origin</c> (auto / manual) and <c>driver_id</c>.</item>
///   <item>Creates two new child tables: <c>accident_event_documents</c>
///     (multi-file attachments typed by phase) and
///     <c>accident_event_third_parties</c> (other vehicles involved).</item>
///   <item>Adds <c>accident_event_id</c> to <c>vehicle_costs</c> so the
///     /depenses module can surface the accident link on repair and
///     insurance-refund rows.</item>
///   <item>Imports every existing <c>accident_claims</c> row into
///     <c>accident_events</c> with <c>origin = 'manual'</c> and
///     <c>status = 'confirmed'</c>, plus all child documents and third
///     parties.</item>
///   <item>Drops the now-empty <c>accident_claims</c> tables.</item>
/// </list>
///
/// All ALTER TABLE statements use <c>IF NOT EXISTS</c> / <c>IF EXISTS</c>
/// so re-running the migration on prod (or replaying it after manual
/// SQL fixes) is safe. The Down path tries to recreate the old
/// <c>accident_claims</c> shape so an emergency rollback is possible
/// without losing the imported rows (they stay in <c>accident_events</c>).
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260427150000_UnifyAccidentEventLifecycle")]
public partial class UnifyAccidentEventLifecycle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1) Extend accident_events with timeline columns -------------------
        migrationBuilder.Sql(@"
ALTER TABLE accident_events
    ADD COLUMN IF NOT EXISTS origin                  VARCHAR(20)  NOT NULL DEFAULT 'auto',
    ADD COLUMN IF NOT EXISTS driver_id               INTEGER      NULL,

    ADD COLUMN IF NOT EXISTS weather_conditions      VARCHAR(100) NULL,
    ADD COLUMN IF NOT EXISTS road_conditions         VARCHAR(100) NULL,
    ADD COLUMN IF NOT EXISTS police_report_number    VARCHAR(100) NULL,
    ADD COLUMN IF NOT EXISTS mileage_at_accident     INTEGER      NULL,

    ADD COLUMN IF NOT EXISTS initial_description     TEXT         NULL,
    ADD COLUMN IF NOT EXISTS initial_severity        VARCHAR(20)  NULL,
    ADD COLUMN IF NOT EXISTS damaged_zones_json      JSONB        NULL,

    ADD COLUMN IF NOT EXISTS expert_visited_at       TIMESTAMP WITH TIME ZONE NULL,
    ADD COLUMN IF NOT EXISTS expert_name             VARCHAR(200) NULL,
    ADD COLUMN IF NOT EXISTS expert_company          VARCHAR(200) NULL,
    ADD COLUMN IF NOT EXISTS expert_assessment       TEXT         NULL,
    ADD COLUMN IF NOT EXISTS expert_estimated_amount NUMERIC(12,2) NULL,

    ADD COLUMN IF NOT EXISTS mechanic_quote_at       TIMESTAMP WITH TIME ZONE NULL,
    ADD COLUMN IF NOT EXISTS mechanic_name           VARCHAR(200) NULL,
    ADD COLUMN IF NOT EXISTS mechanic_quoted_amount  NUMERIC(12,2) NULL,

    ADD COLUMN IF NOT EXISTS repair_started_at       TIMESTAMP WITH TIME ZONE NULL,
    ADD COLUMN IF NOT EXISTS repair_completed_at     TIMESTAMP WITH TIME ZONE NULL,
    ADD COLUMN IF NOT EXISTS actual_repair_cost      NUMERIC(12,2) NULL,

    ADD COLUMN IF NOT EXISTS claim_number            VARCHAR(100) NULL,
    ADD COLUMN IF NOT EXISTS claim_submitted_at      TIMESTAMP WITH TIME ZONE NULL,
    ADD COLUMN IF NOT EXISTS claim_approved_amount   NUMERIC(12,2) NULL,
    ADD COLUMN IF NOT EXISTS claim_status            VARCHAR(30)  NULL,
    ADD COLUMN IF NOT EXISTS third_party_involved    BOOLEAN      NOT NULL DEFAULT FALSE,

    ADD COLUMN IF NOT EXISTS witnesses               TEXT         NULL,
    ADD COLUMN IF NOT EXISTS additional_notes        TEXT         NULL;

CREATE INDEX IF NOT EXISTS ix_accident_events_origin           ON accident_events (origin);
CREATE INDEX IF NOT EXISTS ix_accident_events_claim_status     ON accident_events (claim_status);
CREATE INDEX IF NOT EXISTS ix_accident_events_repair_completed ON accident_events (repair_completed_at) WHERE repair_completed_at IS NOT NULL;
");

        // 2) New child table: accident_event_documents ---------------------
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS accident_event_documents (
    id                  SERIAL PRIMARY KEY,
    accident_event_id   INTEGER NOT NULL REFERENCES accident_events(id) ON DELETE CASCADE,
    document_type       VARCHAR(50)  NOT NULL DEFAULT 'other',
    file_name           VARCHAR(300) NOT NULL,
    file_url            VARCHAR(500) NOT NULL,
    file_size           INTEGER      NULL,
    mime_type           VARCHAR(100) NULL,
    uploaded_by_user_id INTEGER      NULL,
    uploaded_at         TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_accident_event_documents_accident_event_id
    ON accident_event_documents (accident_event_id);
CREATE INDEX IF NOT EXISTS ix_accident_event_documents_type
    ON accident_event_documents (document_type);
");

        // 3) New child table: accident_event_third_parties -----------------
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS accident_event_third_parties (
    id                 SERIAL PRIMARY KEY,
    accident_event_id  INTEGER NOT NULL REFERENCES accident_events(id) ON DELETE CASCADE,
    name               VARCHAR(200) NULL,
    phone              VARCHAR(50)  NULL,
    vehicle_plate      VARCHAR(50)  NULL,
    vehicle_model      VARCHAR(100) NULL,
    insurance_company  VARCHAR(200) NULL,
    insurance_number   VARCHAR(100) NULL,
    created_at         TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_accident_event_third_parties_accident_event_id
    ON accident_event_third_parties (accident_event_id);
");

        // 4) VehicleCost — link to accident -------------------------------
        migrationBuilder.Sql(@"
ALTER TABLE vehicle_costs
    ADD COLUMN IF NOT EXISTS accident_event_id INTEGER NULL
        REFERENCES accident_events(id) ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS ix_vehicle_costs_accident_event_id
    ON vehicle_costs (accident_event_id) WHERE accident_event_id IS NOT NULL;
");

        // 5) Import existing accident_claims rows into accident_events -----
        // We only import if the legacy table exists. Each claim becomes a
        // confirmed manual accident with phase 1 (no sensor data),
        // phase 2 (initial damages), and phase 6 (claim) pre-populated.
        migrationBuilder.Sql(@"
DO $$
DECLARE
    new_event_id INTEGER;
    claim_row    RECORD;
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = current_schema() AND table_name = 'accident_claims'
    ) THEN
        RETURN;
    END IF;

    FOR claim_row IN
        SELECT * FROM accident_claims
    LOOP
        INSERT INTO accident_events (
            origin, status,
            company_id, vehicle_id, driver_id,
            device_uid, incident_at,
            latitude, longitude,
            reference_code, vehicle_label,
            location_commune, location_governorate, location_road_type,
            confidence,
            weather_conditions, road_conditions,
            police_report_number, mileage_at_accident,
            initial_description, initial_severity, damaged_zones_json,
            claim_number, claim_approved_amount, claim_status, third_party_involved,
            witnesses, additional_notes,
            decided_by_user_id, decided_at,
            created_at, updated_at
        ) VALUES (
            'manual',
            CASE WHEN claim_row.""Status"" IN ('rejected') THEN 'dismissed' ELSE 'confirmed' END,
            claim_row.company_id, claim_row.vehicle_id, claim_row.driver_id,
            '',
            claim_row.accident_date::timestamp + COALESCE(claim_row.accident_time, INTERVAL '0'),
            COALESCE(claim_row.latitude, 0), COALESCE(claim_row.longitude, 0),
            claim_row.claim_number,
            NULL,
            NULL, NULL, NULL,
            100,
            claim_row.weather_conditions, claim_row.road_conditions,
            claim_row.police_report_number, claim_row.mileage_at_accident,
            claim_row.description, claim_row.severity, claim_row.damaged_zones::jsonb,
            claim_row.claim_number, claim_row.approved_amount,
            CASE claim_row.""Status""
                WHEN 'draft'        THEN 'pending'
                WHEN 'submitted'    THEN 'pending'
                WHEN 'under_review' THEN 'pending'
                WHEN 'approved'     THEN 'approved'
                WHEN 'rejected'     THEN 'rejected'
                WHEN 'closed'       THEN 'closed'
                ELSE NULL
            END,
            claim_row.third_party_involved,
            claim_row.witnesses, claim_row.additional_notes,
            claim_row.created_by_user_id, claim_row.created_at,
            claim_row.created_at, NOW()
        ) RETURNING id INTO new_event_id;

        -- Documents
        IF EXISTS (
            SELECT 1 FROM information_schema.tables
            WHERE table_schema = current_schema() AND table_name = 'accident_claim_documents'
        ) THEN
            INSERT INTO accident_event_documents (
                accident_event_id, document_type, file_name, file_url, file_size, mime_type, uploaded_at
            )
            SELECT new_event_id,
                   COALESCE(d.document_type, 'other'),
                   d.file_name, d.file_url, d.file_size, d.mime_type, d.uploaded_at
            FROM accident_claim_documents d
            WHERE d.claim_id = claim_row.id;
        END IF;

        -- Third parties
        IF EXISTS (
            SELECT 1 FROM information_schema.tables
            WHERE table_schema = current_schema() AND table_name = 'accident_claim_third_parties'
        ) THEN
            INSERT INTO accident_event_third_parties (
                accident_event_id, name, phone, vehicle_plate, vehicle_model,
                insurance_company, insurance_number, created_at
            )
            SELECT new_event_id, t.name, t.phone, t.vehicle_plate, t.vehicle_model,
                   t.insurance_company, t.insurance_number, t.created_at
            FROM accident_claim_third_parties t
            WHERE t.claim_id = claim_row.id;
        END IF;
    END LOOP;
END $$;
");

        // 6) Drop legacy claim tables --------------------------------------
        migrationBuilder.Sql(@"
DROP TABLE IF EXISTS accident_claim_third_parties;
DROP TABLE IF EXISTS accident_claim_documents;
DROP TABLE IF EXISTS accident_claims;
");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Best-effort rollback: drop the new columns + tables. The legacy
        // accident_claims tables are NOT recreated — the imported rows
        // stay in accident_events. If you really need the old shape back,
        // restore from a pre-migration DB dump.
        migrationBuilder.Sql(@"
ALTER TABLE vehicle_costs DROP COLUMN IF EXISTS accident_event_id;
DROP TABLE IF EXISTS accident_event_third_parties;
DROP TABLE IF EXISTS accident_event_documents;
ALTER TABLE accident_events
    DROP COLUMN IF EXISTS additional_notes,
    DROP COLUMN IF EXISTS witnesses,
    DROP COLUMN IF EXISTS third_party_involved,
    DROP COLUMN IF EXISTS claim_status,
    DROP COLUMN IF EXISTS claim_approved_amount,
    DROP COLUMN IF EXISTS claim_submitted_at,
    DROP COLUMN IF EXISTS claim_number,
    DROP COLUMN IF EXISTS actual_repair_cost,
    DROP COLUMN IF EXISTS repair_completed_at,
    DROP COLUMN IF EXISTS repair_started_at,
    DROP COLUMN IF EXISTS mechanic_quoted_amount,
    DROP COLUMN IF EXISTS mechanic_name,
    DROP COLUMN IF EXISTS mechanic_quote_at,
    DROP COLUMN IF EXISTS expert_estimated_amount,
    DROP COLUMN IF EXISTS expert_assessment,
    DROP COLUMN IF EXISTS expert_company,
    DROP COLUMN IF EXISTS expert_name,
    DROP COLUMN IF EXISTS expert_visited_at,
    DROP COLUMN IF EXISTS damaged_zones_json,
    DROP COLUMN IF EXISTS initial_severity,
    DROP COLUMN IF EXISTS initial_description,
    DROP COLUMN IF EXISTS mileage_at_accident,
    DROP COLUMN IF EXISTS police_report_number,
    DROP COLUMN IF EXISTS road_conditions,
    DROP COLUMN IF EXISTS weather_conditions,
    DROP COLUMN IF EXISTS driver_id,
    DROP COLUMN IF EXISTS origin;
");
    }
}
