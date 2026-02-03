-- ============================================================
-- FIX: Ajouter colonnes manquantes à vehicles
-- Date: 2026-02-03
-- Erreur: column v.fuel_tank_capacity does not exist
-- ============================================================

-- Ajouter fuel_tank_capacity à vehicles
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS fuel_tank_capacity INTEGER;

-- Vérification
SELECT column_name FROM information_schema.columns 
WHERE table_name = 'vehicles' AND column_name = 'fuel_tank_capacity';
