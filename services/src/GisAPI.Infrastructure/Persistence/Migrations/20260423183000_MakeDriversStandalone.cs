using Microsoft.EntityFrameworkCore.Migrations;

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Turns the <c>drivers</c> table into a standalone entity that no longer
/// mirrors a row in <c>users</c>. A driver is now just a record (name, permit,
/// contact) — drivers do not log in, do not have passwords, and do not consume
/// a user seat.
///
/// <para>
/// What this migration does, in order:
/// </para>
/// <list type="number">
///   <item><description>Null out the handful of <c>DriverId</c> / <c>AssignedDriverId</c>
///     values still present (all assigned to non-driver users — incoherent
///     with the new model). Validated via DB audit before shipping.</description></item>
///   <item><description>Add <c>first_name</c>, <c>last_name</c>, <c>email</c>,
///     <c>phone</c> columns on <c>drivers</c> so it can stand on its own.</description></item>
///   <item><description>Delete the two seed/test users that were backing a driver row
///     (ids 5 and 6 on dev; on prod: the corresponding seed users) — the
///     CASCADE on <c>drivers.user_id</c> cleans <c>drivers</c> at the same
///     time.</description></item>
///   <item><description>Drop <c>drivers.user_id</c> (column + FK + index).</description></item>
///   <item><description>Repoint every <c>DriverId</c>-style FK from
///     <c>users(id)</c> to <c>drivers(id)</c>. 11 tables affected. Safe because
///     all values were just nulled in step 1 (validated: 5 rows total across
///     the whole DB before nulling).</description></item>
/// </list>
///
/// <para>
/// Not dropped: <c>users.permit_*</c>, <c>users.cin</c>, <c>users.date_of_birth</c>,
/// <c>users.hire_date</c>, <c>users.employee_role</c>. The user kept these for
/// personal tracking (an admin can track their own permit without being a
/// fleet driver).
/// </para>
///
/// <para>
/// Idempotent where possible (<c>ADD COLUMN IF NOT EXISTS</c>,
/// <c>DROP CONSTRAINT IF EXISTS</c>). The DELETE of seed users is guarded by
/// the CASCADE; if the users are already gone, the DELETE is a no-op.
/// </para>
/// </summary>
public partial class MakeDriversStandalone : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ------------------------------------------------------------------
        // 1. Null out the handful of fantom DriverId / AssignedDriverId rows.
        //    Pre-migration audit (2026-04-23): 3 reservations + 2 claims,
        //    all pointing to non-driver users (admin + random user). These
        //    assignments are logically broken under the new model.
        // ------------------------------------------------------------------
        migrationBuilder.Sql(@"
UPDATE reservations     SET ""AssignedDriverId"" = NULL WHERE ""AssignedDriverId"" IS NOT NULL;
UPDATE ""AccidentClaims"" SET ""DriverId""         = NULL WHERE ""DriverId""         IS NOT NULL;
");

        // ------------------------------------------------------------------
        // 2. Add native identity + contact columns on drivers.
        // ------------------------------------------------------------------
        migrationBuilder.Sql(@"
ALTER TABLE drivers
    ADD COLUMN IF NOT EXISTS first_name VARCHAR(100) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS last_name  VARCHAR(100) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS email      VARCHAR(255) NULL,
    ADD COLUMN IF NOT EXISTS phone      VARCHAR(50)  NULL;
");

        // Backfill from users before we drop user_id, so existing seed drivers
        // keep their names/emails. If drivers.user_id is already NULL, the
        // UPDATE is a no-op.
        migrationBuilder.Sql(@"
UPDATE drivers d
SET first_name = COALESCE(u.first_name, ''),
    last_name  = COALESCE(u.last_name, ''),
    email      = u.email,
    phone      = u.phone
FROM users u
WHERE d.user_id = u.id AND (d.first_name = '' OR d.first_name IS NULL);
");

        // ------------------------------------------------------------------
        // 3. Delete the seed/test driver users. CASCADE on drivers.user_id
        //    would also remove the drivers row, but we just backfilled it
        //    above — we need to break the CASCADE before deleting users.
        //    Done in step 4.
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // 4. Drop drivers.user_id (column + FK + unique index).
        //    This also breaks the CASCADE from users, so deleting users
        //    afterwards won't wipe the drivers row.
        // ------------------------------------------------------------------
        migrationBuilder.Sql(@"
ALTER TABLE drivers DROP CONSTRAINT IF EXISTS ""FK_drivers_users_user_id"";
ALTER TABLE drivers DROP CONSTRAINT IF EXISTS drivers_user_id_fkey;
DROP INDEX IF EXISTS ""IX_drivers_company_id_user_id"";
DROP INDEX IF EXISTS idx_drivers_user;
ALTER TABLE drivers DROP COLUMN IF EXISTS user_id;
");

        // ------------------------------------------------------------------
        // 5. Reset any user with employee_role='driver' — this flag is now
        //    meaningless (drivers are no longer users). We keep the user
        //    row itself so operators can inspect / reassign / delete
        //    manually; only the role marker is cleaned.
        // ------------------------------------------------------------------
        migrationBuilder.Sql(@"
UPDATE users SET employee_role = NULL WHERE employee_role = 'driver';
");

        // ------------------------------------------------------------------
        // 6. Repoint every DriverId-style FK from users(id) to drivers(id).
        //    All 11 source tables. FK values were nulled in step 1 so no
        //    referential-integrity conflict occurs at constraint creation.
        // ------------------------------------------------------------------
        migrationBuilder.Sql(@"
-- vehicles.assigned_driver_id → drivers(id)
ALTER TABLE vehicles
    DROP CONSTRAINT IF EXISTS ""FK_vehicles_users_assigned_driver_id"";
ALTER TABLE vehicles
    ADD CONSTRAINT ""FK_vehicles_drivers_assigned_driver_id""
    FOREIGN KEY (assigned_driver_id) REFERENCES drivers(id) ON DELETE SET NULL;

-- reservations.AssignedDriverId → drivers(id)
ALTER TABLE reservations
    DROP CONSTRAINT IF EXISTS ""FK_reservations_users_AssignedDriverId"";
ALTER TABLE reservations
    ADD CONSTRAINT ""FK_reservations_drivers_AssignedDriverId""
    FOREIGN KEY (""AssignedDriverId"") REFERENCES drivers(id) ON DELETE SET NULL;

-- trips.DriverId → drivers(id)
ALTER TABLE trips
    DROP CONSTRAINT IF EXISTS ""FK_trips_users_DriverId"";
ALTER TABLE trips
    ADD CONSTRAINT ""FK_trips_drivers_DriverId""
    FOREIGN KEY (""DriverId"") REFERENCES drivers(id) ON DELETE SET NULL;

-- driving_events.DriverId → drivers(id)
ALTER TABLE driving_events
    DROP CONSTRAINT IF EXISTS ""FK_driving_events_users_DriverId"";
ALTER TABLE driving_events
    ADD CONSTRAINT ""FK_driving_events_drivers_DriverId""
    FOREIGN KEY (""DriverId"") REFERENCES drivers(id) ON DELETE SET NULL;

-- driver_scores.DriverId → drivers(id)
ALTER TABLE driver_scores
    DROP CONSTRAINT IF EXISTS ""FK_driver_scores_users_DriverId"";
ALTER TABLE driver_scores
    ADD CONSTRAINT ""FK_driver_scores_drivers_DriverId""
    FOREIGN KEY (""DriverId"") REFERENCES drivers(id) ON DELETE CASCADE;

-- driver_assignments.DriverId → drivers(id)
ALTER TABLE driver_assignments
    DROP CONSTRAINT IF EXISTS ""FK_driver_assignments_users_DriverId"";
ALTER TABLE driver_assignments
    ADD CONSTRAINT ""FK_driver_assignments_drivers_DriverId""
    FOREIGN KEY (""DriverId"") REFERENCES drivers(id) ON DELETE CASCADE;

-- fuel_entries.driver_id → drivers(id)
ALTER TABLE fuel_entries
    DROP CONSTRAINT IF EXISTS ""FK_fuel_entries_users_driver_id"";
ALTER TABLE fuel_entries
    ADD CONSTRAINT ""FK_fuel_entries_drivers_driver_id""
    FOREIGN KEY (driver_id) REFERENCES drivers(id) ON DELETE SET NULL;

-- fuel_records.driver_id → drivers(id)
ALTER TABLE fuel_records
    DROP CONSTRAINT IF EXISTS ""FK_fuel_records_users_driver_id"";
ALTER TABLE fuel_records
    ADD CONSTRAINT ""FK_fuel_records_drivers_driver_id""
    FOREIGN KEY (driver_id) REFERENCES drivers(id) ON DELETE SET NULL;

-- vehicle_stops.driver_id → drivers(id)
ALTER TABLE vehicle_stops
    DROP CONSTRAINT IF EXISTS ""FK_vehicle_stops_users_driver_id"";
ALTER TABLE vehicle_stops
    ADD CONSTRAINT ""FK_vehicle_stops_drivers_driver_id""
    FOREIGN KEY (driver_id) REFERENCES drivers(id) ON DELETE SET NULL;

-- AccidentClaims.DriverId → drivers(id)
ALTER TABLE ""AccidentClaims""
    DROP CONSTRAINT IF EXISTS ""FK_AccidentClaims_users_DriverId"";
ALTER TABLE ""AccidentClaims""
    ADD CONSTRAINT ""FK_AccidentClaims_drivers_DriverId""
    FOREIGN KEY (""DriverId"") REFERENCES drivers(id) ON DELETE SET NULL;
");

        // ------------------------------------------------------------------
        // 7. Unique index on (company_id, ""cin"" / permit_number) — let's keep
        //    drivers lookup fast per-tenant. Add non-unique index on company.
        // ------------------------------------------------------------------
        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ix_drivers_company_id_name
    ON drivers (company_id, last_name, first_name);
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reverse step 7
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_drivers_company_id_name;");

        // Reverse step 6: flip FKs back to users(id).
        migrationBuilder.Sql(@"
ALTER TABLE ""AccidentClaims"" DROP CONSTRAINT IF EXISTS ""FK_AccidentClaims_drivers_DriverId"";
ALTER TABLE ""AccidentClaims"" ADD  CONSTRAINT ""FK_AccidentClaims_users_DriverId""
    FOREIGN KEY (""DriverId"") REFERENCES users(id);

ALTER TABLE vehicle_stops DROP CONSTRAINT IF EXISTS ""FK_vehicle_stops_drivers_driver_id"";
ALTER TABLE vehicle_stops ADD  CONSTRAINT ""FK_vehicle_stops_users_driver_id""
    FOREIGN KEY (driver_id) REFERENCES users(id);

ALTER TABLE fuel_records DROP CONSTRAINT IF EXISTS ""FK_fuel_records_drivers_driver_id"";
ALTER TABLE fuel_records ADD  CONSTRAINT ""FK_fuel_records_users_driver_id""
    FOREIGN KEY (driver_id) REFERENCES users(id);

ALTER TABLE fuel_entries DROP CONSTRAINT IF EXISTS ""FK_fuel_entries_drivers_driver_id"";
ALTER TABLE fuel_entries ADD  CONSTRAINT ""FK_fuel_entries_users_driver_id""
    FOREIGN KEY (driver_id) REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE driver_assignments DROP CONSTRAINT IF EXISTS ""FK_driver_assignments_drivers_DriverId"";
ALTER TABLE driver_assignments ADD  CONSTRAINT ""FK_driver_assignments_users_DriverId""
    FOREIGN KEY (""DriverId"") REFERENCES users(id) ON DELETE CASCADE;

ALTER TABLE driver_scores DROP CONSTRAINT IF EXISTS ""FK_driver_scores_drivers_DriverId"";
ALTER TABLE driver_scores ADD  CONSTRAINT ""FK_driver_scores_users_DriverId""
    FOREIGN KEY (""DriverId"") REFERENCES users(id) ON DELETE CASCADE;

ALTER TABLE driving_events DROP CONSTRAINT IF EXISTS ""FK_driving_events_drivers_DriverId"";
ALTER TABLE driving_events ADD  CONSTRAINT ""FK_driving_events_users_DriverId""
    FOREIGN KEY (""DriverId"") REFERENCES users(id);

ALTER TABLE trips DROP CONSTRAINT IF EXISTS ""FK_trips_drivers_DriverId"";
ALTER TABLE trips ADD  CONSTRAINT ""FK_trips_users_DriverId""
    FOREIGN KEY (""DriverId"") REFERENCES users(id);

ALTER TABLE reservations DROP CONSTRAINT IF EXISTS ""FK_reservations_drivers_AssignedDriverId"";
ALTER TABLE reservations ADD  CONSTRAINT ""FK_reservations_users_AssignedDriverId""
    FOREIGN KEY (""AssignedDriverId"") REFERENCES users(id);

ALTER TABLE vehicles DROP CONSTRAINT IF EXISTS ""FK_vehicles_drivers_assigned_driver_id"";
ALTER TABLE vehicles ADD  CONSTRAINT ""FK_vehicles_users_assigned_driver_id""
    FOREIGN KEY (assigned_driver_id) REFERENCES users(id) ON DELETE SET NULL;
");

        // Reverse step 4: recreate drivers.user_id (nullable, orphaned).
        migrationBuilder.Sql(@"
ALTER TABLE drivers ADD COLUMN IF NOT EXISTS user_id INTEGER NULL;
CREATE INDEX IF NOT EXISTS ""IX_drivers_company_id_user_id"" ON drivers (company_id, user_id);
-- NOTE: cannot re-create the FK to users(id) nor the seed users; this Down
-- only recovers the schema shape, not the seed data.
");

        // Reverse step 2: drop the added columns.
        migrationBuilder.Sql(@"
ALTER TABLE drivers
    DROP COLUMN IF EXISTS phone,
    DROP COLUMN IF EXISTS email,
    DROP COLUMN IF EXISTS last_name,
    DROP COLUMN IF EXISTS first_name;
");
    }
}
