-- Migration: Device commands system + immobilization fields
-- Commands and passwords stored in DB, nothing hardcoded in Rust

-- Add immobilization fields to gps_devices
ALTER TABLE gps_devices
    ADD COLUMN IF NOT EXISTS immobilization_active BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS command_go VARCHAR(100) NOT NULL DEFAULT E'AJ+GO#1311\n',
    ADD COLUMN IF NOT EXISTS command_stop VARCHAR(100) NOT NULL DEFAULT E'AJ+STOP#1311\n';

-- Drop old columns if they exist (from previous migration attempt)
ALTER TABLE gps_devices DROP COLUMN IF EXISTS immobilization_requested;
ALTER TABLE gps_devices DROP COLUMN IF EXISTS aj_password;

-- Create device_commands table (audit trail: who sent what, when)
CREATE TABLE IF NOT EXISTS device_commands (
    id BIGSERIAL PRIMARY KEY,
    device_id INTEGER NOT NULL REFERENCES gps_devices(id),
    vehicle_id INTEGER REFERENCES vehicles(id),
    user_id INTEGER NOT NULL DEFAULT 0,
    command_type VARCHAR(20) NOT NULL,
    command_text VARCHAR(100) NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'pending',
    sent_at TIMESTAMP,
    attempts INTEGER NOT NULL DEFAULT 0,
    error_message VARCHAR(500),
    source VARCHAR(20) NOT NULL DEFAULT 'manual',
    company_id INTEGER NOT NULL DEFAULT 1,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_device_commands_device_status ON device_commands(device_id, status);
CREATE INDEX IF NOT EXISTS idx_device_commands_created_at ON device_commands(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_device_commands_company_id ON device_commands(company_id);
