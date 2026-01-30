-- ============================================================================
-- Migration: Users & Roles Restructure
-- Date: 2026-01-30
-- Description: Complete restructure of users and roles tables
--              - Split name into first_name/last_name
--              - Remove legacy arrays (roles[], permissions[])
--              - Add company_admin flag to roles
--              - Create user_vehicles pivot table
-- ============================================================================

BEGIN;

-- ============================================================================
-- STEP 0: BACKUP EXISTING DATA
-- ============================================================================
DROP TABLE IF EXISTS _backup_users_20260130 CASCADE;
DROP TABLE IF EXISTS _backup_roles_20260130 CASCADE;

CREATE TABLE _backup_users_20260130 AS SELECT * FROM users;
CREATE TABLE _backup_roles_20260130 AS SELECT * FROM roles;

-- ============================================================================
-- STEP 1: DROP FOREIGN KEY CONSTRAINTS REFERENCING users
-- ============================================================================
ALTER TABLE vehicles DROP CONSTRAINT IF EXISTS "FK_vehicles_users_assigned_driver_id";
ALTER TABLE vehicles DROP CONSTRAINT IF EXISTS "FK_vehicles_users_assigned_supervisor_id";
ALTER TABLE driver_assignments DROP CONSTRAINT IF EXISTS "FK_driver_assignments_users_DriverId";
ALTER TABLE driver_scores DROP CONSTRAINT IF EXISTS "FK_driver_scores_users_DriverId";
ALTER TABLE driving_events DROP CONSTRAINT IF EXISTS "FK_driving_events_users_DriverId";
ALTER TABLE fuel_records DROP CONSTRAINT IF EXISTS "FK_fuel_records_users_driver_id";
ALTER TABLE reservations DROP CONSTRAINT IF EXISTS "FK_reservations_users_AssignedDriverId";
ALTER TABLE trips DROP CONSTRAINT IF EXISTS "FK_trips_users_DriverId";
ALTER TABLE vehicle_stops DROP CONSTRAINT IF EXISTS "FK_vehicle_stops_users_driver_id";
ALTER TABLE users DROP CONSTRAINT IF EXISTS "FK_users_roles";
ALTER TABLE users DROP CONSTRAINT IF EXISTS "FK_users_societes_company_id";

-- ============================================================================
-- STEP 2: DROP AND RECREATE ROLES TABLE
-- ============================================================================
DROP TABLE IF EXISTS roles CASCADE;

CREATE TABLE roles (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description TEXT,
    societe_id INTEGER NOT NULL,
    is_company_admin BOOLEAN NOT NULL DEFAULT FALSE,
    permissions JSONB NOT NULL DEFAULT '{}',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    CONSTRAINT fk_roles_societe FOREIGN KEY (societe_id) 
        REFERENCES societes(id) ON DELETE CASCADE
);

-- Only one company_admin role per company
CREATE UNIQUE INDEX uq_roles_company_admin_per_societe 
    ON roles (societe_id) 
    WHERE is_company_admin = TRUE;

CREATE INDEX idx_roles_societe_id ON roles(societe_id);
CREATE INDEX idx_roles_is_company_admin ON roles(is_company_admin);

COMMENT ON TABLE roles IS 'Roles table - each company has its own roles, one must be company_admin';
COMMENT ON COLUMN roles.is_company_admin IS 'TRUE = this role has full admin permissions for the company';
COMMENT ON COLUMN roles.permissions IS 'Permissions inherited from subscription_type, can be restricted';

-- ============================================================================
-- STEP 3: DROP AND RECREATE USERS TABLE
-- ============================================================================
DROP TABLE IF EXISTS users CASCADE;

CREATE TABLE users (
    id SERIAL PRIMARY KEY,
    first_name VARCHAR(100) NOT NULL,
    last_name VARCHAR(100) NOT NULL,
    email VARCHAR(255) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    phone VARCHAR(20),
    permit_number VARCHAR(50),
    role_id INTEGER NOT NULL,
    company_id INTEGER NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'active',
    last_login_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    CONSTRAINT fk_users_role FOREIGN KEY (role_id) 
        REFERENCES roles(id) ON DELETE RESTRICT,
    CONSTRAINT fk_users_societe FOREIGN KEY (company_id) 
        REFERENCES societes(id) ON DELETE CASCADE,
    CONSTRAINT chk_users_status CHECK (status IN ('active', 'inactive', 'suspended'))
);

