-- Migration: Add accident_events table for persisted accident reports
-- Date: 2026-04-15
-- Related: Step C of the 118013 retro-notification work. Backs the new
-- /api/accident-reports/{id} endpoint and the frontend accident-report
-- component, which previously had its narrative fully hardcoded.

CREATE TABLE IF NOT EXISTS accident_events (
    id SERIAL PRIMARY KEY,
    company_id INTEGER NOT NULL,
    vehicle_id INTEGER,
    gps_device_id INTEGER,
    device_uid VARCHAR(50) NOT NULL,
    incident_at TIMESTAMP NOT NULL,
    latitude DOUBLE PRECISION NOT NULL,
    longitude DOUBLE PRECISION NOT NULL,
    reference_code VARCHAR(100),
    vehicle_label VARCHAR(100),
    location_commune VARCHAR(100),
    location_governorate VARCHAR(100),
    location_road_type VARCHAR(100),
    synthesis_text TEXT,
    confidence INTEGER NOT NULL DEFAULT 90,
    story JSONB,
    reasons JSONB,
    indicators JSONB,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_accident_events_company ON accident_events(company_id);
CREATE INDEX IF NOT EXISTS idx_accident_events_vehicle ON accident_events(vehicle_id);
CREATE INDEX IF NOT EXISTS idx_accident_events_incident_at ON accident_events(incident_at DESC);
