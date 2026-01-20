DO $$ 
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'advancedreports') THEN
        ALTER TABLE subscription_types RENAME COLUMN advancedreports TO advanced_reports;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'apiaccess') THEN
        ALTER TABLE subscription_types RENAME COLUMN apiaccess TO api_access;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'realtimealerts') THEN
        ALTER TABLE subscription_types RENAME COLUMN realtimealerts TO real_time_alerts;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'historyplayback') THEN
        ALTER TABLE subscription_types RENAME COLUMN historyplayback TO history_playback;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'fuelanalysis') THEN
        ALTER TABLE subscription_types RENAME COLUMN fuelanalysis TO fuel_analysis;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'drivingbehavior') THEN
        ALTER TABLE subscription_types RENAME COLUMN drivingbehavior TO driving_behavior;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'historyretentiondays') THEN
        ALTER TABLE subscription_types RENAME COLUMN historyretentiondays TO history_retention_days;
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
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'targetcompanytype') THEN
        ALTER TABLE subscription_types RENAME COLUMN targetcompanytype TO target_company_type;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'monthlyprice') THEN
        ALTER TABLE subscription_types RENAME COLUMN monthlyprice TO monthly_price;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'quarterlyprice') THEN
        ALTER TABLE subscription_types RENAME COLUMN quarterlyprice TO quarterly_price;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'yearlyprice') THEN
        ALTER TABLE subscription_types RENAME COLUMN yearlyprice TO yearly_price;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'monthlydurationdays') THEN
        ALTER TABLE subscription_types RENAME COLUMN monthlydurationdays TO monthly_duration_days;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'quarterlydurationdays') THEN
        ALTER TABLE subscription_types RENAME COLUMN quarterlydurationdays TO quarterly_duration_days;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'yearlydurationdays') THEN
        ALTER TABLE subscription_types RENAME COLUMN yearlydurationdays TO yearly_duration_days;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'sortorder') THEN
        ALTER TABLE subscription_types RENAME COLUMN sortorder TO sort_order;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'isactive') THEN
        ALTER TABLE subscription_types RENAME COLUMN isactive TO is_active;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'accessrights') THEN
        ALTER TABLE subscription_types RENAME COLUMN accessrights TO access_rights;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'createdat') THEN
        ALTER TABLE subscription_types RENAME COLUMN createdat TO created_at;
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'updatedat') THEN
        ALTER TABLE subscription_types RENAME COLUMN updatedat TO updated_at;
    END IF;
    RAISE NOTICE 'Column renaming complete';
END $$;