CREATE INDEX idx_users_email ON users(email);
CREATE INDEX idx_users_company_id ON users(company_id);
CREATE INDEX idx_users_role_id ON users(role_id);
CREATE INDEX idx_users_status ON users(status);

COMMENT ON TABLE users IS 'Users table - all users belong to a company and have a role';
COMMENT ON COLUMN users.permit_number IS 'Driving permit/license number';

-- ============================================================================
-- STEP 4: CREATE USER_VEHICLES PIVOT TABLE
-- ============================================================================
DROP TABLE IF EXISTS user_vehicles CASCADE;

CREATE TABLE user_vehicles (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL,
    vehicle_id INTEGER NOT NULL,
    assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    assigned_by INTEGER,
    
    CONSTRAINT fk_user_vehicles_user FOREIGN KEY (user_id) 
        REFERENCES users(id) ON DELETE CASCADE,
    CONSTRAINT fk_user_vehicles_vehicle FOREIGN KEY (vehicle_id) 
        REFERENCES vehicles(id) ON DELETE CASCADE,
    CONSTRAINT fk_user_vehicles_assigned_by FOREIGN KEY (assigned_by) 
        REFERENCES users(id) ON DELETE SET NULL,
    CONSTRAINT uq_user_vehicle UNIQUE (user_id, vehicle_id)
);

CREATE INDEX idx_user_vehicles_user_id ON user_vehicles(user_id);
CREATE INDEX idx_user_vehicles_vehicle_id ON user_vehicles(vehicle_id);

COMMENT ON TABLE user_vehicles IS 'Many-to-many relationship between users and vehicles within same company';

-- ============================================================================
-- STEP 5: RECREATE FOREIGN KEY CONSTRAINTS TO users
-- ============================================================================
ALTER TABLE vehicles ADD CONSTRAINT "FK_vehicles_users_assigned_driver_id" 
    FOREIGN KEY (assigned_driver_id) REFERENCES users(id) ON DELETE SET NULL;
ALTER TABLE vehicles ADD CONSTRAINT "FK_vehicles_users_assigned_supervisor_id" 
    FOREIGN KEY (assigned_supervisor_id) REFERENCES users(id) ON DELETE SET NULL;

-- Note: These constraints will only be added if the tables exist
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'driver_assignments') THEN
        ALTER TABLE driver_assignments ADD CONSTRAINT "FK_driver_assignments_users_DriverId" 
            FOREIGN KEY ("DriverId") REFERENCES users(id) ON DELETE CASCADE;
    END IF;
    
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'driver_scores') THEN
        ALTER TABLE driver_scores ADD CONSTRAINT "FK_driver_scores_users_DriverId" 
            FOREIGN KEY ("DriverId") REFERENCES users(id) ON DELETE CASCADE;
    END IF;
    
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'driving_events') THEN
        ALTER TABLE driving_events ADD CONSTRAINT "FK_driving_events_users_DriverId" 
            FOREIGN KEY ("DriverId") REFERENCES users(id) ON DELETE SET NULL;
    END IF;
    
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'fuel_records') THEN
        ALTER TABLE fuel_records ADD CONSTRAINT "FK_fuel_records_users_driver_id" 
            FOREIGN KEY (driver_id) REFERENCES users(id) ON DELETE SET NULL;
    END IF;
    
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'reservations') THEN
        ALTER TABLE reservations ADD CONSTRAINT "FK_reservations_users_AssignedDriverId" 
            FOREIGN KEY ("AssignedDriverId") REFERENCES users(id) ON DELETE SET NULL;
    END IF;
    
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'trips') THEN
        ALTER TABLE trips ADD CONSTRAINT "FK_trips_users_DriverId" 
            FOREIGN KEY ("DriverId") REFERENCES users(id) ON DELETE SET NULL;
    END IF;
    
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'vehicle_stops') THEN
        ALTER TABLE vehicle_stops ADD CONSTRAINT "FK_vehicle_stops_users_driver_id" 
            FOREIGN KEY (driver_id) REFERENCES users(id) ON DELETE SET NULL;
    END IF;
END $$;

-- ============================================================================
-- STEP 6: CREATE FUNCTION TO GET DEFAULT PERMISSIONS FROM SUBSCRIPTION
-- ============================================================================
CREATE OR REPLACE FUNCTION get_subscription_permissions(p_societe_id INTEGER)
RETURNS JSONB AS $$
DECLARE
    v_permissions JSONB;
    v_subscription_type subscription_types%ROWTYPE;
