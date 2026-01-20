-- ============================================================================
-- CREATE PLATFORM SUPER ADMIN
-- Date: 2026-01-20
-- Description: Creates a platform-level admin that can manage all companies
-- ============================================================================

-- 1. First, fix subscription_types columns if needed
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'advancedreports') THEN
        ALTER TABLE subscription_types RENAME COLUMN advancedreports TO advanced_reports;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'apiaccess') THEN
        ALTER TABLE subscription_types RENAME COLUMN apiaccess TO api_access;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'gpstracking') THEN
        ALTER TABLE subscription_types RENAME COLUMN gpstracking TO gps_tracking;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'gpsinstallation') THEN
        ALTER TABLE subscription_types RENAME COLUMN gpsinstallation TO gps_installation;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'maxvehicles') THEN
        ALTER TABLE subscription_types RENAME COLUMN maxvehicles TO max_vehicles;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'maxusers') THEN
        ALTER TABLE subscription_types RENAME COLUMN maxusers TO max_users;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'maxgpsdevices') THEN
        ALTER TABLE subscription_types RENAME COLUMN maxgpsdevices TO max_gps_devices;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'maxgeofences') THEN
        ALTER TABLE subscription_types RENAME COLUMN maxgeofences TO max_geofences;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'isactive') THEN
        ALTER TABLE subscription_types RENAME COLUMN isactive TO is_active;
    END IF;
END $$;

-- Add missing columns to subscription_types
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS access_rights JSONB;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS target_company_type TEXT NOT NULL DEFAULT 'all';
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS monthly_duration_days INTEGER NOT NULL DEFAULT 30;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS quarterly_duration_days INTEGER NOT NULL DEFAULT 90;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS yearly_duration_days INTEGER NOT NULL DEFAULT 365;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS monthly_price NUMERIC(10,2) NOT NULL DEFAULT 0;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS quarterly_price NUMERIC(10,2) NOT NULL DEFAULT 0;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS yearly_price NUMERIC(10,2) NOT NULL DEFAULT 0;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS driving_behavior BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS fuel_analysis BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS history_playback BOOLEAN NOT NULL DEFAULT true;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS real_time_alerts BOOLEAN NOT NULL DEFAULT true;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS advanced_reports BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS api_access BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS gps_tracking BOOLEAN NOT NULL DEFAULT true;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS gps_installation BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS history_retention_days INTEGER NOT NULL DEFAULT 90;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS is_active BOOLEAN NOT NULL DEFAULT true;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS sort_order INTEGER NOT NULL DEFAULT 0;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS max_vehicles INTEGER NOT NULL DEFAULT 10;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS max_users INTEGER NOT NULL DEFAULT 5;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS max_gps_devices INTEGER NOT NULL DEFAULT 10;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS max_geofences INTEGER NOT NULL DEFAULT 20;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

-- 2. Create Platform Unlimited subscription type (if not exists)
INSERT INTO subscription_types (
    name, code, description, target_company_type,
    monthly_price, quarterly_price, yearly_price,
    monthly_duration_days, quarterly_duration_days, yearly_duration_days,
    max_vehicles, max_users, max_gps_devices, max_geofences,
    gps_tracking, gps_installation, api_access, advanced_reports,
    real_time_alerts, history_playback, fuel_analysis, driving_behavior,
    history_retention_days, is_active, sort_order,
    created_at, updated_at
) VALUES (
    'Platform Unlimited', 'platform-unlimited', 'Unlimited access for platform administrators', 'all',
    0, 0, 0,
    36500, 36500, 36500,
    999999, 999999, 999999, 999999,
    true, true, true, true,
    true, true, true, true,
    36500, true, 0,
    NOW(), NOW()
) ON CONFLICT (code) DO UPDATE SET
    max_vehicles = 999999,
    max_users = 999999,
    max_gps_devices = 999999,
    max_geofences = 999999,
    history_retention_days = 36500,
    updated_at = NOW();

