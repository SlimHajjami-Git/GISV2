-- Migration: Add device_commands table and immobilization fields to gps_devices
-- Purpose: Database-driven AJ+ immobilization command system
-- - Commands (STOP/GO) are stored in DB with audit trail (user_id, timestamps)
-- - immobilization_requested flag prevents auto-recovery when stop is intentional
-- - aj_password per device allows different passwords per device

-- Add immobilization fields to gps_devices
ALTER TABLE gps_devices
    ADD COLUMN IF NOT EXISTS immobilization_requested BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS aj_password VARCHAR(20) NOT NULL DEFAULT '1311';

-- Create device_commands table
CREATE TABLE IF NOT EXISTS device_commands (
    id BIGSERIAL PRIMARY KEY,
    device_id INTEGER NOT NULL REFERENCES gps_devices(id),
    vehicle_id INTEGER REFERENCES vehicles(id),
    user_id INTEGER NOT NULL DEFAULT 0,
    command_type VARCHAR(20) NOT NULL,          -- 'STOP' | 'GO'
    command_text VARCHAR(100) NOT NULL,         -- 'AJ+STOP#1311\n' | 'AJ+GO#1311\n'
    status VARCHAR(20) NOT NULL DEFAULT 'pending',  -- 'pending' | 'sent' | 'acknowledged' | 'failed' | 'expired'
    sent_at TIMESTAMP,
    attempts INTEGER NOT NULL DEFAULT 0,
    error_message VARCHAR(500),
    source VARCHAR(20) NOT NULL DEFAULT 'manual',   -- 'manual' | 'auto_recovery'
    company_id INTEGER NOT NULL DEFAULT 1,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Indexes for fast lookup
CREATE INDEX IF NOT EXISTS idx_device_commands_device_status ON device_commands(device_id, status);
CREATE INDEX IF NOT EXISTS idx_device_commands_created_at ON device_commands(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_device_commands_company_id ON device_commands(company_id);
