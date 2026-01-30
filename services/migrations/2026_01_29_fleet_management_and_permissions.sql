-- ============================================================================
-- Migration: Fleet Management & Module Permissions
-- Date: 2026-01-29
-- Description: Adds fleet management tables, module/report permissions to 
--              subscription_types, and role permissions
-- ============================================================================

BEGIN;

-- ============================================================================
-- 1. DEPARTMENTS TABLE
-- ============================================================================
CREATE TABLE IF NOT EXISTS departments (
    "Id" SERIAL PRIMARY KEY,
    "CompanyId" INTEGER NOT NULL,
    "Name" VARCHAR(100) NOT NULL,
    "Description" VARCHAR(500),
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_departments_companyid ON departments ("CompanyId");

-- ============================================================================
-- 2. FUEL TYPES TABLE
-- ============================================================================
CREATE TABLE IF NOT EXISTS fuel_types (
    "Id" SERIAL PRIMARY KEY,
    "Code" VARCHAR(20) NOT NULL UNIQUE,
    "Name" VARCHAR(50) NOT NULL,
    "IsSystem" BOOLEAN NOT NULL DEFAULT TRUE
);

-- Insert default fuel types if not exist
INSERT INTO fuel_types ("Code", "Name", "IsSystem") 
VALUES 
    ('diesel', 'Diesel', TRUE),
    ('essence', 'Essence', TRUE),
    ('gpl', 'GPL', TRUE),
    ('electrique', 'Électrique', TRUE)
ON CONFLICT ("Code") DO NOTHING;

-- ============================================================================
-- 3. FUEL PRICING TABLE
-- ============================================================================
CREATE TABLE IF NOT EXISTS fuel_pricing (
    "Id" SERIAL PRIMARY KEY,
    "CompanyId" INTEGER NOT NULL,
    "FuelTypeId" INTEGER NOT NULL REFERENCES fuel_types("Id"),
    "PricePerLiter" NUMERIC(10,3) NOT NULL,
    "EffectiveFrom" TIMESTAMP WITH TIME ZONE NOT NULL,
    "EffectiveTo" TIMESTAMP WITH TIME ZONE,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_fuel_pricing_companyid ON fuel_pricing ("CompanyId");
CREATE INDEX IF NOT EXISTS ix_fuel_pricing_fueltypeid ON fuel_pricing ("FuelTypeId");

-- ============================================================================
-- 4. SPEED LIMIT ALERTS TABLE
-- ============================================================================
CREATE TABLE IF NOT EXISTS speed_limit_alerts (
    "Id" SERIAL PRIMARY KEY,
    "VehicleId" INTEGER NOT NULL,
    "CompanyId" INTEGER NOT NULL,
    "SpeedLimit" INTEGER NOT NULL,
    "ActualSpeed" INTEGER NOT NULL,
    "Latitude" DOUBLE PRECISION NOT NULL,
    "Longitude" DOUBLE PRECISION NOT NULL,
    "Address" VARCHAR(500),
    "RecordedAt" TIMESTAMP WITH TIME ZONE NOT NULL,
    "IsAcknowledged" BOOLEAN NOT NULL DEFAULT FALSE,
    "AcknowledgedAt" TIMESTAMP WITH TIME ZONE,
    "AcknowledgedById" INTEGER,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_speed_limit_alerts_companyid ON speed_limit_alerts ("CompanyId");
CREATE INDEX IF NOT EXISTS ix_speed_limit_alerts_vehicleid ON speed_limit_alerts ("VehicleId");

-- ============================================================================
-- 5. VEHICLES TABLE - Add DepartmentId, SpeedLimit, FuelType columns
-- ============================================================================
DO $$ 
BEGIN
    -- Add DepartmentId column if not exists
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'vehicles' AND column_name = 'DepartmentId'
    ) THEN
        ALTER TABLE vehicles ADD COLUMN "DepartmentId" INTEGER;
    END IF;

    -- Add SpeedLimit column if not exists
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'vehicles' AND column_name = 'SpeedLimit'
    ) THEN
        ALTER TABLE vehicles ADD COLUMN "SpeedLimit" INTEGER DEFAULT 120;
    END IF;

    -- Add FuelType column if not exists
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'vehicles' AND column_name = 'FuelType'
    ) THEN
        ALTER TABLE vehicles ADD COLUMN "FuelType" VARCHAR(20) DEFAULT 'diesel';
    END IF;
END $$;

