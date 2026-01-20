-- ============================================================================
-- PRODUCTION MIGRATION SCRIPT: Sync Prod Schema to Local
-- Date: 2026-01-20
-- Version: 1.0
-- ============================================================================
-- 
-- BEFORE RUNNING:
--   1. BACKUP YOUR PRODUCTION DATABASE
--   2. Test this script on a copy of production first
--   3. Schedule maintenance window
--
-- WHAT THIS SCRIPT DOES:
--   - Creates new tables: societes, subscription_types, roles, geofence_groups, 
--     poi_visits, vehicle_user_assignments
--   - Migrates data from companies → societes
--   - Migrates data from subscriptions → subscription_types  
--   - Adds new columns to users, geofences, geofence_events, gps_positions
--   - Removes bird_flight columns from gps_positions
--   - Drops old tables: companies, employees, subscriptions, campaigns
--   - Creates backup tables for dropped data
--
-- ============================================================================

-- ============================================================================
-- STEP 1: Create new tables
-- ============================================================================

-- 1.1 subscription_types (replaces subscriptions)
CREATE TABLE IF NOT EXISTS subscription_types (
    id SERIAL PRIMARY KEY,
    name TEXT NOT NULL,
    code TEXT NOT NULL,
    description TEXT,
    target_company_type TEXT NOT NULL DEFAULT 'all',
    monthly_price NUMERIC(10,2) NOT NULL DEFAULT 0,
    quarterly_price NUMERIC(10,2) NOT NULL DEFAULT 0,
    yearly_price NUMERIC(10,2) NOT NULL DEFAULT 0,
    monthly_duration_days INTEGER NOT NULL DEFAULT 30,
    quarterly_duration_days INTEGER NOT NULL DEFAULT 90,
    yearly_duration_days INTEGER NOT NULL DEFAULT 365,
    max_vehicles INTEGER NOT NULL DEFAULT 10,
    max_users INTEGER NOT NULL DEFAULT 5,
    max_gps_devices INTEGER NOT NULL DEFAULT 10,
    max_geofences INTEGER NOT NULL DEFAULT 20,
    gps_tracking BOOLEAN NOT NULL DEFAULT true,
    gps_installation BOOLEAN NOT NULL DEFAULT false,
    api_access BOOLEAN NOT NULL DEFAULT false,
    advanced_reports BOOLEAN NOT NULL DEFAULT false,
    real_time_alerts BOOLEAN NOT NULL DEFAULT true,
    history_playback BOOLEAN NOT NULL DEFAULT true,
    fuel_analysis BOOLEAN NOT NULL DEFAULT false,
    driving_behavior BOOLEAN NOT NULL DEFAULT false,
    history_retention_days INTEGER NOT NULL DEFAULT 90,
    sort_order INTEGER NOT NULL DEFAULT 0,
    is_active BOOLEAN NOT NULL DEFAULT true,
    access_rights JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Add unique constraint on code if not exists
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'subscription_types_code_key') THEN
        ALTER TABLE subscription_types ADD CONSTRAINT subscription_types_code_key UNIQUE (code);
    END IF;
EXCEPTION WHEN OTHERS THEN NULL;
END $$;

-- 1.2 Migrate subscriptions data if table exists
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'subscriptions') THEN
        INSERT INTO subscription_types (id, name, code, description, monthly_price, max_vehicles, gps_tracking, gps_installation, created_at, updated_at)
        SELECT 
            id, 
            name, 
            COALESCE(LOWER(REPLACE(name, ' ', '_')), 'plan_' || id) as code,
            name as description,
            COALESCE(price, 0) as monthly_price,
            COALESCE(max_vehicles, 10),
            COALESCE(gps_tracking, true),
            COALESCE(gps_installation, false),
            COALESCE(created_at, NOW()),
            NOW()
        FROM subscriptions
        ON CONFLICT (id) DO NOTHING;
        
        -- Update sequence
        PERFORM setval('subscription_types_id_seq', COALESCE((SELECT MAX(id) FROM subscription_types), 1));
    END IF;
END $$;

