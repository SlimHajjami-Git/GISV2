-- ============================================================================
-- MIGRATION SCRIPT: Prod to Local Schema Sync
-- Date: 2026-01-20
-- Description: Migrates production database to match local schema
-- ============================================================================
-- IMPORTANT: 
--   1. BACKUP YOUR DATABASE BEFORE RUNNING THIS SCRIPT
--   2. Run this script in a transaction (BEGIN/COMMIT)
--   3. Test on a copy of production first
-- ============================================================================

-- NOTE: Run without transaction first to identify issues
-- BEGIN;

-- ============================================================================
-- STEP 1: Create new tables that don't exist in prod
-- ============================================================================

-- 1.1 Create subscription_types table (replaces subscriptions)
CREATE TABLE IF NOT EXISTS subscription_types (
    id SERIAL PRIMARY KEY,
    name TEXT NOT NULL,
    code TEXT NOT NULL UNIQUE,
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

-- 1.2 Migrate data from subscriptions to subscription_types
INSERT INTO subscription_types (id, name, code, description, monthly_price, max_vehicles, gps_tracking, gps_installation, created_at, updated_at)
SELECT 
    id, 
    name, 
    LOWER(REPLACE(name, ' ', '_')) as code,
    name as description,
    price as monthly_price,
    max_vehicles,
    gps_tracking,
    gps_installation,
    created_at,
    NOW()
FROM subscriptions
ON CONFLICT (id) DO NOTHING;

-- Update sequence for subscription_types
SELECT setval('subscription_types_id_seq', COALESCE((SELECT MAX(id) FROM subscription_types), 1));

-- 1.3 Create societes table (replaces companies)
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

-- 1.4 Migrate data from companies to societes
INSERT INTO societes (id, name, type, address, city, country, phone, email, "LogoUrl", "TaxId", "RC", "IF", "IsActive", subscription_started_at, "SubscriptionExpiresAt", subscription_type_id, created_at, updated_at, settings)
SELECT 
    id,
    name,
    type,
    address,
    city,
    country,
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
    created_at,
    updated_at,
    settings
FROM companies
ON CONFLICT (id) DO NOTHING;

-- Update sequence for societes
SELECT setval('societes_id_seq', COALESCE((SELECT MAX(id) FROM societes), 1));

-- 1.5 Create roles table
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

-- Insert default roles
INSERT INTO roles (name, description, role_type, is_system, is_default, permissions)
VALUES 
    ('Administrateur', 'Accès complet au système', 'company_admin', true, false, '{"all": true}'),
    ('Gestionnaire de flotte', 'Gestion des véhicules et conducteurs', 'employee', true, true, '{"vehicles": true, "drivers": true, "reports": true}'),
    ('Conducteur', 'Accès limité pour les conducteurs', 'employee', true, false, '{"own_vehicle": true, "own_trips": true}')
ON CONFLICT DO NOTHING;

-- 1.6 Create geofence_groups table
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

-- 1.7 Create poi_visits table
CREATE TABLE IF NOT EXISTS poi_visits (
    id SERIAL PRIMARY KEY,
    poi_id INTEGER NOT NULL REFERENCES points_of_interest(id) ON DELETE CASCADE,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
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
    company_id INTEGER NOT NULL
);

-- 1.8 Create vehicle_user_assignments table
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
-- STEP 2: Add missing columns to existing tables
-- ============================================================================

-- 2.1 Add columns to users table
ALTER TABLE users ADD COLUMN IF NOT EXISTS user_type VARCHAR(50) NOT NULL DEFAULT 'user';
ALTER TABLE users ADD COLUMN IF NOT EXISTS role_id INTEGER REFERENCES roles(id);
ALTER TABLE users ADD COLUMN IF NOT EXISTS is_company_admin BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS date_of_birth TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN IF NOT EXISTS cin VARCHAR(50);
ALTER TABLE users ADD COLUMN IF NOT EXISTS hire_date TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN IF NOT EXISTS license_number VARCHAR(50);
ALTER TABLE users ADD COLUMN IF NOT EXISTS license_expiry TIMESTAMPTZ;

-- 2.2 Migrate employee data to users (hire_date, license fields)
UPDATE users u
SET 
    hire_date = e.hire_date,
    license_number = e.license_number,
    license_expiry = e.license_expiry
FROM employees e
WHERE u.email = e.email AND e.email IS NOT NULL;

-- 2.3 Add columns to geofences table
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS group_id INTEGER REFERENCES geofence_groups(id);
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS icon_name VARCHAR(50);
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS active_start_time INTERVAL;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS active_end_time INTERVAL;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS active_days JSONB;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS max_stay_duration_minutes INTEGER;
ALTER TABLE geofences ADD COLUMN IF NOT EXISTS notification_cooldown_minutes INTEGER NOT NULL DEFAULT 0;

-- 2.4 Add columns to geofence_events table
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS device_id INTEGER;
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS address VARCHAR(500);
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS duration_inside_seconds INTEGER;
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS is_notified BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS notified_at TIMESTAMPTZ;

-- 2.5 Add columns to points_of_interest table
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

-- 2.6 Add event_key column to gps_positions if not exists
ALTER TABLE gps_positions ADD COLUMN IF NOT EXISTS event_key VARCHAR(128);

-- 2.7 Fix gps_devices mat column (handle duplicate Mat/mat)
DO $$
BEGIN
    -- If both Mat and mat exist, copy data and drop Mat
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'gps_devices' AND column_name = 'Mat') THEN
        UPDATE gps_devices SET mat = "Mat" WHERE mat IS NULL AND "Mat" IS NOT NULL;
        ALTER TABLE gps_devices DROP COLUMN IF EXISTS "Mat";
    END IF;