BEGIN
    -- Get the subscription type for this company
    SELECT st.* INTO v_subscription_type
    FROM societes s
    JOIN subscription_types st ON st.id = s.subscription_type_id
    WHERE s.id = p_societe_id;
    
    IF v_subscription_type IS NULL THEN
        -- Default permissions if no subscription
        RETURN '{
            "modules": {
                "dashboard": true,
                "monitoring": true,
                "vehicles": true,
                "users": true,
                "reports": true,
                "settings": true
            }
        }'::JSONB;
    END IF;
    
    -- Build permissions from subscription type
    v_permissions := jsonb_build_object(
        'modules', jsonb_build_object(
            'dashboard', COALESCE(v_subscription_type."ModuleDashboard", true),
            'monitoring', COALESCE(v_subscription_type."ModuleMonitoring", true),
            'vehicles', COALESCE(v_subscription_type."ModuleVehicles", true),
            'employees', COALESCE(v_subscription_type."ModuleEmployees", true),
            'geofences', COALESCE(v_subscription_type."ModuleGeofences", true),
            'maintenance', COALESCE(v_subscription_type."ModuleMaintenance", true),
            'costs', COALESCE(v_subscription_type."ModuleCosts", true),
            'reports', COALESCE(v_subscription_type."ModuleReports", true),
            'settings', COALESCE(v_subscription_type."ModuleSettings", true),
            'users', COALESCE(v_subscription_type."ModuleUsers", true),
            'suppliers', COALESCE(v_subscription_type."ModuleSuppliers", false),
            'documents', COALESCE(v_subscription_type."ModuleDocuments", false),
            'accidents', COALESCE(v_subscription_type."ModuleAccidents", false),
            'fleet_management', COALESCE(v_subscription_type."ModuleFleetManagement", false)
        ),
        'reports', jsonb_build_object(
            'trips', COALESCE(v_subscription_type."ReportTrips", true),
            'fuel', COALESCE(v_subscription_type."ReportFuel", true),
            'speed', COALESCE(v_subscription_type."ReportSpeed", true),
            'stops', COALESCE(v_subscription_type."ReportStops", true),
            'mileage', COALESCE(v_subscription_type."ReportMileage", true),
            'costs', COALESCE(v_subscription_type."ReportCosts", true),
            'maintenance', COALESCE(v_subscription_type."ReportMaintenance", true)
        )
    );
    
    RETURN v_permissions;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- STEP 7: CREATE FUNCTION TO AUTO-CREATE COMPANY ADMIN ROLE AND USER
-- ============================================================================
CREATE OR REPLACE FUNCTION create_company_admin(
    p_societe_id INTEGER,
    p_first_name VARCHAR(100),
    p_last_name VARCHAR(100),
    p_email VARCHAR(255),
    p_password_hash VARCHAR(255),
    p_phone VARCHAR(20) DEFAULT NULL
)
RETURNS INTEGER AS $$
DECLARE
    v_role_id INTEGER;
    v_user_id INTEGER;
    v_permissions JSONB;
BEGIN
    -- Get subscription permissions
    v_permissions := get_subscription_permissions(p_societe_id);
    
    -- Create company_admin role
    INSERT INTO roles (name, description, societe_id, is_company_admin, permissions)
    VALUES (
        'Administrateur',
        'Administrateur de la société avec tous les droits',
        p_societe_id,
        TRUE,
        v_permissions
    )
    RETURNING id INTO v_role_id;
    
    -- Create admin user
    INSERT INTO users (first_name, last_name, email, password_hash, phone, role_id, company_id, status)
    VALUES (p_first_name, p_last_name, p_email, p_password_hash, p_phone, v_role_id, p_societe_id, 'active')
    RETURNING id INTO v_user_id;
    
    RETURN v_user_id;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- STEP 8: CREATE DEFAULT EMPLOYEE ROLE FOR EACH COMPANY
-- ============================================================================
CREATE OR REPLACE FUNCTION create_default_employee_role(p_societe_id INTEGER)
RETURNS INTEGER AS $$
DECLARE
    v_role_id INTEGER;
    v_permissions JSONB;
