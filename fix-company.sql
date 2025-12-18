-- Fix company ID for migrated vehicles (assign to company 1 which is the test user's company)
-- First, check what companies exist
SELECT "Id", "Name" FROM "Companies";

-- Check what users exist
SELECT "Id", "Name", "Email", "CompanyId" FROM "Users";

-- Update migrated vehicles to use the correct company (the one with the test user)
UPDATE "Vehicles" SET "CompanyId" = (SELECT "CompanyId" FROM "Users" WHERE "Email" = 'test@example.com' LIMIT 1)
WHERE "Plate" NOT LIKE 'TEST%';

-- Verify
SELECT "Id", "Name", "Plate", "CompanyId" FROM "Vehicles";
