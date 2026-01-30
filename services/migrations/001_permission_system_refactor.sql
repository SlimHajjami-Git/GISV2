-- ============================================================================
-- Migration: Permission System Refactor
-- Date: 2026-01-29
-- Description: Clean up subscription_types, add granular report permissions,
--              migrate users to role_id system
-- ============================================================================

BEGIN;

-- ============================================================================
-- STEP 1: Clean up subscription_types - Remove camelCase duplicate columns
-- ============================================================================

-- First, ensure snake_case columns have the data from camelCase if they're null
UPDATE subscription_types SET 
    gps_tracking = COALESCE(gps_tracking, gpstracking, false),
    gps_installation = COALESCE(gps_installation, gpsinstallation, false),
    api_access = COALESCE(api_access, apiaccess, false),
    advanced_reports = COALESCE(advanced_reports, advancedreports, false),
    real_time_alerts = COALESCE(real_time_alerts, realtimealerts, true),
    history_playback = COALESCE(history_playback, historyplayback, true),
    fuel_analysis = COALESCE(fuel_analysis, fuelanalysis, false),
    driving_behavior = COALESCE(driving_behavior, drivingbehavior, false),
    history_retention_days = COALESCE(history_retention_days, historyretentiondays, 30),
    max_vehicles = COALESCE(max_vehicles, maxvehicles, 10),
    max_users = COALESCE(max_users, maxusers, 5),
    max_gps_devices = COALESCE(max_gps_devices, maxgpsdevices, 10),
    max_geofences = COALESCE(max_geofences, maxgeofences, 20),
    monthly_price = COALESCE(monthly_price, monthlyprice, 0),
    quarterly_price = COALESCE(quarterly_price, quarterlyprice, 0),
    yearly_price = COALESCE(yearly_price, yearlyprice, 0),
    monthly_duration_days = COALESCE(monthly_duration_days, monthlydurationdays, 30),
    quarterly_duration_days = COALESCE(quarterly_duration_days, quarterlydurationdays, 90),
    yearly_duration_days = COALESCE(yearly_duration_days, yearlydurationdays, 365),
    sort_order = COALESCE(sort_order, sortorder, 0),
    is_active = COALESCE(is_active, isactive, true),
    target_company_type = COALESCE(target_company_type, targetcompanytype, 'all'),
    created_at = COALESCE(created_at, createdat, NOW()),
    updated_at = COALESCE(updated_at, updatedat, NOW());

-- Drop camelCase columns
ALTER TABLE subscription_types 
    DROP COLUMN IF EXISTS gpstracking,
    DROP COLUMN IF EXISTS gpsinstallation,
    DROP COLUMN IF EXISTS apiaccess,
    DROP COLUMN IF EXISTS advancedreports,
    DROP COLUMN IF EXISTS realtimealerts,
    DROP COLUMN IF EXISTS historyplayback,
    DROP COLUMN IF EXISTS fuelanalysis,
    DROP COLUMN IF EXISTS drivingbehavior,
    DROP COLUMN IF EXISTS historyretentiondays,
    DROP COLUMN IF EXISTS maxvehicles,
    DROP COLUMN IF EXISTS maxusers,
    DROP COLUMN IF EXISTS maxgpsdevices,
    DROP COLUMN IF EXISTS maxgeofences,
    DROP COLUMN IF EXISTS monthlyprice,
    DROP COLUMN IF EXISTS quarterlyprice,
    DROP COLUMN IF EXISTS yearlyprice,
    DROP COLUMN IF EXISTS monthlydurationdays,
    DROP COLUMN IF EXISTS quarterlydurationdays,
    DROP COLUMN IF EXISTS yearlydurationdays,
    DROP COLUMN IF EXISTS sortorder,
    DROP COLUMN IF EXISTS isactive,
    DROP COLUMN IF EXISTS targetcompanytype,
    DROP COLUMN IF EXISTS createdat,
    DROP COLUMN IF EXISTS updatedat;

-- ============================================================================
-- STEP 2: Add granular report permissions to subscription_types
-- ============================================================================