-- 1.3 societes (replaces companies)
CREATE TABLE IF NOT EXISTS societes (
    id SERIAL PRIMARY KEY,
    name TEXT NOT NULL,
    type TEXT NOT NULL DEFAULT 'company',
    description TEXT,
    address TEXT,
    city TEXT,
    country TEXT NOT NULL DEFAULT 'MA',
    phone TEXT,
    email TEXT,
    "LogoUrl" TEXT,
    "TaxId" TEXT,
    "RC" TEXT,
    "IF" TEXT,
    "IsActive" BOOLEAN NOT NULL DEFAULT true,
    subscription_started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "SubscriptionExpiresAt" TIMESTAMPTZ,
    billing_cycle TEXT NOT NULL DEFAULT 'monthly',
    subscription_status TEXT NOT NULL DEFAULT 'active',
    last_payment_at TIMESTAMPTZ,
    next_payment_amount NUMERIC,
    subscription_type_id INTEGER REFERENCES subscription_types(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    settings JSONB
);

-- 1.4 Migrate companies data if table exists
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'companies') THEN
        INSERT INTO societes (id, name, type, address, city, country, phone, email, "LogoUrl", "TaxId", "RC", "IF", "IsActive", subscription_started_at, "SubscriptionExpiresAt", subscription_type_id, created_at, updated_at, settings)
        SELECT 
            id,
            name,
            COALESCE(type, 'company'),
            address,
            city,
            COALESCE(country, 'MA'),
            phone,
            email,
            "LogoUrl",
            "TaxId",
            "RC",
            "IF",
            COALESCE("IsActive", true),
            COALESCE(created_at, NOW()),
            "SubscriptionExpiresAt",
            subscription_id,
            COALESCE(created_at, NOW()),
            COALESCE(updated_at, NOW()),
            settings
        FROM companies
        ON CONFLICT (id) DO NOTHING;
        
        PERFORM setval('societes_id_seq', COALESCE((SELECT MAX(id) FROM societes), 1));
    END IF;
END $$;

