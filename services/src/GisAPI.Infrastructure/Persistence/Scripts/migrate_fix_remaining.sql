-- ============================================================================
-- FIX SCRIPT: Complete remaining migration items
-- ============================================================================

-- 1. Create poi_visits with correct FK reference (using "Id" not "id")
CREATE TABLE IF NOT EXISTS poi_visits (
    id SERIAL PRIMARY KEY,
    poi_id INTEGER NOT NULL,
    vehicle_id INTEGER NOT NULL,
    device_id INTEGER,
    arrival_at TIMESTAMPTZ NOT NULL,
    departure_at TIMESTAMPTZ,
    duration_minutes INTEGER,
    arrival_lat DOUBLE PRECISION NOT NULL,
    arrival_lng DOUBLE PRECISION NOT NULL,
    departure_lat DOUBLE PRECISION,
    departure_lng DOUBLE PRECISION,
    notes VARCHAR(500),
    is_notified BOOLEAN NOT NULL DEFAULT false,
    company_id INTEGER NOT NULL,
    CONSTRAINT fk_poi_visits_poi FOREIGN KEY (poi_id) REFERENCES points_of_interest("Id") ON DELETE CASCADE,
    CONSTRAINT fk_poi_visits_vehicle FOREIGN KEY (vehicle_id) REFERENCES vehicles(id) ON DELETE CASCADE
);

-- 2. Add columns to users table
ALTER TABLE users ADD COLUMN IF NOT EXISTS user_type VARCHAR(50) NOT NULL DEFAULT 'user';
ALTER TABLE users ADD COLUMN IF NOT EXISTS role_id INTEGER REFERENCES roles(id);
ALTER TABLE users ADD COLUMN IF NOT EXISTS is_company_admin BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS date_of_birth TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN IF NOT EXISTS cin VARCHAR(50);
ALTER TABLE users ADD COLUMN IF NOT EXISTS hire_date TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN IF NOT EXISTS license_number VARCHAR(50);
ALTER TABLE users ADD COLUMN IF NOT EXISTS license_expiry TIMESTAMPTZ;

-- 3. Add columns to geofences table
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS group_id INTEGER REFERENCES geofence_groups(id);
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS icon_name VARCHAR(50);
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS active_start_time INTERVAL;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS active_end_time INTERVAL;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS active_days JSONB;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS max_stay_duration_minutes INTEGER;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS notification_cooldown_minutes INTEGER NOT NULL DEFAULT 0;

-- 4. Add columns to geofence_events table
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS device_id INTEGER;
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS address VARCHAR(500);
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS duration_inside_seconds INTEGER;
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS is_notified BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS notified_at TIMESTAMPTZ;

-- 5. Add event_key column to gps_positions
ALTER TABLE gps_positions ADD COLUMN IF NOT EXISTS event_key VARCHAR(128);

-- 6. Remove Bird Flight columns
ALTER TABLE gps_positions DROP COLUMN IF EXISTS is_bird_flight;
ALTER TABLE gps_positions DROP COLUMN IF EXISTS bird_flight_reason;
ALTER TABLE gps_positions DROP COLUMN IF EXISTS implicit_speed_kph;

-- 7. Fix gps_devices mat column
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'gps_devices' AND column_name = 'Mat') THEN
        UPDATE gps_devices SET mat = "Mat" WHERE mat IS NULL AND "Mat" IS NOT NULL;
        ALTER TABLE gps_devices DROP COLUMN IF EXISTS "Mat";
    END IF;
END $$;

-- 8. Update FK references from companies to societes
ALTER TABLE vehicles DROP CONSTRAINT IF EXISTS "FK_vehicles_companies_company_id";
ALTER TABLE gps_devices DROP CONSTRAINT IF EXISTS "FK_gps_devices_companies_company_id";
ALTER TABLE geofences DROP CONSTRAINT IF EXISTS "FK_geofences_companies_company_id";
ALTER TABLE maintenance_records DROP CONSTRAINT IF EXISTS "FK_maintenance_records_companies_company_id";
ALTER TABLE vehicle_costs DROP CONSTRAINT IF EXISTS "FK_vehicle_costs_companies_company_id";

-- 9. Add FK to societes for main tables
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_vehicles_societes_company_id') THEN
        ALTER TABLE vehicles ADD CONSTRAINT "FK_vehicles_societes_company_id" 
        FOREIGN KEY (company_id) REFERENCES societes(id);
    END IF;
    
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_users_societes_company_id') THEN
        ALTER TABLE users ADD CONSTRAINT "FK_users_societes_company_id" 
        FOREIGN KEY (company_id) REFERENCES societes(id);
    END IF;
END $$;

-- 10. Create indexes
CREATE INDEX IF NOT EXISTS ix_roles_societe_id ON roles(societe_id);
CREATE INDEX IF NOT EXISTS ix_geofence_groups_company_id ON geofence_groups(company_id);
CREATE INDEX IF NOT EXISTS ix_poi_visits_poi_id ON poi_visits(poi_id);
CREATE INDEX IF NOT EXISTS ix_poi_visits_vehicle_id ON poi_visits(vehicle_id);
CREATE INDEX IF NOT EXISTS ix_vehicle_user_assignments_vehicle_id ON vehicle_user_assignments(vehicle_id);
CREATE INDEX IF NOT EXISTS ix_vehicle_user_assignments_user_id ON vehicle_user_assignments(user_id);
CREATE INDEX IF NOT EXISTS ix_users_role_id ON users(role_id);

-- 11. Record migration in history
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260120210000_ManualProdToLocalSync', '9.0.1')
ON CONFLICT ("MigrationId") DO NOTHING;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260107180000_RemoveBirdFlightFilter', '9.0.1')
ON CONFLICT ("MigrationId") DO NOTHING;

-- Done
SELECT 'Migration completed successfully!' as status;