-- ============================================================================
-- 6. SUBSCRIPTION_TYPES - Add Module Permissions
-- ============================================================================
DO $$ 
BEGIN
    -- Module permissions
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_dashboard') THEN
        ALTER TABLE subscription_types ADD COLUMN module_dashboard BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_monitoring') THEN
        ALTER TABLE subscription_types ADD COLUMN module_monitoring BOOLEAN DEFAULT FALSE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_vehicles') THEN
        ALTER TABLE subscription_types ADD COLUMN module_vehicles BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_employees') THEN
        ALTER TABLE subscription_types ADD COLUMN module_employees BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_geofences') THEN
        ALTER TABLE subscription_types ADD COLUMN module_geofences BOOLEAN DEFAULT FALSE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_maintenance') THEN
        ALTER TABLE subscription_types ADD COLUMN module_maintenance BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_costs') THEN
        ALTER TABLE subscription_types ADD COLUMN module_costs BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_reports') THEN
        ALTER TABLE subscription_types ADD COLUMN module_reports BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_settings') THEN
        ALTER TABLE subscription_types ADD COLUMN module_settings BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_users') THEN
        ALTER TABLE subscription_types ADD COLUMN module_users BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_suppliers') THEN
        ALTER TABLE subscription_types ADD COLUMN module_suppliers BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_documents') THEN
        ALTER TABLE subscription_types ADD COLUMN module_documents BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_accidents') THEN
        ALTER TABLE subscription_types ADD COLUMN module_accidents BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'module_fleet_management') THEN
        ALTER TABLE subscription_types ADD COLUMN module_fleet_management BOOLEAN DEFAULT FALSE;
    END IF;
END $$;

-- ============================================================================
-- 7. SUBSCRIPTION_TYPES - Add Report Permissions
-- ============================================================================
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'report_trips') THEN
        ALTER TABLE subscription_types ADD COLUMN report_trips BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'report_fuel') THEN
        ALTER TABLE subscription_types ADD COLUMN report_fuel BOOLEAN DEFAULT FALSE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'report_speed') THEN
        ALTER TABLE subscription_types ADD COLUMN report_speed BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'report_stops') THEN
        ALTER TABLE subscription_types ADD COLUMN report_stops BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'report_mileage') THEN
        ALTER TABLE subscription_types ADD COLUMN report_mileage BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'report_costs') THEN
        ALTER TABLE subscription_types ADD COLUMN report_costs BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'report_maintenance') THEN
        ALTER TABLE subscription_types ADD COLUMN report_maintenance BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'report_daily') THEN
        ALTER TABLE subscription_types ADD COLUMN report_daily BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'report_monthly') THEN
        ALTER TABLE subscription_types ADD COLUMN report_monthly BOOLEAN DEFAULT FALSE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'report_mileage_period') THEN
        ALTER TABLE subscription_types ADD COLUMN report_mileage_period BOOLEAN DEFAULT FALSE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'report_speed_infraction') THEN
        ALTER TABLE subscription_types ADD COLUMN report_speed_infraction BOOLEAN DEFAULT TRUE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name = 'report_driving_behavior') THEN
        ALTER TABLE subscription_types ADD COLUMN report_driving_behavior BOOLEAN DEFAULT FALSE;
    END IF;
END $$;

-- ============================================================================
-- 8. ROLES TABLE - Add ModulePermissions JSONB column
-- ============================================================================
DO $$ 
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_name = 'roles' AND column_name = 'module_permissions'
    ) THEN
        ALTER TABLE roles ADD COLUMN module_permissions JSONB;
    END IF;
END $$;

-- ============================================================================
-- 9. UPDATE EXISTING SUBSCRIPTION TYPES with module permissions
-- ============================================================================
UPDATE subscription_types SET
    module_dashboard = TRUE,
    module_monitoring = CASE WHEN gps_tracking THEN TRUE ELSE FALSE END,
    module_vehicles = TRUE,
    module_employees = TRUE,
    module_geofences = CASE WHEN max_geofences > 0 THEN TRUE ELSE FALSE END,
    module_maintenance = TRUE,
    module_costs = TRUE,
    module_reports = TRUE,
    module_settings = TRUE,
    module_users = TRUE,
    module_suppliers = TRUE,
    module_documents = TRUE,
    module_accidents = TRUE,
    module_fleet_management = CASE WHEN code IN ('premium', 'enterprise') THEN TRUE ELSE FALSE END,
    report_trips = TRUE,
    report_fuel = CASE WHEN fuel_analysis THEN TRUE ELSE FALSE END,
    report_speed = TRUE,
    report_stops = TRUE,
    report_mileage = TRUE,
    report_costs = TRUE,
    report_maintenance = TRUE,
    report_daily = TRUE,
    report_monthly = CASE WHEN advanced_reports THEN TRUE ELSE FALSE END,
    report_mileage_period = CASE WHEN advanced_reports THEN TRUE ELSE FALSE END,
    report_speed_infraction = TRUE,
    report_driving_behavior = CASE WHEN driving_behavior THEN TRUE ELSE FALSE END
WHERE module_dashboard IS NULL OR module_monitoring IS NULL;

COMMIT;

