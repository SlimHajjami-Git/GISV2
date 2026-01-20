-- ============================================
-- CALYPSO - Migration: Subscription Entity Removal
-- ============================================
-- This migration:
-- 1. Removes subscription_id from societes table (now uses subscription_type_id only)
-- 2. Drops the subscriptions table (replaced by subscription_types)
-- 3. Sets default values for monthly/quarterly fields (keeping columns for backward compatibility)
-- 4. Removes unique constraint on code if exists
-- 5. Adds TaxId column to societes if missing

-- ============================================
-- STEP 0: Remove Subscription entity references
-- ============================================

-- 0a. Migrate any societes using subscription_id to subscription_type_id
DO $$ 
BEGIN
    -- Check if subscription_id column exists
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'societes' AND column_name = 'subscription_id') THEN
        -- Update societes to use subscription_type_id based on subscription's subscription_type_id
        UPDATE societes s
        SET subscription_type_id = sub.subscription_type_id
        FROM subscriptions sub
        WHERE s.subscription_id = sub.id 
          AND s.subscription_type_id IS NULL
          AND sub.subscription_type_id IS NOT NULL;
        
        -- For any remaining societes without subscription_type_id, set to first active subscription type
        -- Try to find any subscription type (active or not) to avoid leaving societes without a type
        UPDATE societes
        SET subscription_type_id = (SELECT id FROM subscription_types ORDER BY id LIMIT 1)
        WHERE subscription_type_id IS NULL;
    END IF;
END $$;

-- 0b. Drop foreign key constraints on subscription_id if exists
DO $$ 
DECLARE
    constraint_rec RECORD;
BEGIN
    -- Find and drop all foreign key constraints related to subscription_id
    FOR constraint_rec IN 
        SELECT constraint_name 
        FROM information_schema.table_constraints 
        WHERE table_name = 'societes'
        AND constraint_type = 'FOREIGN KEY'
        AND (constraint_name LIKE '%subscription%' OR constraint_name LIKE '%Subscription%')
    LOOP
        EXECUTE 'ALTER TABLE societes DROP CONSTRAINT IF EXISTS ' || quote_ident(constraint_rec.constraint_name);
        RAISE NOTICE 'Dropped constraint: %', constraint_rec.constraint_name;
    END LOOP;
END $$;

-- 0c. Drop subscription_id column from societes
DO $$ 
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'societes' AND column_name = 'subscription_id') THEN
        ALTER TABLE societes DROP COLUMN subscription_id;
        RAISE NOTICE 'Dropped subscription_id column from societes';
    END IF;
END $$;

-- 0d. Drop subscriptions table
DO $$ 
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'subscriptions') THEN
        DROP TABLE subscriptions CASCADE;
        RAISE NOTICE 'Dropped subscriptions table';
    END IF;
END $$;

-- 1. Update existing subscription_types to use yearly price for all
UPDATE subscription_types 
SET 
    monthly_price = COALESCE(yearly_price / 12, 0),
    quarterly_price = COALESCE(yearly_price / 4, 0),
    monthly_duration_days = 30,
    quarterly_duration_days = 90,
    sort_order = 0
WHERE monthly_price IS NULL OR monthly_price = 0;

-- 2. Set defaults for future inserts (columns kept for backward compatibility)
ALTER TABLE subscription_types 
    ALTER COLUMN monthly_price SET DEFAULT 0,
    ALTER COLUMN quarterly_price SET DEFAULT 0,
    ALTER COLUMN monthly_duration_days SET DEFAULT 30,
    ALTER COLUMN quarterly_duration_days SET DEFAULT 90,
    ALTER COLUMN sort_order SET DEFAULT 0;

-- 3. Drop unique constraint on code if exists (code will be auto-generated from name)
DO $$ 
BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ix_subscription_types_code') THEN
        ALTER TABLE subscription_types DROP CONSTRAINT ix_subscription_types_code;
    END IF;
    
    -- Also check for index
    IF EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'ix_subscription_types_code') THEN
        DROP INDEX ix_subscription_types_code;
    END IF;
END $$;

-- 4. Ensure TaxId exists in societes table
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'societes' AND column_name = 'TaxId') THEN
        ALTER TABLE societes ADD COLUMN "TaxId" VARCHAR(50);
    END IF;
END $$;

-- 5. Add comment for documentation
COMMENT ON COLUMN subscription_types.monthly_price IS 'Deprecated: All subscriptions are now annual. Kept for backward compatibility.';
COMMENT ON COLUMN subscription_types.quarterly_price IS 'Deprecated: All subscriptions are now annual. Kept for backward compatibility.';
COMMENT ON COLUMN subscription_types.sort_order IS 'Deprecated: Display order removed from UI. Kept for backward compatibility.';
COMMENT ON COLUMN subscription_types.code IS 'Auto-generated from name. No longer user-editable.';

-- 6. Verify societes has all required columns
DO $$ 
BEGIN
    -- Ensure RC column exists
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'societes' AND column_name = 'RC') THEN
        ALTER TABLE societes ADD COLUMN "RC" VARCHAR(50);
    END IF;
    
    -- Ensure IF column exists  
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'societes' AND column_name = 'IF') THEN
        ALTER TABLE societes ADD COLUMN "IF" VARCHAR(50);
    END IF;
