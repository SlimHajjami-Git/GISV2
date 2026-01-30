-- Migration: System Admin Role for Production
-- Date: 2026-01-30
-- Description: Add is_system_role column and create System Admin role
-- SAFE: Uses IF NOT EXISTS to avoid breaking existing data

BEGIN;

-- 1. Add is_system_role column if not exists
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                   WHERE table_name = 'roles' AND column_name = 'is_system_role') THEN
        ALTER TABLE roles ADD COLUMN is_system_role BOOLEAN NOT NULL DEFAULT FALSE;
        RAISE NOTICE 'Added is_system_role column to roles table';
    ELSE
        RAISE NOTICE 'is_system_role column already exists';
    END IF;
END $$;

-- 2. Create System Admin role if not exists
INSERT INTO roles (name, description, is_system_role, is_company_admin, permissions, created_at, updated_at)
SELECT 'System Admin', 'Administrateur système avec pouvoir absolu', TRUE, FALSE, 
       '{"*": true}'::jsonb, NOW(), NOW()
WHERE NOT EXISTS (SELECT 1 FROM roles WHERE name = 'System Admin' OR is_system_role = TRUE);

-- 3. Update existing System Admin role to ensure is_system_role = true
UPDATE roles SET is_system_role = TRUE WHERE name = 'System Admin';

-- 4. Assign System Admin role to admin@belive.tn if exists
DO $$
DECLARE
    v_role_id INT;
    v_user_id INT;
BEGIN
    SELECT id INTO v_role_id FROM roles WHERE is_system_role = TRUE LIMIT 1;
    SELECT id INTO v_user_id FROM users WHERE email = 'admin@belive.tn';
    
    IF v_role_id IS NOT NULL AND v_user_id IS NOT NULL THEN
        UPDATE users SET role_id = v_role_id WHERE id = v_user_id;
        RAISE NOTICE 'Assigned System Admin role to admin@belive.tn';
    ELSE
        RAISE NOTICE 'admin@belive.tn not found or System Admin role not created';
    END IF;
END $$;

-- Verify
SELECT 'Roles with is_system_role:' as info;
SELECT id, name, is_system_role, is_company_admin FROM roles WHERE is_system_role = TRUE;

SELECT 'Users with System Admin role:' as info;
SELECT u.id, u.email, r.name as role_name, r.is_system_role 
FROM users u 
JOIN roles r ON u.role_id = r.id 
WHERE r.is_system_role = TRUE;

COMMIT;
