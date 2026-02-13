-- ============================================================
-- Migration: Drivers + Missing Tables
-- Date: 2026-02-13
-- Description:
--   1. Create drivers table
--   2. Create driver_assignments table
--   3. Create driver_scores table
--   4. Create departments table
--   5. Create speed_limit_alerts table
--   6. Create audit_logs table
-- ============================================================

-- 1. Drivers table
CREATE TABLE IF NOT EXISTS drivers (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    permit_number VARCHAR(100),
    permit_type VARCHAR(50),
    permit_expiry TIMESTAMPTZ,
    cin VARCHAR(50),
    date_of_birth TIMESTAMPTZ,
    hire_date TIMESTAMPTZ,
    assigned_vehicle_id INTEGER REFERENCES vehicles(id) ON DELETE SET NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'active',
    company_id INTEGER NOT NULL REFERENCES societes(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_drivers_company_id ON drivers(company_id);
CREATE INDEX IF NOT EXISTS idx_drivers_user_id ON drivers(user_id);

-- 2. Driver assignments table
CREATE TABLE IF NOT EXISTS driver_assignments (
    id SERIAL PRIMARY KEY,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    driver_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    assigned_by_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
    start_date TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    end_date TIMESTAMPTZ,
    status VARCHAR(20) NOT NULL DEFAULT 'active',
    assignment_type VARCHAR(30) NOT NULL DEFAULT 'permanent',
    notes TEXT,
    start_mileage INTEGER,
    end_mileage INTEGER,
    company_id INTEGER NOT NULL REFERENCES societes(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_driver_assignments_company_id ON driver_assignments(company_id);
CREATE INDEX IF NOT EXISTS idx_driver_assignments_vehicle_id ON driver_assignments(vehicle_id);
CREATE INDEX IF NOT EXISTS idx_driver_assignments_driver_id ON driver_assignments(driver_id);

-- 3. Driver scores table
CREATE TABLE IF NOT EXISTS driver_scores (
    id SERIAL PRIMARY KEY,
    driver_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    date DATE NOT NULL,
    overall_score INTEGER NOT NULL DEFAULT 100,
    speeding_score INTEGER NOT NULL DEFAULT 100,
    braking_score INTEGER NOT NULL DEFAULT 100,
    acceleration_score INTEGER NOT NULL DEFAULT 100,
    idling_score INTEGER NOT NULL DEFAULT 100,
    fuel_efficiency_score INTEGER NOT NULL DEFAULT 100,
    speeding_events INTEGER NOT NULL DEFAULT 0,
    harsh_braking_events INTEGER NOT NULL DEFAULT 0,
    harsh_acceleration_events INTEGER NOT NULL DEFAULT 0,
    idling_events INTEGER NOT NULL DEFAULT 0,
    distance_km NUMERIC(10,2) NOT NULL DEFAULT 0,
    driving_time_minutes INTEGER NOT NULL DEFAULT 0,
    company_id INTEGER NOT NULL REFERENCES societes(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_driver_scores_driver_date UNIQUE(driver_id, date)
);

CREATE INDEX IF NOT EXISTS idx_driver_scores_company_id ON driver_scores(company_id);
CREATE INDEX IF NOT EXISTS idx_driver_scores_driver_id ON driver_scores(driver_id);

-- 4. Departments table
CREATE TABLE IF NOT EXISTS departments (
    id SERIAL PRIMARY KEY,
    name VARCHAR(200) NOT NULL,
    description TEXT,
    is_active BOOLEAN NOT NULL DEFAULT true,
    company_id INTEGER NOT NULL REFERENCES societes(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_departments_company_id ON departments(company_id);

-- 5. Speed limit alerts table
CREATE TABLE IF NOT EXISTS speed_limit_alerts (
    id SERIAL PRIMARY KEY,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    speed_limit INTEGER NOT NULL,
    actual_speed INTEGER NOT NULL,
    latitude DOUBLE PRECISION NOT NULL DEFAULT 0,
    longitude DOUBLE PRECISION NOT NULL DEFAULT 0,
    address TEXT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_acknowledged BOOLEAN NOT NULL DEFAULT false,
    acknowledged_at TIMESTAMPTZ,
    acknowledged_by_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
    company_id INTEGER NOT NULL REFERENCES societes(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_speed_limit_alerts_company_id ON speed_limit_alerts(company_id);
CREATE INDEX IF NOT EXISTS idx_speed_limit_alerts_vehicle_id ON speed_limit_alerts(vehicle_id);

-- 6. Audit logs table
CREATE TABLE IF NOT EXISTS audit_logs (
    id BIGSERIAL PRIMARY KEY,
    user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
    company_id INTEGER REFERENCES societes(id) ON DELETE SET NULL,
    action VARCHAR(100) NOT NULL,
    entity_type VARCHAR(100) NOT NULL,
    entity_id INTEGER,
    entity_name VARCHAR(255),
    old_values JSONB,
    new_values JSONB,
    ip_address VARCHAR(50),
    user_agent TEXT,
    description TEXT,
    timestamp TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_audit_logs_company_id ON audit_logs(company_id);
CREATE INDEX IF NOT EXISTS idx_audit_logs_user_id ON audit_logs(user_id);
CREATE INDEX IF NOT EXISTS idx_audit_logs_timestamp ON audit_logs(timestamp);
