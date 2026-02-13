-- Add per-user module permission columns to users table
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS access_level VARCHAR(20) NOT NULL DEFAULT 'user',
    ADD COLUMN IF NOT EXISTS can_monitoring BOOLEAN NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS can_vehicles BOOLEAN NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS can_drivers BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_reports BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_geofences BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_maintenance BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_costs BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_documents BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_accidents BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_users BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_settings BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_suppliers BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_fleet_management BOOLEAN NOT NULL DEFAULT false;

-- Grant all permissions to existing admin users
UPDATE users
SET access_level = 'admin',
    can_monitoring = true,
    can_vehicles = true,
    can_drivers = true,
    can_reports = true,
    can_geofences = true,
    can_maintenance = true,
    can_costs = true,
    can_documents = true,
    can_accidents = true,
    can_users = true,
    can_settings = true,
    can_suppliers = true,
    can_fleet_management = true
WHERE role_id IN (SELECT id FROM roles WHERE is_company_admin = true OR is_system_role = true);
