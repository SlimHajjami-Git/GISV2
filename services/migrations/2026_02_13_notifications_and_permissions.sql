-- ============================================================
-- Migration: Notifications + User Permissions + Security Fixes
-- Date: 2026-02-13
-- Description:
--   1. Add per-user module permission columns to users table
--   2. Grant all permissions to existing admin users
--   3. Create user_vehicles table for vehicle assignment filtering
--   4. Create notifications table if not exists
-- ============================================================

-- 1. Add per-user module permission columns
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS access_level VARCHAR(20) NOT NULL DEFAULT 'user',
    ADD COLUMN IF NOT EXISTS can_monitoring BOOLEAN NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS can_vehicles BOOLEAN NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS can_drivers BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_reports BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_geofences BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_maintenance BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_costs BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_documents BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_accidents BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_users BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_settings BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_suppliers BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS can_fleet_management BOOLEAN NOT NULL DEFAULT false;

-- 2. Grant all permissions to existing admin users
UPDATE users
SET access_level = 'admin',
    can_monitoring = true,
    can_vehicles = true,
    can_drivers = true,
    can_reports = true,
    can_geofences = true,
    can_maintenance = true,
    can_costs = true,
    can_documents = true,
    can_accidents = true,
    can_users = true,
    can_settings = true,
    can_suppliers = true,
    can_fleet_management = true
WHERE role_id IN (SELECT id FROM roles WHERE is_company_admin = true OR is_system_role = true);

-- 3. user_vehicles table for per-user vehicle assignment
CREATE TABLE IF NOT EXISTS user_vehicles (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    assigned_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    assigned_by_id INTEGER REFERENCES users(id),
    UNIQUE(user_id, vehicle_id)
);

CREATE INDEX IF NOT EXISTS idx_user_vehicles_user_id ON user_vehicles(user_id);
CREATE INDEX IF NOT EXISTS idx_user_vehicles_vehicle_id ON user_vehicles(vehicle_id);

-- 4. notifications table
CREATE TABLE IF NOT EXISTS notifications (
    id BIGSERIAL PRIMARY KEY,
    company_id INTEGER NOT NULL,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    type VARCHAR(50) NOT NULL DEFAULT 'info',
    title VARCHAR(255) NOT NULL,
    message TEXT,
    priority VARCHAR(20) NOT NULL DEFAULT 'normal',
    is_read BOOLEAN NOT NULL DEFAULT false,
    reference_type VARCHAR(50),
    reference_id INTEGER,
    action_url VARCHAR(500),
    metadata JSONB,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    read_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX IF NOT EXISTS idx_notifications_user_id ON notifications(user_id);
CREATE INDEX IF NOT EXISTS idx_notifications_company_id ON notifications(company_id);
CREATE INDEX IF NOT EXISTS idx_notifications_user_unread ON notifications(user_id, is_read) WHERE is_read = false;