-- Add individual report type columns
ALTER TABLE subscription_types 
    ADD COLUMN IF NOT EXISTS report_trips BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS report_fuel BOOLEAN DEFAULT false,
    ADD COLUMN IF NOT EXISTS report_speed BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS report_stops BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS report_mileage BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS report_costs BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS report_maintenance BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS report_daily BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS report_monthly BOOLEAN DEFAULT false,
    ADD COLUMN IF NOT EXISTS report_mileage_period BOOLEAN DEFAULT false,
    ADD COLUMN IF NOT EXISTS report_speed_infraction BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS report_driving_behavior BOOLEAN DEFAULT false;

-- Set default report permissions based on existing features
UPDATE subscription_types SET
    report_trips = gps_tracking,
    report_fuel = fuel_analysis,
    report_speed = gps_tracking,
    report_stops = gps_tracking,
    report_mileage = gps_tracking,
    report_costs = true,
    report_maintenance = true,
    report_daily = gps_tracking,
    report_monthly = advanced_reports,
    report_mileage_period = advanced_reports,
    report_speed_infraction = gps_tracking,
    report_driving_behavior = driving_behavior;

-- ============================================================================
-- STEP 3: Add module permissions to subscription_types
-- ============================================================================

ALTER TABLE subscription_types
    ADD COLUMN IF NOT EXISTS module_dashboard BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS module_monitoring BOOLEAN DEFAULT false,
    ADD COLUMN IF NOT EXISTS module_vehicles BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS module_employees BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS module_geofences BOOLEAN DEFAULT false,
    ADD COLUMN IF NOT EXISTS module_maintenance BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS module_costs BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS module_reports BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS module_settings BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS module_users BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS module_suppliers BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS module_documents BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS module_accidents BOOLEAN DEFAULT true,
    ADD COLUMN IF NOT EXISTS module_fleet_management BOOLEAN DEFAULT false;

-- Set default module permissions based on existing features
UPDATE subscription_types SET
    module_dashboard = true,
    module_monitoring = gps_tracking,
    module_vehicles = true,
    module_employees = true,
    module_geofences = gps_tracking,
    module_maintenance = true,
    module_costs = true,
    module_reports = true,
    module_settings = true,
    module_users = true,
    module_suppliers = true,
    module_documents = true,
    module_accidents = true,
    module_fleet_management = gps_tracking;

-- ============================================================================
-- STEP 4: Ensure roles table has proper structure
-- ============================================================================

-- Add module_permissions JSONB column to roles for granular control
ALTER TABLE roles 
    ADD COLUMN IF NOT EXISTS module_permissions JSONB DEFAULT '{}';

-- Update existing system roles with proper module_permissions
UPDATE roles SET module_permissions = '{
    "dashboard": true, "monitoring": true, "vehicles": true, "employees": true,
    "geofences": true, "maintenance": true, "costs": true, "reports": true,
    "settings": true, "users": true, "suppliers": true, "documents": true,
    "accidents": true, "fleet_management": true
}'::jsonb WHERE id = 1; -- Administrateur

UPDATE roles SET module_permissions = '{
    "dashboard": true, "monitoring": true, "vehicles": true, "employees": true,
    "geofences": false, "maintenance": true, "costs": true, "reports": true,
    "settings": false, "users": false, "suppliers": true, "documents": true,
    "accidents": false, "fleet_management": false
}'::jsonb WHERE id = 2; -- Gestionnaire de flotte

UPDATE roles SET module_permissions = '{
    "dashboard": true, "monitoring": true, "vehicles": false, "employees": false,
    "geofences": false, "maintenance": false, "costs": false, "reports": false,
    "settings": false, "users": false, "suppliers": false, "documents": false,
    "accidents": false, "fleet_management": false
}'::jsonb WHERE id = 3; -- Conducteur

-- ============================================================================
-- STEP 5: Migrate users from legacy roles[] to role_id system
-- ============================================================================

-- Update users with 'super_admin' or 'platform_admin' in roles array
UPDATE users SET 
    user_type = 'platform_admin',
    is_company_admin = true
WHERE 'super_admin' = ANY(roles) OR 'platform_admin' = ANY(roles);

-- Update users with 'admin' in roles array (company admins)
UPDATE users SET 
    user_type = 'company_admin',
    is_company_admin = true,
    role_id = 1 -- Administrateur role
WHERE 'admin' = ANY(roles) 
  AND NOT ('super_admin' = ANY(roles) OR 'platform_admin' = ANY(roles));