-- 3. Create Platform Admin company (societe) if not exists
INSERT INTO societes (
    name, type, address, city, country, phone, email,
    subscription_type_id, is_active, subscription_expires_at,
    created_at, updated_at
) 
SELECT 
    'Platform Admin', 'platform', 'System', 'System', 'MA', '', 'platform@gis.ma',
    (SELECT id FROM subscription_types WHERE code = 'platform-unlimited' LIMIT 1),
    true, '2099-12-31'::timestamp,
    NOW(), NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM societes WHERE name = 'Platform Admin'
);

-- 4. Create the Super Admin user
-- Password: Admin@GIS2026!
-- BCrypt hash generated for: Admin@GIS2026!
DO $$
DECLARE
    v_company_id INTEGER;
    v_user_id INTEGER;
BEGIN
    -- Get Platform Admin company ID
    SELECT id INTO v_company_id FROM societes WHERE name = 'Platform Admin' LIMIT 1;
    
    IF v_company_id IS NULL THEN
        RAISE EXCEPTION 'Platform Admin company not found';
    END IF;
    
    -- Check if admin already exists
    SELECT id INTO v_user_id FROM users WHERE email = 'admin@belive.tn';
    
    IF v_user_id IS NOT NULL THEN
        -- Update existing admin
        UPDATE users SET
            name = 'Super Admin',
            password_hash = '$2a$11$SSFnYGBW31exSV45qir0KutzLHPu3BJo/ZPVfzUlJtPe30T.9g9Rm',
            roles = ARRAY['super_admin', 'platform_admin'],
            permissions = ARRAY['all', 'admin', 'manage_companies', 'manage_subscriptions', 'manage_users', 'manage_vehicles', 'manage_devices', 'manage_geofences', 'view_reports', 'manage_settings'],
            status = 'active',
            user_type = 'platform_admin',
            is_company_admin = true,
            company_id = v_company_id,
            updated_at = NOW()
        WHERE id = v_user_id;
        
        RAISE NOTICE 'Updated existing admin user (Id: %)', v_user_id;
    ELSE
        -- Create new admin
        INSERT INTO users (
            name, email, phone, password_hash,
            roles, permissions, assigned_vehicle_ids,
            status, user_type, is_company_admin,
            company_id, created_at, updated_at
        ) VALUES (
            'Super Admin',
            'admin@belive.tn',
            '+212 600 000000',
            '$2a$11$SSFnYGBW31exSV45qir0KutzLHPu3BJo/ZPVfzUlJtPe30T.9g9Rm',
            ARRAY['super_admin', 'platform_admin'],
            ARRAY['all', 'admin', 'manage_companies', 'manage_subscriptions', 'manage_users', 'manage_vehicles', 'manage_devices', 'manage_geofences', 'view_reports', 'manage_settings'],
            ARRAY[]::integer[],
            'active',
            'platform_admin',
            true,
            v_company_id,
            NOW(), NOW()
        ) RETURNING id INTO v_user_id;
        
        RAISE NOTICE 'Created new admin user (Id: %)', v_user_id;
    END IF;
END $$;

-- 5. Verification
SELECT '=== PLATFORM ADMIN SETUP COMPLETE ===' as status;
SELECT 'Subscription Type:' as info, id, name, code, max_vehicles FROM subscription_types WHERE code = 'platform-unlimited';
SELECT 'Company:' as info, id, name, type, is_active FROM societes WHERE name = 'Platform Admin';
SELECT 'Admin User:' as info, id, name, email, user_type, roles FROM users WHERE email = 'admin@belive.tn';

SELECT '' as info;
SELECT '========================================' as info;
SELECT 'LOGIN CREDENTIALS:' as info;
SELECT 'Email: admin@belive.tn' as info;
SELECT 'Password: Admin@GIS2026!' as info;
SELECT '========================================' as info;
