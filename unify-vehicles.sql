-- =====================================================
-- UNIFY VEHICLES TABLES - Migrate to single 'vehicles' table
-- =====================================================

-- Step 1: Add missing columns to existing 'vehicles' table
ALTER TABLE vehicles 
    ADD COLUMN IF NOT EXISTS type VARCHAR(50) DEFAULT 'camion',
    ADD COLUMN IF NOT EXISTS year INTEGER DEFAULT 2024,
    ADD COLUMN IF NOT EXISTS status VARCHAR(20) DEFAULT 'available',
    ADD COLUMN IF NOT EXISTS has_gps BOOLEAN DEFAULT false,
    ADD COLUMN IF NOT EXISTS mileage INTEGER DEFAULT 0,
    ADD COLUMN IF NOT EXISTS rental_mileage INTEGER DEFAULT 0,
    ADD COLUMN IF NOT EXISTS company_id INTEGER,
    ADD COLUMN IF NOT EXISTS assigned_driver_id INTEGER,
    ADD COLUMN IF NOT EXISTS assigned_supervisor_id INTEGER,
    ADD COLUMN IF NOT EXISTS gps_device_id INTEGER;

-- Step 2: Add foreign key constraints
-- First check if employees table exists (from EF Core)
DO $$
BEGIN
    -- Add FK to Employees for driver
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'Employees') THEN
        IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_name = 'fk_vehicles_driver') THEN
            ALTER TABLE vehicles ADD CONSTRAINT fk_vehicles_driver 
                FOREIGN KEY (assigned_driver_id) REFERENCES "Employees"("Id") ON DELETE SET NULL;
        END IF;
        
        IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_name = 'fk_vehicles_supervisor') THEN
            ALTER TABLE vehicles ADD CONSTRAINT fk_vehicles_supervisor 
                FOREIGN KEY (assigned_supervisor_id) REFERENCES "Employees"("Id") ON DELETE SET NULL;
        END IF;
    END IF;
    
    -- Add FK to Companies
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'Companies') THEN
        IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_name = 'fk_vehicles_company') THEN
            ALTER TABLE vehicles ADD CONSTRAINT fk_vehicles_company 
                FOREIGN KEY (company_id) REFERENCES "Companies"("Id") ON DELETE CASCADE;
        END IF;
    END IF;
    
    -- Add FK to GpsDevices
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'GpsDevices') THEN
        IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_name = 'fk_vehicles_gps_device') THEN
            ALTER TABLE vehicles ADD CONSTRAINT fk_vehicles_gps_device 
                FOREIGN KEY (gps_device_id) REFERENCES "GpsDevices"("Id") ON DELETE SET NULL;
        END IF;
    END IF;
END $$;

-- Step 3: Update existing vehicles with default company (company_id = 2 for Test Corp)
UPDATE vehicles SET company_id = 2 WHERE company_id IS NULL;

-- Step 4: Link vehicles to devices (update has_gps based on devices table)
UPDATE vehicles v SET has_gps = true 
WHERE EXISTS (SELECT 1 FROM devices d WHERE d.vehicle_id = v.id);

-- Step 5: Create indexes for performance
CREATE INDEX IF NOT EXISTS idx_vehicles_company_id ON vehicles(company_id);
CREATE INDEX IF NOT EXISTS idx_vehicles_plate_number ON vehicles(plate_number);
CREATE INDEX IF NOT EXISTS idx_vehicles_status ON vehicles(status);

-- Step 6: Migrate data from "Vehicles" (EF Core) to vehicles if any new data exists
INSERT INTO vehicles (name, plate_number, brand, model, color, type, year, status, has_gps, mileage, company_id, assigned_driver_id, assigned_supervisor_id, gps_device_id, created_at, updated_at)
SELECT 
    "Name", 
    "Plate", 
    "Brand", 
    "Model", 
    "Color",
    "Type",
    "Year",
    "Status",
    "HasGps",
    "Mileage",
    "CompanyId",
    "AssignedDriverId",
    "AssignedSupervisorId",
    "GpsDeviceId",
    "CreatedAt",
    "UpdatedAt"
FROM "Vehicles" v
WHERE NOT EXISTS (SELECT 1 FROM vehicles vv WHERE vv.plate_number = v."Plate")
  AND "Plate" NOT LIKE 'TEST%';

-- Step 7: Drop the duplicate "Vehicles" table
DROP TABLE IF EXISTS "Vehicles" CASCADE;

-- Verify results
SELECT 'Vehicles unified successfully!' as status;
SELECT id, name, plate_number, type, status, company_id, has_gps FROM vehicles ORDER BY id;
