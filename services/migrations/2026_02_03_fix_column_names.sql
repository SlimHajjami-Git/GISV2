-- ============================================================
-- FIX: Aligner colonnes BDD avec entités C#
-- Date: 2026-02-03
-- ============================================================

-- ============================================================
-- REPAIRS: Renommer company_id -> societe_id, ajouter reference
-- ============================================================

-- Renommer la colonne company_id en societe_id
ALTER TABLE repairs RENAME COLUMN company_id TO societe_id;

-- Renommer description en notes (si pas déjà fait)
-- ALTER TABLE repairs RENAME COLUMN notes TO description;

-- Ajouter la colonne reference
ALTER TABLE repairs ADD COLUMN IF NOT EXISTS reference VARCHAR(100);

-- Renommer odometer_km en mileage_at_repair
ALTER TABLE repairs RENAME COLUMN odometer_km TO mileage_at_repair;

-- Ajouter repair_parts table
CREATE TABLE IF NOT EXISTS repair_parts (
    id SERIAL PRIMARY KEY,
    repair_id INTEGER NOT NULL REFERENCES repairs(id) ON DELETE CASCADE,
    part_name VARCHAR(200) NOT NULL,
    part_reference VARCHAR(100),
    quantity INTEGER DEFAULT 1,
    unit_price DECIMAL(10,2) DEFAULT 0,
    subtotal DECIMAL(10,2) DEFAULT 0,
    notes VARCHAR(500),
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_repair_parts_repair_id ON repair_parts(repair_id);

-- ============================================================
-- FUEL_ENTRIES: Renommer colonnes pour correspondre à FuelEntry.cs
-- Note: L'entité FuelEntry.cs utilise des noms différents
-- On garde les colonnes SQL et on ajuste la configuration EF
-- ============================================================

-- Ajouter colonnes manquantes pour FuelEntry
ALTER TABLE fuel_entries ADD COLUMN IF NOT EXISTS vehicle_plate VARCHAR(20);
ALTER TABLE fuel_entries ADD COLUMN IF NOT EXISTS fuel_type_id INTEGER;
ALTER TABLE fuel_entries ADD COLUMN IF NOT EXISTS driver_id INTEGER REFERENCES users(id) ON DELETE SET NULL;

-- Renommer pour correspondre à l'entité
ALTER TABLE fuel_entries RENAME COLUMN liters TO volume;
ALTER TABLE fuel_entries RENAME COLUMN entry_date TO invoice_date;
ALTER TABLE fuel_entries RENAME COLUMN total_cost TO total_amount;
ALTER TABLE fuel_entries RENAME COLUMN receipt_number TO invoice_number;

-- ============================================================
-- VÉRIFICATION
-- ============================================================

-- Afficher les colonnes de repairs
SELECT 'repairs' as table_name, column_name FROM information_schema.columns WHERE table_name = 'repairs' ORDER BY ordinal_position;

-- Afficher les colonnes de fuel_entries
SELECT 'fuel_entries' as table_name, column_name FROM information_schema.columns WHERE table_name = 'fuel_entries' ORDER BY ordinal_position;
