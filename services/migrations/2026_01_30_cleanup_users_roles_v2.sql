-- Migration: Cleanup users and roles tables - V2
-- Date: 2026-01-30
-- Description: Update roles table, then cleanup users table

-- ============================================
-- STEP 0: Drop dependent views
-- ============================================
DROP VIEW IF EXISTS user_effective_permissions CASCADE;

-- ============================================
-- STEP 1: Update roles table structure
-- ============================================

-- Add is_company_admin column to roles
ALTER TABLE roles ADD COLUMN IF NOT EXISTS is_company_admin BOOLEAN NOT NULL DEFAULT false;

-- Migrate role_type to is_company_admin
UPDATE roles SET is_company_admin = true WHERE role_type IN ('company_admin', 'admin', 'system_admin');

-- Make societe_id NOT NULL (all roles belong to a company now)
-- First, delete system roles or assign them to a company
DELETE FROM roles WHERE societe_id IS NULL AND NOT EXISTS (SELECT 1 FROM users WHERE role_id = roles.id);

-- For remaining roles without societe_id that have users, assign to user's company
UPDATE roles r
SET societe_id = (SELECT u.company_id FROM users u WHERE u.role_id = r.id LIMIT 1)
WHERE r.societe_id IS NULL;

-- Now make it NOT NULL if no NULL values remain
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM roles WHERE societe_id IS NULL) THEN
        ALTER TABLE roles ALTER COLUMN societe_id SET NOT NULL;
    END IF;
END $$;

-- Drop obsolete columns from roles
ALTER TABLE roles DROP COLUMN IF EXISTS role_type;
ALTER TABLE roles DROP COLUMN IF EXISTS is_system;
ALTER TABLE roles DROP COLUMN IF EXISTS is_default;
ALTER TABLE roles DROP COLUMN IF EXISTS module_permissions;

-- ============================================
-- STEP 2: Ensure default roles exist per company
-- ============================================

-- Create admin role for companies without one
INSERT INTO roles (name, description, societe_id, is_company_admin, permissions, created_at, updated_at)
SELECT 
    'Administrateur',
    'Administrateur de la société avec tous les accès',
    s.id,
    true,
    '{"dashboard": true, "vehicles": true, "drivers": true, "geofences": true, "reports": true, "maintenance": true, "costs": true, "gps_devices": true, "users": true, "settings": true}'::jsonb,
    NOW(),
    NOW()
FROM societes s
WHERE NOT EXISTS (
    SELECT 1 FROM roles r WHERE r.societe_id = s.id AND r.is_company_admin = true
);

-- Create employee role for companies without one
INSERT INTO roles (name, description, societe_id, is_company_admin, permissions, created_at, updated_at)
SELECT 
    'Employé',
    'Rôle employé par défaut',
    s.id,
    false,
    '{"dashboard": true, "monitoring": true}'::jsonb,
    NOW(),
    NOW()
FROM societes s
WHERE NOT EXISTS (
    SELECT 1 FROM roles r WHERE r.societe_id = s.id AND r.is_company_admin = false
);

-- ============================================
-- STEP 3: Assign roles to users without role_id
-- ============================================

-- Assign admin role to company admins
UPDATE users u
SET role_id = (
    SELECT r.id FROM roles r 
    WHERE r.societe_id = u.company_id AND r.is_company_admin = true 
    LIMIT 1
)
WHERE u.role_id IS NULL 
  AND (u.is_company_admin = true OR 'admin' = ANY(u.roles) OR u.user_type = 'company_admin');

-- Assign employee role to remaining users
UPDATE users u
SET role_id = (
    SELECT r.id FROM roles r 
    WHERE r.societe_id = u.company_id AND r.is_company_admin = false 
    LIMIT 1
)
WHERE u.role_id IS NULL;

-- Make role_id NOT NULL
ALTER TABLE users ALTER COLUMN role_id SET NOT NULL;

-- ============================================
-- STEP 4: Drop obsolete columns from users
-- ============================================

ALTER TABLE users DROP COLUMN IF EXISTS roles;
ALTER TABLE users DROP COLUMN IF EXISTS permissions;
ALTER TABLE users DROP COLUMN IF EXISTS user_type;
ALTER TABLE users DROP COLUMN IF EXISTS is_company_admin;
ALTER TABLE users DROP COLUMN IF EXISTS assigned_vehicle_ids;
ALTER TABLE users DROP COLUMN IF EXISTS name;
ALTER TABLE users DROP COLUMN IF EXISTS date_of_birth;
ALTER TABLE users DROP COLUMN IF EXISTS cin;
ALTER TABLE users DROP COLUMN IF EXISTS hire_date;
ALTER TABLE users DROP COLUMN IF EXISTS license_number;
ALTER TABLE users DROP COLUMN IF EXISTS license_expiry;

-- ============================================
-- STEP 5: Ensure proper columns exist
-- ============================================

ALTER TABLE users ADD COLUMN IF NOT EXISTS permit_number VARCHAR(50);

-- ============================================
-- STEP 6: Cleanup indexes
-- ============================================

DROP INDEX IF EXISTS idx_users_user_type;
DROP INDEX IF EXISTS idx_users_company_admin;
CREATE INDEX IF NOT EXISTS idx_users_first_name ON users(first_name);
CREATE INDEX IF NOT EXISTS idx_users_last_name ON users(last_name);

-- ============================================
-- VERIFICATION
-- ============================================

SELECT 'Users table structure:' as info;
SELECT column_name, data_type, is_nullable 
FROM information_schema.columns 
WHERE table_name = 'users' 
ORDER BY ordinal_position;

SELECT 'Roles table structure:' as info;
SELECT column_name, data_type, is_nullable 
FROM information_schema.columns 
WHERE table_name = 'roles' 
ORDER BY ordinal_position;
