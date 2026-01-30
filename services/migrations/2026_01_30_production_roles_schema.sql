-- Migration: Align roles table schema with code
-- Date: 2026-01-30
-- Description: Add is_company_admin column to roles table
-- SAFE: Uses IF NOT EXISTS pattern

-- 1. Add is_company_admin column if not exists
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'roles' AND column_name = 'is_company_admin') THEN
        ALTER TABLE roles ADD COLUMN is_company_admin BOOLEAN NOT NULL DEFAULT FALSE;
        RAISE NOTICE 'Added is_company_admin column';
    END IF;
END $$;

-- 2. Set is_company_admin = true for roles that look like admin roles
UPDATE roles SET is_company_admin = TRUE 
WHERE LOWER(name) LIKE '%admin%' AND is_system_role = FALSE;

-- Verify
SELECT 'Roles schema after migration:' as info;
SELECT id, name, is_system_role, is_company_admin FROM roles ORDER BY id;