-- 1.5 roles table
CREATE TABLE IF NOT EXISTS roles (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description TEXT,
    role_type VARCHAR(50) NOT NULL DEFAULT 'employee',
    societe_id INTEGER REFERENCES societes(id),
    is_system BOOLEAN NOT NULL DEFAULT false,
    is_default BOOLEAN NOT NULL DEFAULT false,
    permissions JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Insert default roles if empty
INSERT INTO roles (name, description, role_type, is_system, is_default, permissions)
SELECT 'Administrateur', 'Accès complet au système', 'company_admin', true, false, '{"all": true}'
WHERE NOT EXISTS (SELECT 1 FROM roles WHERE name = 'Administrateur' AND is_system = true);

INSERT INTO roles (name, description, role_type, is_system, is_default, permissions)
SELECT 'Gestionnaire de flotte', 'Gestion des véhicules et conducteurs', 'employee', true, true, '{"vehicles": true, "drivers": true, "reports": true}'
WHERE NOT EXISTS (SELECT 1 FROM roles WHERE name = 'Gestionnaire de flotte' AND is_system = true);

INSERT INTO roles (name, description, role_type, is_system, is_default, permissions)
SELECT 'Conducteur', 'Accès limité pour les conducteurs', 'employee', true, false, '{"own_vehicle": true, "own_trips": true}'
WHERE NOT EXISTS (SELECT 1 FROM roles WHERE name = 'Conducteur' AND is_system = true);

-- 1.6 geofence_groups table
CREATE TABLE IF NOT EXISTS geofence_groups (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description VARCHAR(500),
    color VARCHAR(20) NOT NULL DEFAULT '#3B82F6',
    is_active BOOLEAN NOT NULL DEFAULT true,
    societe_id INTEGER REFERENCES societes(id),
    company_id INTEGER NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- 1.7 poi_visits table (handle both Id and id column names)
DO $$
DECLARE
    poi_id_col TEXT;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'poi_visits') THEN
        -- Determine POI id column name
        SELECT column_name INTO poi_id_col 
        FROM information_schema.columns 
        WHERE table_name = 'points_of_interest' AND column_name IN ('id', 'Id')
        LIMIT 1;
        
        IF poi_id_col = 'Id' THEN
            CREATE TABLE poi_visits (
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
        ELSE
            CREATE TABLE poi_visits (
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
                CONSTRAINT fk_poi_visits_poi FOREIGN KEY (poi_id) REFERENCES points_of_interest(id) ON DELETE CASCADE,
                CONSTRAINT fk_poi_visits_vehicle FOREIGN KEY (vehicle_id) REFERENCES vehicles(id) ON DELETE CASCADE
            );
        END IF;
    END IF;
END $$;

-- 1.8 vehicle_user_assignments table
CREATE TABLE IF NOT EXISTS vehicle_user_assignments (
    "Id" SERIAL PRIMARY KEY,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    unassigned_at TIMESTAMPTZ,
    is_active BOOLEAN NOT NULL DEFAULT true,
    assigned_by TEXT,
    "Notes" TEXT
);

-- ============================================================================
-- STEP 2: Add columns to existing tables
-- ============================================================================

-- 2.1 users table
ALTER TABLE users ADD COLUMN IF NOT EXISTS user_type VARCHAR(50) NOT NULL DEFAULT 'user';
ALTER TABLE users ADD COLUMN IF NOT EXISTS role_id INTEGER REFERENCES roles(id);
ALTER TABLE users ADD COLUMN IF NOT EXISTS is_company_admin BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS date_of_birth TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN IF NOT EXISTS cin VARCHAR(50);
ALTER TABLE users ADD COLUMN IF NOT EXISTS hire_date TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN IF NOT EXISTS license_number VARCHAR(50);
ALTER TABLE users ADD COLUMN IF NOT EXISTS license_expiry TIMESTAMPTZ;

-- Migrate employee data to users if employees table exists
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'employees') THEN
        UPDATE users u
        SET 
            hire_date = COALESCE(u.hire_date, e.hire_date),
            license_number = COALESCE(u.license_number, e.license_number),
            license_expiry = COALESCE(u.license_expiry, e.license_expiry)
        FROM employees e
        WHERE u.email = e.email AND e.email IS NOT NULL;
    END IF;
END $$;

-- 2.2 geofences table
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS group_id INTEGER REFERENCES geofence_groups(id);
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS icon_name VARCHAR(50);
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS active_start_time INTERVAL;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS active_end_time INTERVAL;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS active_days JSONB;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS max_stay_duration_minutes INTEGER;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS notification_cooldown_minutes INTEGER NOT NULL DEFAULT 0;

-- 2.3 geofence_events table
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS device_id INTEGER;
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS address VARCHAR(500);
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS duration_inside_seconds INTEGER;
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS is_notified BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS notified_at TIMESTAMPTZ;

-- 2.4 points_of_interest columns
ALTER TABLE points_of_interest ADD COLUMN IF NOT EXISTS radius DOUBLE PRECISION NOT NULL DEFAULT 100;
ALTER TABLE points_of_interest ADD COLUMN IF NOT EXISTS alert_on_arrival BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE points_of_interest ADD COLUMN IF NOT EXISTS alert_on_departure BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE points_of_interest ADD COLUMN IF NOT EXISTS contact_name VARCHAR(100);
ALTER TABLE points_of_interest ADD COLUMN IF NOT EXISTS expected_stay_minutes INTEGER;
ALTER TABLE points_of_interest ADD COLUMN IF NOT EXISTS notification_cooldown_minutes INTEGER NOT NULL DEFAULT 0;
ALTER TABLE points_of_interest ADD COLUMN IF NOT EXISTS external_id VARCHAR(100);
ALTER TABLE points_of_interest ADD COLUMN IF NOT EXISTS tags JSONB;
ALTER TABLE points_of_interest ADD COLUMN IF NOT EXISTS visit_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE points_of_interest ADD COLUMN IF NOT EXISTS last_visit_at TIMESTAMPTZ;

-- 2.5 gps_positions
ALTER TABLE gps_positions ADD COLUMN IF NOT EXISTS event_key VARCHAR(128);

-- ============================================================================
-- STEP 3: Remove deprecated columns
-- ============================================================================

ALTER TABLE gps_positions DROP COLUMN IF EXISTS is_bird_flight;
ALTER TABLE gps_positions DROP COLUMN IF EXISTS bird_flight_reason;
ALTER TABLE gps_positions DROP COLUMN IF EXISTS implicit_speed_kph;

-- Fix gps_devices Mat column duplication
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'gps_devices' AND column_name = 'Mat') THEN
        UPDATE gps_devices SET mat = "Mat" WHERE mat IS NULL AND "Mat" IS NOT NULL;
        ALTER TABLE gps_devices DROP COLUMN IF EXISTS "Mat";
    END IF;
END $$;

-- ============================================================================
-- STEP 4: Update foreign keys
-- ============================================================================

-- Drop old FKs to companies
ALTER TABLE vehicles DROP CONSTRAINT IF EXISTS "FK_vehicles_companies_company_id";
ALTER TABLE gps_devices DROP CONSTRAINT IF EXISTS "FK_gps_devices_companies_company_id";
ALTER TABLE geofences DROP CONSTRAINT IF EXISTS "FK_geofences_companies_company_id";
ALTER TABLE maintenance_records DROP CONSTRAINT IF EXISTS "FK_maintenance_records_companies_company_id";
ALTER TABLE vehicle_costs DROP CONSTRAINT IF EXISTS "FK_vehicle_costs_companies_company_id";
ALTER TABLE users DROP CONSTRAINT IF EXISTS "FK_users_companies_company_id";

