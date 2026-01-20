-- ============================================================
-- CALYPSO MIGRATION SCRIPT v2.0
-- Date: 2026-01-16
-- Description: Migration from FleetTrack Pro to Calypso
-- ============================================================

-- STEP 0: CREATE BACKUP TABLES
DROP TABLE IF EXISTS _backup_companies CASCADE;
DROP TABLE IF EXISTS _backup_users CASCADE;
DROP TABLE IF EXISTS _backup_employees CASCADE;
DROP TABLE IF EXISTS _backup_campaigns CASCADE;

CREATE TABLE _backup_companies AS SELECT * FROM companies;
CREATE TABLE _backup_users AS SELECT * FROM users;
CREATE TABLE _backup_employees AS SELECT * FROM employees;
CREATE TABLE _backup_campaigns AS SELECT * FROM campaigns;

-- STEP 1: CREATE NEW ROLES TABLE
DROP TABLE IF EXISTS roles CASCADE;
CREATE TABLE roles (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description TEXT,
    role_type VARCHAR(50) NOT NULL DEFAULT 'employee',
    permissions JSONB NOT NULL DEFAULT '{}',
    societe_id INTEGER,
    is_system BOOLEAN DEFAULT FALSE,
    is_default BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX idx_roles_societe_id ON roles(societe_id);
CREATE INDEX idx_roles_role_type ON roles(role_type);

-- STEP 2: INSERT DEFAULT SYSTEM ROLES
INSERT INTO roles (name, description, role_type, permissions, is_system, is_default) VALUES
(
    'Administrateur Système',
    'Accès complet à toutes les fonctionnalités et sociétés',
    'system_admin',
    '{"modules": {"admin_panel": true, "manage_companies": true, "manage_subscriptions": true, "manage_all_users": true, "system_settings": true, "view_all_data": true}, "restrictions": {"assigned_vehicles_only": false}}',
    true,
    false
),
(
    'Chef de Société',
    'Gestion complète de sa société dans les limites de abonnement',
    'company_admin',
    '{"modules": {"dashboard": true, "monitoring": true, "vehicles": {"view": true, "create": true, "edit": true, "delete": true}, "users": {"view": true, "create": true, "edit": true, "delete": true}, "roles": {"view": true, "create": true, "edit": true, "delete": true}, "maintenance": {"view": true, "create": true, "edit": true, "delete": true}, "reports": {"view": true, "export": true}, "gps_playback": true, "geofences": {"view": true, "create": true, "edit": true, "delete": true}, "settings": true}, "restrictions": {"assigned_vehicles_only": false, "own_company_only": true}}',
    true,
    false
),
(
    'Employé',
    'Accès standard aux fonctionnalités de base',
    'employee',
    '{"modules": {"dashboard": true, "monitoring": true, "vehicles": {"view": true, "create": false, "edit": false, "delete": false}, "maintenance": {"view": true, "create": true, "edit": false, "delete": false}, "reports": {"view": true, "export": false}, "gps_playback": true, "geofences": {"view": true, "create": false, "edit": false, "delete": false}}, "restrictions": {"assigned_vehicles_only": true}}',
    true,
    true
);

-- STEP 3: DROP FOREIGN KEY CONSTRAINTS FROM companies
ALTER TABLE users DROP CONSTRAINT IF EXISTS "FK_users_companies_company_id";
ALTER TABLE vehicles DROP CONSTRAINT IF EXISTS "FK_vehicles_companies_company_id";
ALTER TABLE employees DROP CONSTRAINT IF EXISTS "FK_employees_companies_company_id";
ALTER TABLE geofences DROP CONSTRAINT IF EXISTS "FK_geofences_companies_company_id";
ALTER TABLE gps_devices DROP CONSTRAINT IF EXISTS "FK_gps_devices_companies_company_id";
ALTER TABLE maintenance_records DROP CONSTRAINT IF EXISTS "FK_maintenance_records_companies_company_id";
ALTER TABLE vehicle_costs DROP CONSTRAINT IF EXISTS "FK_vehicle_costs_companies_company_id";
ALTER TABLE audit_logs DROP CONSTRAINT IF EXISTS "FK_audit_logs_companies_CompanyId";
ALTER TABLE points_of_interest DROP CONSTRAINT IF EXISTS "FK_points_of_interest_companies_CompanyId";
ALTER TABLE report_schedules DROP CONSTRAINT IF EXISTS "FK_report_schedules_companies_CompanyId";
ALTER TABLE reports DROP CONSTRAINT IF EXISTS "FK_reports_companies_CompanyId";
ALTER TABLE suppliers DROP CONSTRAINT IF EXISTS "FK_suppliers_companies_CompanyId";
ALTER TABLE companies DROP CONSTRAINT IF EXISTS "companies_campaign_id_fkey";

-- STEP 4: RENAME companies TO societes
ALTER TABLE companies RENAME TO societes;
ALTER INDEX IF EXISTS "PK_companies" RENAME TO "PK_societes";
ALTER INDEX IF EXISTS "IX_companies_subscription_id" RENAME TO "IX_societes_subscription_id";

-- Add new columns to societes
ALTER TABLE societes ADD COLUMN IF NOT EXISTS description TEXT;
ALTER TABLE societes ADD COLUMN IF NOT EXISTS subscription_type_id INTEGER;
ALTER TABLE societes DROP COLUMN IF EXISTS campaign_id;

-- STEP 5: RECREATE FOREIGN KEY CONSTRAINTS TO societes
ALTER TABLE users ADD CONSTRAINT "FK_users_societes_company_id" 
    FOREIGN KEY (company_id) REFERENCES societes(id) ON DELETE CASCADE;
ALTER TABLE vehicles ADD CONSTRAINT "FK_vehicles_societes_company_id" 
    FOREIGN KEY (company_id) REFERENCES societes(id) ON DELETE CASCADE;
ALTER TABLE geofences ADD CONSTRAINT "FK_geofences_societes_company_id" 
    FOREIGN KEY (company_id) REFERENCES societes(id) ON DELETE CASCADE;
ALTER TABLE gps_devices ADD CONSTRAINT "FK_gps_devices_societes_company_id" 
    FOREIGN KEY (company_id) REFERENCES societes(id) ON DELETE CASCADE;
ALTER TABLE maintenance_records ADD CONSTRAINT "FK_maintenance_records_societes_company_id" 
    FOREIGN KEY (company_id) REFERENCES societes(id) ON DELETE CASCADE;
ALTER TABLE vehicle_costs ADD CONSTRAINT "FK_vehicle_costs_societes_company_id" 
    FOREIGN KEY (company_id) REFERENCES societes(id) ON DELETE CASCADE;
ALTER TABLE audit_logs ADD CONSTRAINT "FK_audit_logs_societes_CompanyId" 
    FOREIGN KEY ("CompanyId") REFERENCES societes(id);
ALTER TABLE points_of_interest ADD CONSTRAINT "FK_points_of_interest_societes_CompanyId" 
    FOREIGN KEY ("CompanyId") REFERENCES societes(id) ON DELETE CASCADE;
ALTER TABLE report_schedules ADD CONSTRAINT "FK_report_schedules_societes_CompanyId" 
    FOREIGN KEY ("CompanyId") REFERENCES societes(id) ON DELETE CASCADE;
ALTER TABLE reports ADD CONSTRAINT "FK_reports_societes_CompanyId" 
    FOREIGN KEY ("CompanyId") REFERENCES societes(id) ON DELETE CASCADE;
ALTER TABLE suppliers ADD CONSTRAINT "FK_suppliers_societes_CompanyId" 
    FOREIGN KEY ("CompanyId") REFERENCES societes(id) ON DELETE CASCADE;

ALTER TABLE societes ADD CONSTRAINT "FK_societes_subscription_types" 
    FOREIGN KEY (subscription_type_id) REFERENCES subscription_types(id);

ALTER TABLE roles ADD CONSTRAINT "FK_roles_societes" 
    FOREIGN KEY (societe_id) REFERENCES societes(id) ON DELETE CASCADE;

-- STEP 6: ADD NEW COLUMNS TO users FOR EMPLOYEE DATA
ALTER TABLE users ADD COLUMN IF NOT EXISTS phone VARCHAR(20);
ALTER TABLE users ADD COLUMN IF NOT EXISTS user_type VARCHAR(50) DEFAULT 'user';
ALTER TABLE users ADD COLUMN IF NOT EXISTS status VARCHAR(20) DEFAULT 'active';
ALTER TABLE users ADD COLUMN IF NOT EXISTS hire_date TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN IF NOT EXISTS license_number VARCHAR(100);
ALTER TABLE users ADD COLUMN IF NOT EXISTS license_expiry TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN IF NOT EXISTS role_id INTEGER;
ALTER TABLE users ADD COLUMN IF NOT EXISTS is_company_admin BOOLEAN DEFAULT FALSE;

ALTER TABLE users ADD CONSTRAINT "FK_users_roles" 
    FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE SET NULL;

-- STEP 7: MIGRATE EMPLOYEES TO USERS
INSERT INTO users (
    name, email, password_hash, company_id, roles, phone, user_type, 
    status, hire_date, license_number, license_expiry, created_at, updated_at, role_id
)
SELECT 
    e.name,
    COALESCE(e.email, 'employee_' || e.id || '@temp.local'),
    '$2a$11$rKPvzLKEHK5Z5zJZ5zJZ5uaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
    e.company_id,
    ARRAY['employee']::text[],
    e.phone,
    'employee',
    e.status,
    e.hire_date,
    e.license_number,
    e.license_expiry,
    e.created_at,
    e.updated_at,
    (SELECT id FROM roles WHERE role_type = 'employee' AND is_system = true LIMIT 1)
FROM employees e
WHERE NOT EXISTS (
    SELECT 1 FROM users u WHERE u.email = e.email AND e.email IS NOT NULL
);

-- STEP 8: CREATE MAPPING TABLE AND UPDATE FKs
CREATE TEMP TABLE employee_user_mapping AS
SELECT 
    e.id as old_employee_id,
    u.id as new_user_id
FROM _backup_employees e
JOIN users u ON (u.email = e.email OR u.email = 'employee_' || e.id || '@temp.local');

-- Update vehicles
UPDATE vehicles v SET assigned_driver_id = m.new_user_id
FROM employee_user_mapping m WHERE v.assigned_driver_id = m.old_employee_id;

UPDATE vehicles v SET assigned_supervisor_id = m.new_user_id
FROM employee_user_mapping m WHERE v.assigned_supervisor_id = m.old_employee_id;

-- Update driver_assignments
ALTER TABLE driver_assignments DROP CONSTRAINT IF EXISTS "FK_driver_assignments_employees_DriverId";
UPDATE driver_assignments d SET "DriverId" = m.new_user_id
FROM employee_user_mapping m WHERE d."DriverId" = m.old_employee_id;

-- Update driver_scores
ALTER TABLE driver_scores DROP CONSTRAINT IF EXISTS "FK_driver_scores_employees_DriverId";
UPDATE driver_scores d SET "DriverId" = m.new_user_id
FROM employee_user_mapping m WHERE d."DriverId" = m.old_employee_id;

-- Update driving_events
ALTER TABLE driving_events DROP CONSTRAINT IF EXISTS "FK_driving_events_employees_DriverId";
UPDATE driving_events d SET "DriverId" = m.new_user_id
FROM employee_user_mapping m WHERE d."DriverId" = m.old_employee_id;

-- Update fuel_records
ALTER TABLE fuel_records DROP CONSTRAINT IF EXISTS "FK_fuel_records_employees_driver_id";
UPDATE fuel_records f SET driver_id = m.new_user_id
FROM employee_user_mapping m WHERE f.driver_id = m.old_employee_id;

-- Update reservations
ALTER TABLE reservations DROP CONSTRAINT IF EXISTS "FK_reservations_employees_AssignedDriverId";
UPDATE reservations r SET "AssignedDriverId" = m.new_user_id
FROM employee_user_mapping m WHERE r."AssignedDriverId" = m.old_employee_id;

-- Update trips
ALTER TABLE trips DROP CONSTRAINT IF EXISTS "FK_trips_employees_DriverId";
UPDATE trips t SET "DriverId" = m.new_user_id
FROM employee_user_mapping m WHERE t."DriverId" = m.old_employee_id;

-- Update vehicle_stops
ALTER TABLE vehicle_stops DROP CONSTRAINT IF EXISTS "FK_vehicle_stops_employees_driver_id";
UPDATE vehicle_stops v SET driver_id = m.new_user_id
FROM employee_user_mapping m WHERE v.driver_id = m.old_employee_id;

-- STEP 9: RECREATE FK CONSTRAINTS TO users
ALTER TABLE vehicles DROP CONSTRAINT IF EXISTS "FK_vehicles_employees_assigned_driver_id";
ALTER TABLE vehicles DROP CONSTRAINT IF EXISTS "FK_vehicles_employees_assigned_supervisor_id";
ALTER TABLE vehicles ADD CONSTRAINT "FK_vehicles_users_assigned_driver_id" 
    FOREIGN KEY (assigned_driver_id) REFERENCES users(id) ON DELETE SET NULL;
ALTER TABLE vehicles ADD CONSTRAINT "FK_vehicles_users_assigned_supervisor_id" 
    FOREIGN KEY (assigned_supervisor_id) REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE driver_assignments ADD CONSTRAINT "FK_driver_assignments_users_DriverId" 
    FOREIGN KEY ("DriverId") REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE driver_scores ADD CONSTRAINT "FK_driver_scores_users_DriverId" 
    FOREIGN KEY ("DriverId") REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE driving_events ADD CONSTRAINT "FK_driving_events_users_DriverId" 
    FOREIGN KEY ("DriverId") REFERENCES users(id);
ALTER TABLE fuel_records ADD CONSTRAINT "FK_fuel_records_users_driver_id" 
    FOREIGN KEY (driver_id) REFERENCES users(id);
ALTER TABLE reservations ADD CONSTRAINT "FK_reservations_users_AssignedDriverId" 
    FOREIGN KEY ("AssignedDriverId") REFERENCES users(id);
ALTER TABLE trips ADD CONSTRAINT "FK_trips_users_DriverId" 
    FOREIGN KEY ("DriverId") REFERENCES users(id);
ALTER TABLE vehicle_stops ADD CONSTRAINT "FK_vehicle_stops_users_driver_id" 
    FOREIGN KEY (driver_id) REFERENCES users(id);

-- STEP 10: UPDATE ADMIN USER
UPDATE users 
SET role_id = (SELECT id FROM roles WHERE role_type = 'system_admin' LIMIT 1),
    user_type = 'admin',
    is_company_admin = false
WHERE 'admin' = ANY(roles);

-- STEP 11: ADD PERMISSIONS TO subscription_types
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS permissions JSONB DEFAULT '{}';

UPDATE subscription_types SET permissions = '{"modules": {"monitoring_gps": false, "playback_history": true, "reports_gps": true, "vehicle_management": true, "maintenance": true, "repairs": true, "geofences": true, "alerts": true, "fuel_analysis": false, "driving_behavior": false, "api_access": false}}'::jsonb WHERE code = 'parc';

UPDATE subscription_types SET permissions = '{"modules": {"monitoring_gps": true, "playback_history": true, "reports_gps": true, "vehicle_management": true, "maintenance": true, "repairs": true, "geofences": true, "alerts": true, "fuel_analysis": true, "driving_behavior": false, "api_access": false}}'::jsonb WHERE code = 'parc_gps';

UPDATE subscription_types SET permissions = '{"modules": {"monitoring_gps": true, "playback_history": true, "reports_gps": true, "vehicle_management": true, "maintenance": true, "repairs": true, "geofences": true, "alerts": true, "fuel_analysis": true, "driving_behavior": true, "api_access": true}}'::jsonb WHERE code = 'enterprise';

-- STEP 12: DROP campaigns TABLE
DROP TABLE IF EXISTS campaigns CASCADE;

-- STEP 13: RENAME employees TABLE (keep as backup)
ALTER TABLE IF EXISTS employees RENAME TO _deprecated_employees;

-- STEP 14: CREATE PERFORMANCE INDEXES
CREATE INDEX IF NOT EXISTS idx_users_company_id ON users(company_id);
CREATE INDEX IF NOT EXISTS idx_users_role_id ON users(role_id);
CREATE INDEX IF NOT EXISTS idx_users_user_type ON users(user_type);
CREATE INDEX IF NOT EXISTS idx_societes_subscription_type_id ON societes(subscription_type_id);

-- DONE!
SELECT 'CALYPSO MIGRATION COMPLETED SUCCESSFULLY' as status;
