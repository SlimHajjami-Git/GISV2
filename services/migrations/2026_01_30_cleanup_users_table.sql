-- Migration: Cleanup users table - Remove obsolete columns
-- Date: 2026-01-30
-- Description: Remove legacy columns replaced by role_id FK and user_vehicles table

-- ============================================
-- STEP 1: Add new columns (first_name, last_name)
-- ============================================

-- Add first_name and last_name columns
ALTER TABLE users ADD COLUMN IF NOT EXISTS first_name VARCHAR(50);
ALTER TABLE users ADD COLUMN IF NOT EXISTS last_name VARCHAR(50);

-- Migrate data from name to first_name/last_name
UPDATE users 
SET 
    first_name = COALESCE(SPLIT_PART(name, ' ', 1), ''),
    last_name = COALESCE(NULLIF(SUBSTRING(name FROM POSITION(' ' IN name) + 1), ''), '')
WHERE first_name IS NULL OR last_name IS NULL;

-- Set defaults for any remaining nulls
UPDATE users SET first_name = '' WHERE first_name IS NULL;
UPDATE users SET last_name = '' WHERE last_name IS NULL;

-- Make columns NOT NULL
ALTER TABLE users ALTER COLUMN first_name SET NOT NULL;
ALTER TABLE users ALTER COLUMN last_name SET NOT NULL;

-- ============================================
-- STEP 2: Ensure role_id is properly set
-- ============================================

-- Create default roles for companies that don't have any
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

-- Create default employee role for companies
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

-- Assign admin role to users who were company admins
UPDATE users u
SET role_id = (
    SELECT r.id FROM roles r 
    WHERE r.societe_id = u.company_id AND r.is_company_admin = true 
    LIMIT 1
)
WHERE u.role_id IS NULL 
  AND (u.is_company_admin = true OR 'admin' = ANY(u.roles) OR u.user_type = 'company_admin');

-- Assign employee role to remaining users without role_id
UPDATE users u
SET role_id = (
    SELECT r.id FROM roles r 
    WHERE r.societe_id = u.company_id AND r.is_company_admin = false 
    LIMIT 1
)
WHERE u.role_id IS NULL;

-- Make role_id NOT NULL (after all users have been assigned)
ALTER TABLE users ALTER COLUMN role_id SET NOT NULL;

-- ============================================
-- STEP 3: Migrate assigned_vehicle_ids to user_vehicles table
-- ============================================

-- Ensure user_vehicles table exists
CREATE TABLE IF NOT EXISTS user_vehicles (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    assigned_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    assigned_by_user_id INTEGER REFERENCES users(id),
    UNIQUE(user_id, vehicle_id)
);

-- Migrate data from assigned_vehicle_ids array to user_vehicles table
INSERT INTO user_vehicles (user_id, vehicle_id, assigned_at)
SELECT u.id, unnest(u.assigned_vehicle_ids), NOW()
FROM users u
WHERE u.assigned_vehicle_ids IS NOT NULL 
  AND array_length(u.assigned_vehicle_ids, 1) > 0
ON CONFLICT (user_id, vehicle_id) DO NOTHING;

-- ============================================
-- STEP 4: Drop obsolete columns
-- ============================================

-- Drop columns that are now managed via role_id
ALTER TABLE users DROP COLUMN IF EXISTS roles;
ALTER TABLE users DROP COLUMN IF EXISTS permissions;
ALTER TABLE users DROP COLUMN IF EXISTS user_type;
ALTER TABLE users DROP COLUMN IF EXISTS is_company_admin;
ALTER TABLE users DROP COLUMN IF EXISTS assigned_vehicle_ids;

-- Drop old name column (replaced by first_name + last_name)
ALTER TABLE users DROP COLUMN IF EXISTS name;

-- Drop columns that are not used in current schema
ALTER TABLE users DROP COLUMN IF EXISTS date_of_birth;
ALTER TABLE users DROP COLUMN IF EXISTS cin;
ALTER TABLE users DROP COLUMN IF EXISTS hire_date;
ALTER TABLE users DROP COLUMN IF EXISTS license_number;
ALTER TABLE users DROP COLUMN IF EXISTS license_expiry;

-- ============================================
-- STEP 5: Rename permit_number if needed
-- ============================================

-- Ensure permit_number column exists
ALTER TABLE users ADD COLUMN IF NOT EXISTS permit_number VARCHAR(50);

-- ============================================
-- STEP 6: Clean up indexes
-- ============================================

-- Drop obsolete indexes
DROP INDEX IF EXISTS idx_users_user_type;
DROP INDEX IF EXISTS idx_users_company_admin;

-- Ensure proper indexes exist
CREATE INDEX IF NOT EXISTS idx_users_first_name ON users(first_name);
CREATE INDEX IF NOT EXISTS idx_users_last_name ON users(last_name);
CREATE INDEX IF NOT EXISTS idx_users_role_id ON users(role_id);

-- ============================================
-- STEP 7: Add FK constraint if not exists
-- ============================================

-- Ensure FK constraint exists
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints 
        WHERE constraint_name = 'fk_users_role_id' 
        AND table_name = 'users'
    ) THEN
        ALTER TABLE users ADD CONSTRAINT fk_users_role_id 
        FOREIGN KEY (role_id) REFERENCES roles(id);
    END IF;
END $$;

-- ============================================
-- VERIFICATION
-- ============================================

-- Show final table structure
-- \d users