END $$;

-- ============================================================================
-- STEP 3: Update foreign key references
-- ============================================================================

-- 3.1 Update users to reference societes instead of companies
-- The company_id column already exists and points to the same IDs

-- 3.2 Add foreign key from users to societes (if not exists)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints 
        WHERE constraint_name = 'FK_users_societes_company_id' 
        AND table_name = 'users'
    ) THEN
        -- First, ensure all company_id values exist in societes
        INSERT INTO societes (id, name, type, country, created_at, updated_at)
        SELECT DISTINCT u.company_id, 'Company ' || u.company_id, 'company', 'MA', NOW(), NOW()
        FROM users u
        WHERE u.company_id IS NOT NULL 
        AND NOT EXISTS (SELECT 1 FROM societes s WHERE s.id = u.company_id)
        ON CONFLICT (id) DO NOTHING;
        
        ALTER TABLE users ADD CONSTRAINT "FK_users_societes_company_id" 
        FOREIGN KEY (company_id) REFERENCES societes(id);
    END IF;
END $$;

-- 3.3 Update vehicles foreign keys
DO $$
BEGIN
    -- Drop old FK to companies if exists
    ALTER TABLE vehicles DROP CONSTRAINT IF EXISTS "FK_vehicles_companies_company_id";
    
    -- Add FK to societes
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints 
        WHERE constraint_name = 'FK_vehicles_societes_company_id' 
        AND table_name = 'vehicles'
    ) THEN
        ALTER TABLE vehicles ADD CONSTRAINT "FK_vehicles_societes_company_id" 
        FOREIGN KEY (company_id) REFERENCES societes(id);
    END IF;
    
    -- Update driver references from employees to users
    ALTER TABLE vehicles DROP CONSTRAINT IF EXISTS "FK_vehicles_employees_assigned_driver_id";
    ALTER TABLE vehicles DROP CONSTRAINT IF EXISTS "FK_vehicles_employees_assigned_supervisor_id";
    
    -- Add FK to users for driver
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints 
        WHERE constraint_name = 'FK_vehicles_users_assigned_driver_id' 
        AND table_name = 'vehicles'
    ) THEN
        ALTER TABLE vehicles ADD CONSTRAINT "FK_vehicles_users_assigned_driver_id" 
        FOREIGN KEY (assigned_driver_id) REFERENCES users(id) ON DELETE SET NULL;
    END IF;
    
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints 
        WHERE constraint_name = 'FK_vehicles_users_assigned_supervisor_id' 
        AND table_name = 'vehicles'
    ) THEN
        ALTER TABLE vehicles ADD CONSTRAINT "FK_vehicles_users_assigned_supervisor_id" 
        FOREIGN KEY (assigned_supervisor_id) REFERENCES users(id) ON DELETE SET NULL;
    END IF;
END $$;

-- 3.4 Update other tables FK from companies to societes
DO $$
BEGIN
    ALTER TABLE gps_devices DROP CONSTRAINT IF EXISTS "FK_gps_devices_companies_company_id";
    ALTER TABLE geofences DROP CONSTRAINT IF EXISTS "FK_geofences_companies_company_id";
    ALTER TABLE maintenance_records DROP CONSTRAINT IF EXISTS "FK_maintenance_records_companies_company_id";
    ALTER TABLE vehicle_costs DROP CONSTRAINT IF EXISTS "FK_vehicle_costs_companies_company_id";
END $$;

-- ============================================================================
-- STEP 4: Remove Bird Flight columns (if they exist and are not needed)
-- ============================================================================

