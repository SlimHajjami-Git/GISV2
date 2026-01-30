-- Migration: Create system_admin role with absolute power
-- Date: 2026-01-30
-- Description: Add system role support and create system_admin role

-- ============================================
-- STEP 1: Add is_system_role column to roles
-- ============================================

ALTER TABLE roles ADD COLUMN IF NOT EXISTS is_system_role BOOLEAN NOT NULL DEFAULT false;

-- Make societe_id nullable for system roles
ALTER TABLE roles ALTER COLUMN societe_id DROP NOT NULL;

-- ============================================
-- STEP 2: Create system_admin role
-- ============================================

INSERT INTO roles (name, description, societe_id, is_company_admin, is_system_role, permissions, created_at, updated_at)
VALUES (
    'System Admin',
    'Administrateur système avec pouvoir absolu sur toute l''application',
    NULL,  -- No company - system-wide role
    false, -- Not a company admin, it's a SYSTEM admin
    true,  -- This is a system role
    '{"*": true, "system_admin": true}'::jsonb,
    NOW(),
    NOW()
)
ON CONFLICT DO NOTHING;

-- Get the system_admin role id
DO $$
DECLARE
    system_admin_role_id INTEGER;
    target_user_id INTEGER;
BEGIN
    -- Get or create system_admin role
    SELECT id INTO system_admin_role_id FROM roles WHERE name = 'System Admin' AND is_system_role = true LIMIT 1;
    
    IF system_admin_role_id IS NULL THEN
        INSERT INTO roles (name, description, societe_id, is_company_admin, is_system_role, permissions, created_at, updated_at)
        VALUES ('System Admin', 'Administrateur système avec pouvoir absolu', NULL, false, true, '{"*": true, "system_admin": true}'::jsonb, NOW(), NOW())
        RETURNING id INTO system_admin_role_id;
    END IF;
    
    -- Find admin@belive.tn user
    SELECT id INTO target_user_id FROM users WHERE email = 'admin@belive.tn' LIMIT 1;
    
    IF target_user_id IS NOT NULL THEN
        -- Update user to have system_admin role
        UPDATE users SET role_id = system_admin_role_id WHERE id = target_user_id;
        RAISE NOTICE 'User admin@belive.tn (id=%) assigned to System Admin role (id=%)', target_user_id, system_admin_role_id;
    ELSE
        RAISE NOTICE 'User admin@belive.tn not found';
    END IF;
END $$;

-- ============================================
-- STEP 3: Add index for system roles
-- ============================================

CREATE INDEX IF NOT EXISTS idx_roles_is_system_role ON roles(is_system_role) WHERE is_system_role = true;

-- ============================================
-- VERIFICATION
-- ============================================

SELECT 'System Admin Role:' as info;
SELECT id, name, description, societe_id, is_company_admin, is_system_role 
FROM roles 
WHERE is_system_role = true;

SELECT 'User with System Admin role:' as info;
SELECT u.id, u.email, u.first_name, u.last_name, r.name as role_name, r.is_system_role
FROM users u
JOIN roles r ON u.role_id = r.id
WHERE r.is_system_role = true;