BEGIN
    -- Get subscription permissions but restrict some modules
    v_permissions := get_subscription_permissions(p_societe_id);
    
    -- Restrict permissions for employees
    v_permissions := jsonb_set(v_permissions, '{modules,users}', 'false'::jsonb);
    v_permissions := jsonb_set(v_permissions, '{modules,settings}', 'false'::jsonb);
    
    -- Create employee role
    INSERT INTO roles (name, description, societe_id, is_company_admin, permissions)
    VALUES (
        'Employé',
        'Employé standard avec accès limité',
        p_societe_id,
        FALSE,
        v_permissions
    )
    RETURNING id INTO v_role_id;
    
    RETURN v_role_id;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- STEP 9: MIGRATE EXISTING DATA
-- ============================================================================

-- Create company_admin role and user for each existing company
DO $$
DECLARE
    v_societe RECORD;
    v_old_user RECORD;
    v_role_id INTEGER;
    v_user_id INTEGER;
    v_permissions JSONB;
    v_first_name VARCHAR(100);
    v_last_name VARCHAR(100);
    v_name_parts TEXT[];
BEGIN
    -- Loop through all existing companies
    FOR v_societe IN SELECT id, name FROM societes WHERE is_active = TRUE
    LOOP
        -- Get subscription permissions for this company
        v_permissions := get_subscription_permissions(v_societe.id);
        
        -- Create company_admin role
        INSERT INTO roles (name, description, societe_id, is_company_admin, permissions)
        VALUES (
            'Administrateur',
            'Administrateur de la société ' || v_societe.name,
            v_societe.id,
            TRUE,
            v_permissions
        )
        RETURNING id INTO v_role_id;
        
        -- Create default employee role
        PERFORM create_default_employee_role(v_societe.id);
        
        -- Find existing admin user for this company
        SELECT * INTO v_old_user 
        FROM _backup_users_20260130 
        WHERE company_id = v_societe.id 
        AND (
            'admin' = ANY(roles) 
            OR is_company_admin = TRUE 
            OR user_type IN ('admin', 'company_admin')
        )
        LIMIT 1;
        
        -- If admin user exists, migrate them
        IF v_old_user IS NOT NULL THEN
            -- Split name into first_name and last_name
            v_name_parts := string_to_array(COALESCE(v_old_user.name, 'Admin'), ' ');
            v_first_name := v_name_parts[1];
            v_last_name := COALESCE(array_to_string(v_name_parts[2:], ' '), v_societe.name);
            
            INSERT INTO users (first_name, last_name, email, password_hash, phone, permit_number, role_id, company_id, status, last_login_at, created_at)
            VALUES (
                v_first_name,
                v_last_name,
                v_old_user.email,
                v_old_user.password_hash,
                v_old_user.phone,
                v_old_user.license_number,
                v_role_id,
                v_societe.id,
                COALESCE(v_old_user.status, 'active'),
                v_old_user.last_login_at,
                COALESCE(v_old_user.created_at, NOW())
            );
        ELSE
            -- Create default admin if none exists
            INSERT INTO users (first_name, last_name, email, password_hash, role_id, company_id, status)
            VALUES (
                'Admin',
                v_societe.name,
                'admin_' || v_societe.id || '@company.local',
                '$2a$11$rKPvzLKEHK5Z5zJZ5zJZ5uaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
                v_role_id,
                v_societe.id,
                'active'
            );
        END IF;
        
        RAISE NOTICE 'Created admin for company: % (ID: %)', v_societe.name, v_societe.id;
    END LOOP;
END $$;

-- Migrate non-admin users
DO $$
DECLARE
    v_old_user RECORD;
    v_role_id INTEGER;
    v_first_name VARCHAR(100);
    v_last_name VARCHAR(100);
    v_name_parts TEXT[];
BEGIN
    FOR v_old_user IN 
        SELECT * FROM _backup_users_20260130 
        WHERE NOT (
            'admin' = ANY(roles) 
            OR is_company_admin = TRUE 
            OR user_type IN ('admin', 'company_admin', 'system_admin', 'platform_admin')
        )
    LOOP
        -- Get employee role for this company
        SELECT id INTO v_role_id 
        FROM roles 
        WHERE societe_id = v_old_user.company_id 
        AND is_company_admin = FALSE 
        LIMIT 1;
        
        IF v_role_id IS NULL THEN
            -- Create employee role if not exists
            v_role_id := create_default_employee_role(v_old_user.company_id);
        END IF;
        
        -- Split name
        v_name_parts := string_to_array(COALESCE(v_old_user.name, 'User'), ' ');
        v_first_name := v_name_parts[1];
        v_last_name := COALESCE(array_to_string(v_name_parts[2:], ' '), 'User');
        
        -- Check if email already exists
        IF NOT EXISTS (SELECT 1 FROM users WHERE email = v_old_user.email) THEN
            INSERT INTO users (first_name, last_name, email, password_hash, phone, permit_number, role_id, company_id, status, last_login_at, created_at)
            VALUES (
                v_first_name,
                v_last_name,
                v_old_user.email,
                v_old_user.password_hash,
                v_old_user.phone,
                v_old_user.license_number,
                v_role_id,
                v_old_user.company_id,
                COALESCE(v_old_user.status, 'active'),
                v_old_user.last_login_at,
                COALESCE(v_old_user.created_at, NOW())
            );
        END IF;
    END LOOP;
