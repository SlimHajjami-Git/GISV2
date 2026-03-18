-- Migration: Add company_id column to maintenance_logs table
-- The maintenance_logs table was created without company_id, causing dashboard expense queries to crash
-- This migration adds the column and populates it from the related vehicle's company_id

-- 1. Add column (nullable first to allow backfill)
ALTER TABLE maintenance_logs ADD COLUMN IF NOT EXISTS company_id INTEGER;

-- 2. Backfill from vehicles table
UPDATE maintenance_logs ml
SET company_id = v.company_id
FROM vehicles v
WHERE ml.vehicle_id = v.id
AND ml.company_id IS NULL;

-- 3. Set NOT NULL after backfill
ALTER TABLE maintenance_logs ALTER COLUMN company_id SET NOT NULL;

-- 4. Add foreign key constraint
ALTER TABLE maintenance_logs
ADD CONSTRAINT fk_maintenance_logs_company
FOREIGN KEY (company_id) REFERENCES societes(id) ON DELETE CASCADE;

-- 5. Add index for performance
CREATE INDEX IF NOT EXISTS ix_maintenance_logs_company_id ON maintenance_logs(company_id);
