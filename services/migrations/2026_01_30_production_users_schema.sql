-- Migration: Align users table schema with code
-- Date: 2026-01-30
-- Description: Add first_name, last_name, permit_number columns and migrate data from name
-- SAFE: Uses IF NOT EXISTS pattern

-- 1. Add first_name column if not exists
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'users' AND column_name = 'first_name') THEN
        ALTER TABLE users ADD COLUMN first_name VARCHAR(100);
        RAISE NOTICE 'Added first_name column';
    END IF;
END $$;

-- 2. Add last_name column if not exists
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'users' AND column_name = 'last_name') THEN
        ALTER TABLE users ADD COLUMN last_name VARCHAR(100);
        RAISE NOTICE 'Added last_name column';
    END IF;
END $$;

-- 3. Add permit_number column if not exists
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'users' AND column_name = 'permit_number') THEN
        ALTER TABLE users ADD COLUMN permit_number VARCHAR(50);
        RAISE NOTICE 'Added permit_number column';
    END IF;
END $$;

-- 4. Migrate data from name to first_name/last_name
-- Split name: first word = first_name, rest = last_name
UPDATE users 
SET 
    first_name = COALESCE(
        CASE 
            WHEN position(' ' in name) > 0 THEN substring(name from 1 for position(' ' in name) - 1)
            ELSE name
        END,
        name
    ),
    last_name = COALESCE(
        CASE 
            WHEN position(' ' in name) > 0 THEN substring(name from position(' ' in name) + 1)
            ELSE ''
        END,
        ''
    )
WHERE first_name IS NULL AND name IS NOT NULL;

-- 5. Set default values for any remaining NULLs
UPDATE users SET first_name = 'User' WHERE first_name IS NULL OR first_name = '';
UPDATE users SET last_name = '' WHERE last_name IS NULL;

-- 6. Make first_name NOT NULL (after data migration)
ALTER TABLE users ALTER COLUMN first_name SET NOT NULL;
ALTER TABLE users ALTER COLUMN last_name SET NOT NULL;

-- 7. Set default for role_id if NULL (use first available role)
UPDATE users 
SET role_id = (SELECT id FROM roles LIMIT 1)
WHERE role_id IS NULL;

-- 8. Make role_id NOT NULL
ALTER TABLE users ALTER COLUMN role_id SET NOT NULL;

-- Verify
SELECT 'Users schema after migration:' as info;
SELECT column_name, data_type, is_nullable 
FROM information_schema.columns 
WHERE table_name = 'users' 
AND column_name IN ('first_name', 'last_name', 'permit_number', 'role_id')
ORDER BY column_name;

SELECT 'Sample users:' as info;
SELECT id, email, first_name, last_name, name, role_id FROM users LIMIT 5;
