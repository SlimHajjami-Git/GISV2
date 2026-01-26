-- ================================================================================
-- Script de correction des permissions sys_admin
-- GIS V2 - 26 Janvier 2026
-- ================================================================================

-- 1. Vérifier les utilisateurs admin actuels
SELECT id, name, email, user_type, roles, permissions, is_company_admin
FROM users 
WHERE user_type IN ('system_admin', 'platform_admin', 'admin')
   OR 'super_admin' = ANY(roles) 
   OR 'admin' = ANY(roles);

-- 2. S'assurer que les platform_admin ont les bons roles dans le tableau roles[]
UPDATE users 
SET roles = CASE 
    WHEN NOT 'super_admin' = ANY(roles) THEN array_append(roles, 'super_admin')
    ELSE roles
END
WHERE user_type IN ('system_admin', 'platform_admin')
  AND NOT 'super_admin' = ANY(roles);

-- 3. S'assurer que les admins ont la permission 'all'
UPDATE users 
SET permissions = CASE 
    WHEN NOT 'all' = ANY(permissions) THEN array_append(permissions, 'all')
    ELSE permissions
END
WHERE user_type IN ('system_admin', 'platform_admin')
  AND NOT 'all' = ANY(permissions);

-- 4. Vérification finale
SELECT id, name, email, user_type, roles, permissions
FROM users 
WHERE user_type IN ('system_admin', 'platform_admin');

-- 5. Optionnel: Créer un sys_admin si n'existe pas
-- INSERT INTO users (name, email, password_hash, roles, permissions, user_type, status, company_id, is_company_admin, created_at, updated_at)
-- VALUES (
--     'System Admin',
--     'sysadmin@gis.local',
--     '$2a$11$SSFnYGBW31exSV45qir0KutzLHPu3BJo/ZPVfzUlJtPe30T.9g9Rm', -- Calypso@2026+
--     ARRAY['super_admin', 'platform_admin'],
--     ARRAY['all'],
--     'platform_admin',
--     'active',
--     1, -- company_id (à adapter)
--     true,
--     NOW(), NOW()
-- )
-- ON CONFLICT (email) DO NOTHING;
