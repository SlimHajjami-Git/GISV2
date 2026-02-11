-- Migration: Add employee/driver fields to users table
-- Date: 2026-02-11

ALTER TABLE users ADD COLUMN IF NOT EXISTS permit_type varchar(50);
ALTER TABLE users ADD COLUMN IF NOT EXISTS permit_expiry timestamp with time zone;
ALTER TABLE users ADD COLUMN IF NOT EXISTS cin varchar(50);
ALTER TABLE users ADD COLUMN IF NOT EXISTS date_of_birth timestamp with time zone;
ALTER TABLE users ADD COLUMN IF NOT EXISTS employee_role varchar(50);
ALTER TABLE users ADD COLUMN IF NOT EXISTS hire_date timestamp with time zone;

CREATE INDEX IF NOT EXISTS idx_users_employee_role ON users (employee_role) WHERE employee_role IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_users_permit_expiry ON users (permit_expiry) WHERE permit_expiry IS NOT NULL;
