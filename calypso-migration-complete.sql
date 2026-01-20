-- ============================================
-- CALYPSO - Migration SQL Complète Production
-- ============================================

-- 1. Table vehicle_user_assignments (Attribution véhicules aux employés)
CREATE TABLE IF NOT EXISTS vehicle_user_assignments (
    id SERIAL PRIMARY KEY,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    assigned_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    unassigned_at TIMESTAMP NULL,
    is_active BOOLEAN DEFAULT TRUE,
    assigned_by VARCHAR(255),
    notes TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(vehicle_id, user_id, is_active)
);

-- Index pour performance
CREATE INDEX IF NOT EXISTS idx_vehicle_assignments_vehicle ON vehicle_user_assignments(vehicle_id);
CREATE INDEX IF NOT EXISTS idx_vehicle_assignments_user ON vehicle_user_assignments(user_id);
CREATE INDEX IF NOT EXISTS idx_vehicle_assignments_active ON vehicle_user_assignments(is_active) WHERE is_active = TRUE;

-- 2. Ajouter colonne user_type si manquante
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'users' AND column_name = 'user_type') THEN
        ALTER TABLE users ADD COLUMN user_type VARCHAR(50) DEFAULT 'employee';
    END IF;
END $$;

-- 3. Ajouter colonne is_company_admin si manquante
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'users' AND column_name = 'is_company_admin') THEN
        ALTER TABLE users ADD COLUMN is_company_admin BOOLEAN DEFAULT FALSE;
    END IF;
END $$;

-- 4. Ajouter colonne role_id si manquante
DO $$ 
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'users' AND column_name = 'role_id') THEN
        ALTER TABLE users ADD COLUMN role_id INTEGER REFERENCES roles(id);
    END IF;
END $$;

-- 5. Créer table roles si manquante
CREATE TABLE IF NOT EXISTS roles (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description TEXT,
    role_type VARCHAR(50) DEFAULT 'employee',
    permissions JSONB,
    societe_id INTEGER REFERENCES societes(id),
    is_system BOOLEAN DEFAULT FALSE,
    is_default BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 6. Rôles système par défaut
INSERT INTO roles (name, description, role_type, is_system, is_default, permissions)
SELECT 'Administrateur Système', 'Accès complet à toutes les fonctionnalités', 'system_admin', TRUE, FALSE, '{"all": true}'
WHERE NOT EXISTS (SELECT 1 FROM roles WHERE role_type = 'system_admin' AND is_system = TRUE);

INSERT INTO roles (name, description, role_type, is_system, is_default, permissions)
SELECT 'Chef de Société', 'Gestion complète de la société', 'company_admin', TRUE, FALSE, '{"dashboard": true, "monitoring": true, "vehicles": true, "employees": true, "users": true, "settings": true, "reports": true}'
WHERE NOT EXISTS (SELECT 1 FROM roles WHERE role_type = 'company_admin' AND is_system = TRUE);

INSERT INTO roles (name, description, role_type, is_system, is_default, permissions)
SELECT 'Employé', 'Accès limité aux véhicules attribués', 'employee', TRUE, TRUE, '{"dashboard": true, "monitoring": true}'
WHERE NOT EXISTS (SELECT 1 FROM roles WHERE role_type = 'employee' AND is_system = TRUE AND is_default = TRUE);

-- 7. Subscription Types avec droits d'accès
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS access_rights JSONB;

-- Mettre à jour les subscription types existants avec droits par défaut
UPDATE subscription_types SET access_rights = '{
    "monitoring": true,
    "playback": true,
    "reports": true,
    "vehicles": true,
    "maintenance": false,
    "repairs": false,
    "geofences": true,
    "alerts": true
}'::jsonb WHERE access_rights IS NULL;

-- 8. Contrainte: un seul chef de société actif par société
CREATE UNIQUE INDEX IF NOT EXISTS idx_unique_company_admin 
ON users (company_id) 
WHERE is_company_admin = TRUE AND status = 'active';

-- 9. Vue pour véhicules avec assignations
CREATE OR REPLACE VIEW v_user_vehicles AS
SELECT 
    v.*,
    va.user_id as assigned_user_id,
    u.name as assigned_user_name
FROM vehicles v
LEFT JOIN vehicle_user_assignments va ON v.id = va.vehicle_id AND va.is_active = TRUE
LEFT JOIN users u ON va.user_id = u.id;

-- 10. Fonction pour vérifier les droits d'abonnement
CREATE OR REPLACE FUNCTION check_subscription_access(
    p_company_id INTEGER,
    p_feature VARCHAR
) RETURNS BOOLEAN AS $$
DECLARE
    v_access BOOLEAN;
BEGIN
    SELECT (st.access_rights->p_feature)::boolean INTO v_access
    FROM societes s
    JOIN subscription_types st ON s.subscription_type_id = st.id
    WHERE s.id = p_company_id;
    
    RETURN COALESCE(v_access, FALSE);
END;
$$ LANGUAGE plpgsql;

-- 11. Trigger pour updated_at automatique
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Appliquer trigger sur vehicle_user_assignments
DROP TRIGGER IF EXISTS trg_vehicle_assignments_updated ON vehicle_user_assignments;
CREATE TRIGGER trg_vehicle_assignments_updated
    BEFORE UPDATE ON vehicle_user_assignments
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- 12. Index performance pour multi-tenant
CREATE INDEX IF NOT EXISTS idx_vehicles_company ON vehicles(company_id);
CREATE INDEX IF NOT EXISTS idx_users_company ON users(company_id);
CREATE INDEX IF NOT EXISTS idx_roles_societe ON roles(societe_id);

COMMENT ON TABLE vehicle_user_assignments IS 'Attribution des véhicules aux employés - Calypso';
COMMENT ON COLUMN users.is_company_admin IS 'Chef de société (un seul par société)';
COMMENT ON COLUMN users.user_type IS 'Type: system_admin, company_admin, employee';