ALTER TABLE gps_positions DROP COLUMN IF EXISTS is_bird_flight;
ALTER TABLE gps_positions DROP COLUMN IF EXISTS bird_flight_reason;
ALTER TABLE gps_positions DROP COLUMN IF EXISTS implicit_speed_kph;

-- ============================================================================
-- STEP 5: Drop old tables (CAREFUL - backup first!)
-- ============================================================================

-- 5.1 Drop foreign keys referencing old tables first
ALTER TABLE driver_assignments DROP CONSTRAINT IF EXISTS "FK_driver_assignments_employees_DriverId";
ALTER TABLE driver_scores DROP CONSTRAINT IF EXISTS "FK_driver_scores_employees_DriverId";
ALTER TABLE driving_events DROP CONSTRAINT IF EXISTS "FK_driving_events_employees_DriverId";
ALTER TABLE fuel_records DROP CONSTRAINT IF EXISTS "FK_fuel_records_employees_driver_id";
ALTER TABLE vehicle_stops DROP CONSTRAINT IF EXISTS "FK_vehicle_stops_employees_driver_id";
ALTER TABLE trips DROP CONSTRAINT IF EXISTS "FK_trips_employees_DriverId";
ALTER TABLE reservations DROP CONSTRAINT IF EXISTS "FK_reservations_employees_AssignedDriverId";

-- 5.2 Backup and drop campaigns table
DROP TABLE IF EXISTS campaigns CASCADE;

-- 5.3 Backup and drop employees table (data migrated to users)
-- First create backup
CREATE TABLE IF NOT EXISTS _backup_employees_migration AS SELECT * FROM employees;
DROP TABLE IF EXISTS employees CASCADE;

-- 5.4 Backup and drop companies table (data migrated to societes)  
CREATE TABLE IF NOT EXISTS _backup_companies_migration AS SELECT * FROM companies;
DROP TABLE IF EXISTS companies CASCADE;

-- 5.5 Backup and drop subscriptions table (data migrated to subscription_types)
CREATE TABLE IF NOT EXISTS _backup_subscriptions_migration AS SELECT * FROM subscriptions;
DROP TABLE IF EXISTS subscriptions CASCADE;

-- ============================================================================
-- STEP 6: Create indexes for new tables
-- ============================================================================

CREATE INDEX IF NOT EXISTS ix_roles_societe_id ON roles(societe_id);
CREATE INDEX IF NOT EXISTS ix_geofence_groups_company_id ON geofence_groups(company_id);
CREATE INDEX IF NOT EXISTS ix_poi_visits_poi_id ON poi_visits(poi_id);
CREATE INDEX IF NOT EXISTS ix_poi_visits_vehicle_id ON poi_visits(vehicle_id);
CREATE INDEX IF NOT EXISTS ix_vehicle_user_assignments_vehicle_id ON vehicle_user_assignments(vehicle_id);
CREATE INDEX IF NOT EXISTS ix_vehicle_user_assignments_user_id ON vehicle_user_assignments(user_id);
CREATE INDEX IF NOT EXISTS ix_users_role_id ON users(role_id);

-- ============================================================================
-- STEP 7: Update EF Migrations History
-- ============================================================================

-- Record this migration
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260120210000_ManualProdToLocalSync', '9.0.1')
ON CONFLICT ("MigrationId") DO NOTHING;

-- Also record the RemoveBirdFlightFilter migration if it wasn't applied
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260107180000_RemoveBirdFlightFilter', '9.0.1')
ON CONFLICT ("MigrationId") DO NOTHING;

-- ============================================================================
-- DONE - Commit transaction
-- ============================================================================

-- COMMIT;

-- ============================================================================
-- VERIFICATION QUERIES (run after migration)
-- ============================================================================
/*
-- Check new tables exist
SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename IN 
('societes', 'subscription_types', 'roles', 'geofence_groups', 'poi_visits', 'vehicle_user_assignments');

-- Check users has new columns
SELECT column_name FROM information_schema.columns WHERE table_name = 'users' AND column_name IN 
('user_type', 'role_id', 'is_company_admin', 'date_of_birth', 'cin', 'hire_date', 'license_number', 'license_expiry');

-- Check old tables are dropped
SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename IN 
('companies', 'employees', 'subscriptions', 'campaigns');

-- Check data migration
SELECT COUNT(*) as societes_count FROM societes;
SELECT COUNT(*) as subscription_types_count FROM subscription_types;
SELECT COUNT(*) as roles_count FROM roles;

-- Check migrations history
SELECT * FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
*/
