-- Migration: Add missing columns to vehicles table for Production
-- Date: 2026-01-30
-- Description: Add new vehicle columns that don't exist in production
-- SAFE: Uses IF NOT EXISTS pattern

-- Helper function to add column if not exists
DO $$
BEGIN
    -- DepartmentId
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'vehicles' AND column_name = 'DepartmentId') THEN
        ALTER TABLE vehicles ADD COLUMN "DepartmentId" INTEGER NULL;
        RAISE NOTICE 'Added DepartmentId column';
    END IF;

    -- FuelType
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'vehicles' AND column_name = 'FuelType') THEN
        ALTER TABLE vehicles ADD COLUMN "FuelType" VARCHAR(50) NULL;
        RAISE NOTICE 'Added FuelType column';
    END IF;

    -- SpeedLimit
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'vehicles' AND column_name = 'SpeedLimit') THEN
        ALTER TABLE vehicles ADD COLUMN "SpeedLimit" INTEGER NULL;
        RAISE NOTICE 'Added SpeedLimit column';
    END IF;

    -- InsuranceExpiry
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'vehicles' AND column_name = 'InsuranceExpiry') THEN
        ALTER TABLE vehicles ADD COLUMN "InsuranceExpiry" TIMESTAMP NULL;
        RAISE NOTICE 'Added InsuranceExpiry column';
    END IF;

    -- RegistrationExpiry
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'vehicles' AND column_name = 'RegistrationExpiry') THEN
        ALTER TABLE vehicles ADD COLUMN "RegistrationExpiry" TIMESTAMP NULL;
        RAISE NOTICE 'Added RegistrationExpiry column';
    END IF;

    -- TaxExpiry
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'vehicles' AND column_name = 'TaxExpiry') THEN
        ALTER TABLE vehicles ADD COLUMN "TaxExpiry" TIMESTAMP NULL;
        RAISE NOTICE 'Added TaxExpiry column';
    END IF;

    -- TechnicalInspectionExpiry
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'vehicles' AND column_name = 'TechnicalInspectionExpiry') THEN
        ALTER TABLE vehicles ADD COLUMN "TechnicalInspectionExpiry" TIMESTAMP NULL;
        RAISE NOTICE 'Added TechnicalInspectionExpiry column';
    END IF;

    -- TransportPermitExpiry
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'vehicles' AND column_name = 'TransportPermitExpiry') THEN
        ALTER TABLE vehicles ADD COLUMN "TransportPermitExpiry" TIMESTAMP NULL;
        RAISE NOTICE 'Added TransportPermitExpiry column';
    END IF;
END $$;

-- Verify
SELECT column_name, data_type FROM information_schema.columns 
WHERE table_name = 'vehicles' 
AND column_name IN ('DepartmentId', 'FuelType', 'SpeedLimit', 'InsuranceExpiry', 'RegistrationExpiry', 'TaxExpiry', 'TechnicalInspectionExpiry', 'TransportPermitExpiry')
ORDER BY column_name;
