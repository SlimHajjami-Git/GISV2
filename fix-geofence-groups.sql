-- Create geofence_groups table
CREATE TABLE IF NOT EXISTS geofence_groups (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    color VARCHAR(20) DEFAULT '#6b7280',
    icon_name VARCHAR(100),
    societe_id INTEGER REFERENCES societes(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);

-- Add group_id column to geofences if missing
DO $$ 
BEGIN 
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='geofences' AND column_name='group_id') THEN
        ALTER TABLE geofences ADD COLUMN group_id INTEGER REFERENCES geofence_groups(id) ON DELETE SET NULL;
    END IF;
END $$;

-- Create vehicle_costs table if missing
CREATE TABLE IF NOT EXISTS vehicle_costs (
    id SERIAL PRIMARY KEY,
    vehicle_id INTEGER REFERENCES vehicles(id) ON DELETE CASCADE,
    societe_id INTEGER REFERENCES societes(id) ON DELETE CASCADE,
    cost_type VARCHAR(50) NOT NULL,
    amount DECIMAL(10,2) NOT NULL,
    currency VARCHAR(3) DEFAULT 'MAD',
    description TEXT,
    cost_date DATE NOT NULL,
    odometer_km INTEGER,
    vendor VARCHAR(255),
    invoice_number VARCHAR(100),
    notes TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ
);

-- Create index for vehicle_costs
CREATE INDEX IF NOT EXISTS idx_vehicle_costs_vehicle_id ON vehicle_costs(vehicle_id);
CREATE INDEX IF NOT EXISTS idx_vehicle_costs_cost_date ON vehicle_costs(cost_date);