END $$;

-- Migrate vehicle assignments from old assigned_vehicle_ids array
DO $$
DECLARE
    v_old_user RECORD;
    v_new_user_id INTEGER;
    v_vehicle_id INTEGER;
BEGIN
    FOR v_old_user IN 
        SELECT * FROM _backup_users_20260130 
        WHERE assigned_vehicle_ids IS NOT NULL 
        AND array_length(assigned_vehicle_ids, 1) > 0
    LOOP
        -- Find new user by email
        SELECT id INTO v_new_user_id FROM users WHERE email = v_old_user.email;
        
        IF v_new_user_id IS NOT NULL THEN
            -- Insert vehicle assignments
            FOREACH v_vehicle_id IN ARRAY v_old_user.assigned_vehicle_ids
            LOOP
                -- Only insert if vehicle exists and belongs to same company
                INSERT INTO user_vehicles (user_id, vehicle_id)
                SELECT v_new_user_id, v.id
                FROM vehicles v
                WHERE v.id = v_vehicle_id 
                AND v.company_id = v_old_user.company_id
                ON CONFLICT (user_id, vehicle_id) DO NOTHING;
            END LOOP;
        END IF;
    END LOOP;
END $$;

-- ============================================================================
-- STEP 10: CREATE TRIGGER TO ENFORCE USER-ROLE COMPANY MATCH
-- ============================================================================
CREATE OR REPLACE FUNCTION check_user_role_company()
RETURNS TRIGGER AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM roles r 
        WHERE r.id = NEW.role_id 
        AND r.societe_id = NEW.company_id
    ) THEN
        RAISE EXCEPTION 'User role must belong to the same company as the user';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_check_user_role_company ON users;
CREATE TRIGGER trg_check_user_role_company
    BEFORE INSERT OR UPDATE ON users
    FOR EACH ROW
    EXECUTE FUNCTION check_user_role_company();

-- ============================================================================
-- STEP 11: CREATE TRIGGER TO ENFORCE USER-VEHICLE COMPANY MATCH
-- ============================================================================
CREATE OR REPLACE FUNCTION check_user_vehicle_company()
RETURNS TRIGGER AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM users u
        JOIN vehicles v ON v.company_id = u.company_id
        WHERE u.id = NEW.user_id 
        AND v.id = NEW.vehicle_id
    ) THEN
        RAISE EXCEPTION 'User can only be assigned vehicles from their own company';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_check_user_vehicle_company ON user_vehicles;
CREATE TRIGGER trg_check_user_vehicle_company
    BEFORE INSERT OR UPDATE ON user_vehicles
    FOR EACH ROW
    EXECUTE FUNCTION check_user_vehicle_company();

COMMIT;

-- ============================================================================
-- VERIFICATION QUERIES (run manually)
-- ============================================================================
-- SELECT 'Roles per company' as check, societe_id, COUNT(*) FROM roles GROUP BY societe_id;
-- SELECT 'Users per company' as check, company_id, COUNT(*) FROM users GROUP BY company_id;
-- SELECT 'Admin roles' as check, * FROM roles WHERE is_company_admin = TRUE;
-- SELECT 'Vehicle assignments' as check, COUNT(*) FROM user_vehicles;

-- ============================================================================
-- SAMPLE: How to create a new company with admin
-- ============================================================================
-- INSERT INTO societes (name, type, subscription_type_id, is_active) 
-- VALUES ('New Company', 'transport', 1, TRUE) RETURNING id;
-- 
-- SELECT create_company_admin(
--     <new_societe_id>,
--     'John',
--     'Doe',
--     'john.doe@newcompany.com',
--     '$2a$11$...',  -- hashed password
--     '+212600000000'
-- );
