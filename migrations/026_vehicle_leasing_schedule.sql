-- Migration: Add leasing schedule columns to vehicles
-- Date: 2026-04-14
-- Required for commit e86a688 (leasing payment schedule in vehicle info)

ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS leasing_duration_months INTEGER;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS leasing_start_date TIMESTAMP;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS leasing_payment_day INTEGER;
