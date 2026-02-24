-- 1. Fix any existing orphaned role assignments
UPDATE users u
SET role_id = (
    SELECT r.id FROM roles r 
    WHERE r.societe_id = u.company_id AND r.is_company_admin = true 
    LIMIT 1
)
WHERE NOT EXISTS (
    SELECT 1 FROM roles r 
    WHERE r.id = u.role_id AND r.societe_id = u.company_id
);

-- 2. Ensure every company has at least one non-admin role
INSERT INTO roles (name, description, societe_id, is_company_admin, is_system_role, created_at, updated_at)
SELECT 'Opérateur', 'Utilisateur avec accès limité', s.id, false, false, NOW(), NOW()
FROM societes s
WHERE NOT EXISTS (
    SELECT 1 FROM roles r WHERE r.societe_id = s.id AND r.is_company_admin = false
);

-- 3. Create trigger to prevent cross-company role assignments
CREATE OR REPLACE FUNCTION check_user_role_company()
RETURNS TRIGGER AS $$
BEGIN
    IF NEW.role_id IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1 FROM roles 
            WHERE id = NEW.role_id 
            AND societe_id = NEW.company_id
        ) THEN
            RAISE EXCEPTION 'Role % does not belong to company %', NEW.role_id, NEW.company_id;
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_user_role_company_check ON users;
CREATE TRIGGER trg_user_role_company_check
    BEFORE INSERT OR UPDATE ON users
    FOR EACH ROW
    EXECUTE FUNCTION check_user_role_company();