-- ============================================================================
-- 10. BRANDS TABLE
-- ============================================================================
CREATE TABLE IF NOT EXISTS brands (
    "Id" SERIAL PRIMARY KEY,
    "Name" VARCHAR(100) NOT NULL,
    "LogoUrl" VARCHAR(500),
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_brands_name ON brands ("Name");

-- Insert common vehicle brands
INSERT INTO brands ("Name", "IsActive") 
VALUES 
    ('Peugeot', TRUE),
    ('Renault', TRUE),
    ('Citroën', TRUE),
    ('Volkswagen', TRUE),
    ('Mercedes-Benz', TRUE),
    ('BMW', TRUE),
    ('Audi', TRUE),
    ('Toyota', TRUE),
    ('Ford', TRUE),
    ('Fiat', TRUE),
    ('Hyundai', TRUE),
    ('Kia', TRUE),
    ('Nissan', TRUE),
    ('Dacia', TRUE),
    ('Opel', TRUE)
ON CONFLICT DO NOTHING;

-- ============================================================================
-- 11. VEHICLE_MODELS TABLE
-- ============================================================================
CREATE TABLE IF NOT EXISTS vehicle_models (
    "Id" SERIAL PRIMARY KEY,
    "BrandId" INTEGER NOT NULL REFERENCES brands("Id") ON DELETE CASCADE,
    "Name" VARCHAR(100) NOT NULL,
    "VehicleType" VARCHAR(50),
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_vehicle_models_brandid ON vehicle_models ("BrandId");

-- Insert common models for some brands
INSERT INTO vehicle_models ("BrandId", "Name", "VehicleType", "IsActive")
SELECT b."Id", m.model_name, m.vehicle_type, TRUE
FROM brands b
CROSS JOIN (VALUES
    ('Peugeot', '208', 'citadine'),
    ('Peugeot', '308', 'citadine'),
    ('Peugeot', '3008', 'suv'),
    ('Peugeot', '5008', 'suv'),
    ('Peugeot', 'Partner', 'utilitaire'),
    ('Peugeot', 'Expert', 'utilitaire'),
    ('Peugeot', 'Boxer', 'camion'),
    ('Renault', 'Clio', 'citadine'),
    ('Renault', 'Megane', 'citadine'),
    ('Renault', 'Captur', 'suv'),
    ('Renault', 'Kadjar', 'suv'),
    ('Renault', 'Kangoo', 'utilitaire'),
    ('Renault', 'Trafic', 'utilitaire'),
    ('Renault', 'Master', 'camion'),
    ('Citroën', 'C3', 'citadine'),
    ('Citroën', 'C4', 'citadine'),
    ('Citroën', 'C5 Aircross', 'suv'),
    ('Citroën', 'Berlingo', 'utilitaire'),
    ('Citroën', 'Jumpy', 'utilitaire'),
    ('Citroën', 'Jumper', 'camion'),
    ('Volkswagen', 'Golf', 'citadine'),
    ('Volkswagen', 'Polo', 'citadine'),
    ('Volkswagen', 'Tiguan', 'suv'),
    ('Volkswagen', 'Caddy', 'utilitaire'),
    ('Volkswagen', 'Transporter', 'utilitaire'),
    ('Volkswagen', 'Crafter', 'camion'),
    ('Mercedes-Benz', 'Classe A', 'citadine'),
    ('Mercedes-Benz', 'Classe C', 'citadine'),
    ('Mercedes-Benz', 'GLA', 'suv'),
    ('Mercedes-Benz', 'Vito', 'utilitaire'),
    ('Mercedes-Benz', 'Sprinter', 'camion'),
    ('Toyota', 'Yaris', 'citadine'),
    ('Toyota', 'Corolla', 'citadine'),
    ('Toyota', 'RAV4', 'suv'),
    ('Toyota', 'Hilux', 'camion'),
    ('Ford', 'Fiesta', 'citadine'),
    ('Ford', 'Focus', 'citadine'),
    ('Ford', 'Kuga', 'suv'),
    ('Ford', 'Transit', 'utilitaire'),
    ('Ford', 'Ranger', 'camion'),
    ('Dacia', 'Sandero', 'citadine'),
    ('Dacia', 'Duster', 'suv'),
    ('Dacia', 'Dokker', 'utilitaire'),
    ('Fiat', '500', 'citadine'),
    ('Fiat', 'Panda', 'citadine'),
    ('Fiat', 'Tipo', 'citadine'),
    ('Fiat', 'Doblo', 'utilitaire'),
    ('Fiat', 'Ducato', 'camion')
) AS m(brand_name, model_name, vehicle_type)
WHERE b."Name" = m.brand_name
ON CONFLICT DO NOTHING;

COMMIT;

-- ============================================================================
-- VERIFICATION QUERIES (run manually to verify)
-- ============================================================================
-- SELECT COUNT(*) FROM departments;
-- SELECT COUNT(*) FROM fuel_types;
-- SELECT COUNT(*) FROM fuel_pricing;
-- SELECT COUNT(*) FROM speed_limit_alerts;
-- SELECT COUNT(*) FROM brands;
-- SELECT COUNT(*) FROM vehicle_models;
-- SELECT column_name FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name LIKE 'module_%';
-- SELECT column_name FROM information_schema.columns WHERE table_name = 'subscription_types' AND column_name LIKE 'report_%';