-- Ensure all company_ids exist in societes before adding FK
DO $$
BEGIN
    -- Insert missing societes for orphaned company_ids
    INSERT INTO societes (id, name, type, country, created_at, updated_at)
    SELECT DISTINCT v.company_id, 'Company ' || v.company_id, 'company', 'MA', NOW(), NOW()
    FROM vehicles v
    WHERE v.company_id IS NOT NULL 
    AND NOT EXISTS (SELECT 1 FROM societes s WHERE s.id = v.company_id)
    ON CONFLICT (id) DO NOTHING;
    
    INSERT INTO societes (id, name, type, country, created_at, updated_at)
    SELECT DISTINCT u.company_id, 'Company ' || u.company_id, 'company', 'MA', NOW(), NOW()
    FROM users u
    WHERE u.company_id IS NOT NULL 
    AND NOT EXISTS (SELECT 1 FROM societes s WHERE s.id = u.company_id)
    ON CONFLICT (id) DO NOTHING;
END $$;

-- Add new FKs to societes
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

-- ============================================================================
-- STEP 5: Backup and drop old tables
-- ============================================================================

-- Drop FK dependencies first
ALTER TABLE driver_assignments DROP CONSTRAINT IF EXISTS "FK_driver_assignments_employees_DriverId";
ALTER TABLE driver_scores DROP CONSTRAINT IF EXISTS "FK_driver_scores_employees_DriverId";
ALTER TABLE driving_events DROP CONSTRAINT IF EXISTS "FK_driving_events_employees_DriverId";
ALTER TABLE fuel_records DROP CONSTRAINT IF EXISTS "FK_fuel_records_employees_driver_id";
ALTER TABLE vehicle_stops DROP CONSTRAINT IF EXISTS "FK_vehicle_stops_employees_driver_id";
ALTER TABLE trips DROP CONSTRAINT IF EXISTS "FK_trips_employees_DriverId";
ALTER TABLE reservations DROP CONSTRAINT IF EXISTS "FK_reservations_employees_AssignedDriverId";

-- Drop campaigns (if exists)
DROP TABLE IF EXISTS campaigns CASCADE;

-- Backup and drop employees
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'employees') THEN
        CREATE TABLE IF NOT EXISTS _backup_employees_migration AS SELECT * FROM employees;
        DROP TABLE employees CASCADE;
    END IF;
END $$;

-- Backup and drop companies
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'companies') THEN
        CREATE TABLE IF NOT EXISTS _backup_companies_migration AS SELECT * FROM companies;
        DROP TABLE companies CASCADE;
    END IF;
END $$;

-- Backup and drop subscriptions
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'subscriptions') THEN
        CREATE TABLE IF NOT EXISTS _backup_subscriptions_migration AS SELECT * FROM subscriptions;
        DROP TABLE subscriptions CASCADE;
    END IF;
END $$;

-- ============================================================================
-- STEP 6: Create indexes
-- ============================================================================

CREATE INDEX IF NOT EXISTS ix_roles_societe_id ON roles(societe_id);
CREATE INDEX IF NOT EXISTS ix_geofence_groups_company_id ON geofence_groups(company_id);
CREATE INDEX IF NOT EXISTS ix_poi_visits_poi_id ON poi_visits(poi_id);
CREATE INDEX IF NOT EXISTS ix_poi_visits_vehicle_id ON poi_visits(vehicle_id);
CREATE INDEX IF NOT EXISTS ix_vehicle_user_assignments_vehicle_id ON vehicle_user_assignments(vehicle_id);
CREATE INDEX IF NOT EXISTS ix_vehicle_user_assignments_user_id ON vehicle_user_assignments(user_id);
CREATE INDEX IF NOT EXISTS ix_users_role_id ON users(role_id);

-- ============================================================================
-- STEP 7: Record migration in EF history
-- ============================================================================

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260120210000_ManualProdToLocalSync', '9.0.1')
ON CONFLICT ("MigrationId") DO NOTHING;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260107180000_RemoveBirdFlightFilter', '9.0.1')
ON CONFLICT ("MigrationId") DO NOTHING;

-- ============================================================================
-- DONE
-- ============================================================================

SELECT '✅ Migration completed successfully!' as status;