-- Update remaining users as employees
UPDATE users SET 
    user_type = 'employee',
    is_company_admin = false,
    role_id = COALESCE(role_id, 2) -- Default to Gestionnaire de flotte
WHERE role_id IS NULL 
  AND user_type NOT IN ('platform_admin', 'company_admin');

-- ============================================================================
-- STEP 6: Create view for effective user permissions
-- ============================================================================

CREATE OR REPLACE VIEW user_effective_permissions AS
SELECT 
    u.id AS user_id,
    u.name AS user_name,
    u.email,
    u.company_id,
    u.user_type,
    u.is_company_admin,
    u.role_id,
    r.name AS role_name,
    r.role_type,
    r.module_permissions AS role_permissions,
    s.name AS company_name,
    st.id AS subscription_type_id,
    st.name AS subscription_name,
    -- Module access (role permissions AND subscription permissions)
    jsonb_build_object(
        'dashboard', COALESCE((r.module_permissions->>'dashboard')::boolean, false) AND COALESCE(st.module_dashboard, false),
        'monitoring', COALESCE((r.module_permissions->>'monitoring')::boolean, false) AND COALESCE(st.module_monitoring, false),
        'vehicles', COALESCE((r.module_permissions->>'vehicles')::boolean, false) AND COALESCE(st.module_vehicles, false),
        'employees', COALESCE((r.module_permissions->>'employees')::boolean, false) AND COALESCE(st.module_employees, false),
        'geofences', COALESCE((r.module_permissions->>'geofences')::boolean, false) AND COALESCE(st.module_geofences, false),
        'maintenance', COALESCE((r.module_permissions->>'maintenance')::boolean, false) AND COALESCE(st.module_maintenance, false),
        'costs', COALESCE((r.module_permissions->>'costs')::boolean, false) AND COALESCE(st.module_costs, false),
        'reports', COALESCE((r.module_permissions->>'reports')::boolean, false) AND COALESCE(st.module_reports, false),
        'settings', COALESCE((r.module_permissions->>'settings')::boolean, false) AND COALESCE(st.module_settings, false),
        'users', COALESCE((r.module_permissions->>'users')::boolean, false) AND COALESCE(st.module_users, false),
        'suppliers', COALESCE((r.module_permissions->>'suppliers')::boolean, false) AND COALESCE(st.module_suppliers, false),
        'documents', COALESCE((r.module_permissions->>'documents')::boolean, false) AND COALESCE(st.module_documents, false),
        'accidents', COALESCE((r.module_permissions->>'accidents')::boolean, false) AND COALESCE(st.module_accidents, false),
        'fleet_management', COALESCE((r.module_permissions->>'fleet_management')::boolean, false) AND COALESCE(st.module_fleet_management, false)
    ) AS effective_modules,
    -- Report access
    jsonb_build_object(
        'trips', st.report_trips,
        'fuel', st.report_fuel,
        'speed', st.report_speed,
        'stops', st.report_stops,
        'mileage', st.report_mileage,
        'costs', st.report_costs,
        'maintenance', st.report_maintenance,
        'daily', st.report_daily,
        'monthly', st.report_monthly,
        'mileage_period', st.report_mileage_period,
        'speed_infraction', st.report_speed_infraction,
        'driving_behavior', st.report_driving_behavior
    ) AS available_reports
FROM users u
LEFT JOIN roles r ON u.role_id = r.id
LEFT JOIN societes s ON u.company_id = s.id
LEFT JOIN subscription_types st ON s.subscription_type_id = st.id;

-- ============================================================================
-- STEP 7: Create indexes for performance
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_users_role_id ON users(role_id);
CREATE INDEX IF NOT EXISTS idx_users_user_type ON users(user_type);
CREATE INDEX IF NOT EXISTS idx_users_company_admin ON users(is_company_admin) WHERE is_company_admin = true;

COMMIT;

-- ============================================================================
-- Verification queries (run manually to check migration success)
-- ============================================================================
-- SELECT * FROM user_effective_permissions LIMIT 5;
-- SELECT column_name FROM information_schema.columns WHERE table_name = 'subscription_types' ORDER BY ordinal_position;
-- SELECT id, name, user_type, is_company_admin, role_id FROM users;