END $$;

COMMENT ON COLUMN societes."TaxId" IS 'Numéro de taxe de la société';
COMMENT ON COLUMN societes."RC" IS 'Registre de Commerce';
COMMENT ON COLUMN societes."IF" IS 'Identifiant Fiscal';

-- ============================================
-- STEP 7: Fix subscription_types column names (snake_case)
-- ============================================
-- Rename columns from camelCase/nocase to snake_case to match EF Core expectations

DO $$ 
BEGIN
    -- advanced_reports
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'advancedreports') THEN
        ALTER TABLE subscription_types RENAME COLUMN advancedreports TO advanced_reports;
    END IF;
    
    -- real_time_alerts
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'realtimealerts') THEN
        ALTER TABLE subscription_types RENAME COLUMN realtimealerts TO real_time_alerts;
    END IF;
    
    -- history_playback
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'historyplayback') THEN
        ALTER TABLE subscription_types RENAME COLUMN historyplayback TO history_playback;
    END IF;
    
    -- fuel_analysis
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'fuelanalysis') THEN
        ALTER TABLE subscription_types RENAME COLUMN fuelanalysis TO fuel_analysis;
    END IF;
    
    -- driving_behavior
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'drivingbehavior') THEN
        ALTER TABLE subscription_types RENAME COLUMN drivingbehavior TO driving_behavior;
    END IF;
    
    -- history_retention_days
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'historyretentiondays') THEN
        ALTER TABLE subscription_types RENAME COLUMN historyretentiondays TO history_retention_days;
    END IF;
    
    -- gps_tracking
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'gpstracking') THEN
        ALTER TABLE subscription_types RENAME COLUMN gpstracking TO gps_tracking;
    END IF;
    
    -- gps_installation
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'gpsinstallation') THEN
        ALTER TABLE subscription_types RENAME COLUMN gpsinstallation TO gps_installation;
    END IF;
    
    -- api_access
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'apiaccess') THEN
        ALTER TABLE subscription_types RENAME COLUMN apiaccess TO api_access;
    END IF;
    
    -- max_vehicles
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'maxvehicles') THEN
        ALTER TABLE subscription_types RENAME COLUMN maxvehicles TO max_vehicles;
    END IF;
    
    -- max_users
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'maxusers') THEN
        ALTER TABLE subscription_types RENAME COLUMN maxusers TO max_users;
    END IF;
    
    -- max_gps_devices
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'maxgpsdevices') THEN
        ALTER TABLE subscription_types RENAME COLUMN maxgpsdevices TO max_gps_devices;
    END IF;
    
    -- max_geofences
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'maxgeofences') THEN
        ALTER TABLE subscription_types RENAME COLUMN maxgeofences TO max_geofences;
    END IF;
    
    -- target_company_type
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'targetcompanytype') THEN
        ALTER TABLE subscription_types RENAME COLUMN targetcompanytype TO target_company_type;
    END IF;
    
    -- monthly_price
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'monthlyprice') THEN
        ALTER TABLE subscription_types RENAME COLUMN monthlyprice TO monthly_price;
    END IF;
    
    -- quarterly_price
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'quarterlyprice') THEN
        ALTER TABLE subscription_types RENAME COLUMN quarterlyprice TO quarterly_price;
    END IF;
    
    -- yearly_price
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'yearlyprice') THEN
        ALTER TABLE subscription_types RENAME COLUMN yearlyprice TO yearly_price;
    END IF;
    
    -- monthly_duration_days
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'monthlydurationdays') THEN
        ALTER TABLE subscription_types RENAME COLUMN monthlydurationdays TO monthly_duration_days;
    END IF;
    
    -- quarterly_duration_days
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'quarterlydurationdays') THEN
        ALTER TABLE subscription_types RENAME COLUMN quarterlydurationdays TO quarterly_duration_days;
    END IF;
    
    -- yearly_duration_days
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'yearlydurationdays') THEN
        ALTER TABLE subscription_types RENAME COLUMN yearlydurationdays TO yearly_duration_days;
    END IF;
    
    -- sort_order
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'sortorder') THEN
        ALTER TABLE subscription_types RENAME COLUMN sortorder TO sort_order;
    END IF;
    
    -- is_active
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'isactive') THEN
        ALTER TABLE subscription_types RENAME COLUMN isactive TO is_active;
    END IF;
    
    -- access_rights
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'accessrights') THEN
        ALTER TABLE subscription_types RENAME COLUMN accessrights TO access_rights;
    END IF;
    
    -- created_at
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'createdat') THEN
        ALTER TABLE subscription_types RENAME COLUMN createdat TO created_at;
    END IF;
    
    -- updated_at
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'updatedat') THEN
        ALTER TABLE subscription_types RENAME COLUMN updatedat TO updated_at;
    END IF;
    
    RAISE NOTICE 'Column renaming complete for subscription_types';
END $$;
