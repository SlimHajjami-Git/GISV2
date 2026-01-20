-- ============================================================================
-- FIX: subscription_types column names (camelCase -> snake_case)
-- Date: 2026-01-20
-- ============================================================================

-- Renommer les colonnes camelCase vers snake_case (si elles existent)
DO $$
BEGIN
    -- advanced_reports
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'advancedreports') THEN
        ALTER TABLE subscription_types RENAME COLUMN advancedreports TO advanced_reports;
    END IF;
    
    -- api_access
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'apiaccess') THEN
        ALTER TABLE subscription_types RENAME COLUMN apiaccess TO api_access;
    END IF;
    
    -- driving_behavior
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'drivingbehavior') THEN
        ALTER TABLE subscription_types RENAME COLUMN drivingbehavior TO driving_behavior;
    END IF;
    
    -- fuel_analysis
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'fuelanalysis') THEN
        ALTER TABLE subscription_types RENAME COLUMN fuelanalysis TO fuel_analysis;
    END IF;
    
    -- gps_installation
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'gpsinstallation') THEN
        ALTER TABLE subscription_types RENAME COLUMN gpsinstallation TO gps_installation;
    END IF;
    
    -- gps_tracking
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'gpstracking') THEN
        ALTER TABLE subscription_types RENAME COLUMN gpstracking TO gps_tracking;
    END IF;
    
    -- history_playback
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'historyplayback') THEN
        ALTER TABLE subscription_types RENAME COLUMN historyplayback TO history_playback;
    END IF;
    
    -- history_retention_days
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'historyretentiondays') THEN
        ALTER TABLE subscription_types RENAME COLUMN historyretentiondays TO history_retention_days;
    END IF;
    
    -- is_active
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'isactive') THEN
        ALTER TABLE subscription_types RENAME COLUMN isactive TO is_active;
    END IF;
    
    -- max_geofences
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'maxgeofences') THEN
        ALTER TABLE subscription_types RENAME COLUMN maxgeofences TO max_geofences;
    END IF;
    
    -- max_gps_devices
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'maxgpsdevices') THEN
        ALTER TABLE subscription_types RENAME COLUMN maxgpsdevices TO max_gps_devices;
    END IF;
    
    -- max_users
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'maxusers') THEN
        ALTER TABLE subscription_types RENAME COLUMN maxusers TO max_users;
    END IF;
    
    -- max_vehicles
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'maxvehicles') THEN
        ALTER TABLE subscription_types RENAME COLUMN maxvehicles TO max_vehicles;
    END IF;
    
    -- monthly_duration_days
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'monthlydurationdays') THEN
        ALTER TABLE subscription_types RENAME COLUMN monthlydurationdays TO monthly_duration_days;
    END IF;
    
    -- monthly_price
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'monthlyprice') THEN
        ALTER TABLE subscription_types RENAME COLUMN monthlyprice TO monthly_price;
    END IF;
    
    -- quarterly_duration_days
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'quarterlydurationdays') THEN
        ALTER TABLE subscription_types RENAME COLUMN quarterlydurationdays TO quarterly_duration_days;
    END IF;
    
    -- quarterly_price
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'quarterlyprice') THEN
        ALTER TABLE subscription_types RENAME COLUMN quarterlyprice TO quarterly_price;
    END IF;
    
    -- real_time_alerts
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'realtimealerts') THEN
        ALTER TABLE subscription_types RENAME COLUMN realtimealerts TO real_time_alerts;
    END IF;
    
    -- sort_order
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'sortorder') THEN
        ALTER TABLE subscription_types RENAME COLUMN sortorder TO sort_order;
    END IF;
    
    -- target_company_type
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'targetcompanytype') THEN
        ALTER TABLE subscription_types RENAME COLUMN targetcompanytype TO target_company_type;
    END IF;
    
    -- yearly_duration_days
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'yearlydurationdays') THEN
        ALTER TABLE subscription_types RENAME COLUMN yearlydurationdays TO yearly_duration_days;
    END IF;
    
    -- yearly_price
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'yearlyprice') THEN
        ALTER TABLE subscription_types RENAME COLUMN yearlyprice TO yearly_price;
    END IF;
    
    -- created_at
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'createdat') THEN
        ALTER TABLE subscription_types RENAME COLUMN createdat TO created_at;
    END IF;
    
    -- updated_at
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'updatedat') THEN
        ALTER TABLE subscription_types RENAME COLUMN updatedat TO updated_at;
    END IF;
END $$;

-- Ajouter les colonnes manquantes
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

-- Vérification
SELECT 'subscription_types columns fixed!' as status;
