-- Migration: Geofencing & POI Improvements
-- Date: 2024-12-31
-- Description: Add new columns for geofencing features, POI visits tracking, and geofence groups

-- ============================================
-- 1. GEOFENCE TABLE UPDATES
-- ============================================

-- Add new columns to geofences table
ALTER TABLE geofences 
ADD COLUMN IF NOT EXISTS icon_name VARCHAR(50),
ADD COLUMN IF NOT EXISTS notification_cooldown_minutes INTEGER DEFAULT 5,
ADD COLUMN IF NOT EXISTS max_stay_duration_minutes INTEGER,
ADD COLUMN IF NOT EXISTS active_start_time TIME,
ADD COLUMN IF NOT EXISTS active_end_time TIME,
ADD COLUMN IF NOT EXISTS active_days JSONB,
ADD COLUMN IF NOT EXISTS group_id INTEGER;

-- ============================================
-- 2. GEOFENCE EVENTS TABLE UPDATES
-- ============================================

-- Add new columns to geofence_events table
ALTER TABLE geofence_events
ADD COLUMN IF NOT EXISTS device_id INTEGER,
ADD COLUMN IF NOT EXISTS address VARCHAR(500),
ADD COLUMN IF NOT EXISTS duration_inside_seconds INTEGER,
ADD COLUMN IF NOT EXISTS is_notified BOOLEAN DEFAULT FALSE,
ADD COLUMN IF NOT EXISTS notified_at TIMESTAMP;

-- ============================================
-- 3. GEOFENCE GROUPS TABLE (NEW)
-- ============================================

CREATE TABLE IF NOT EXISTS geofence_groups (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description VARCHAR(500),
    color VARCHAR(20) DEFAULT '#6b7280',
    icon_name VARCHAR(50),
    company_id INTEGER NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
    created_at TIMESTAMP DEFAULT NOW()
);

-- Add foreign key from geofences to geofence_groups
ALTER TABLE geofences
ADD CONSTRAINT fk_geofences_group
FOREIGN KEY (group_id) REFERENCES geofence_groups(id) ON DELETE SET NULL;

-- ============================================
-- 4. POINTS OF INTEREST TABLE UPDATES
-- ============================================

-- Add new columns to points_of_interest table
ALTER TABLE points_of_interest
ADD COLUMN IF NOT EXISTS radius DOUBLE PRECISION DEFAULT 50,
ADD COLUMN IF NOT EXISTS contact_name VARCHAR(100),
ADD COLUMN IF NOT EXISTS external_id VARCHAR(100),
ADD COLUMN IF NOT EXISTS alert_on_arrival BOOLEAN DEFAULT FALSE,
ADD COLUMN IF NOT EXISTS alert_on_departure BOOLEAN DEFAULT FALSE,
ADD COLUMN IF NOT EXISTS expected_stay_minutes INTEGER,
ADD COLUMN IF NOT EXISTS notification_cooldown_minutes INTEGER DEFAULT 5,
ADD COLUMN IF NOT EXISTS tags JSONB,
ADD COLUMN IF NOT EXISTS visit_count INTEGER DEFAULT 0,
ADD COLUMN IF NOT EXISTS last_visit_at TIMESTAMP;

-- ============================================
-- 5. POI VISITS TABLE (NEW)
-- ============================================

CREATE TABLE IF NOT EXISTS poi_visits (
    id SERIAL PRIMARY KEY,
    poi_id INTEGER NOT NULL,
    vehicle_id INTEGER NOT NULL,
    device_id INTEGER,
    arrival_at TIMESTAMP NOT NULL,
    departure_at TIMESTAMP,
    duration_minutes INTEGER,
    arrival_lat DOUBLE PRECISION NOT NULL,
    arrival_lng DOUBLE PRECISION NOT NULL,
    departure_lat DOUBLE PRECISION,
    departure_lng DOUBLE PRECISION,
    notes VARCHAR(500),
    is_notified BOOLEAN DEFAULT FALSE,
    company_id INTEGER NOT NULL,
    CONSTRAINT fk_poi_visits_poi FOREIGN KEY (poi_id) REFERENCES points_of_interest(id) ON DELETE CASCADE,
    CONSTRAINT fk_poi_visits_vehicle FOREIGN KEY (vehicle_id) REFERENCES vehicles(id) ON DELETE CASCADE
);

-- ============================================
-- 6. INDEXES
-- ============================================

-- Geofence events indexes
CREATE INDEX IF NOT EXISTS idx_geofence_events_geofence_timestamp 
ON geofence_events(geofence_id, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_geofence_events_vehicle_timestamp 
ON geofence_events(vehicle_id, timestamp DESC);

-- Geofence groups index
CREATE INDEX IF NOT EXISTS idx_geofence_groups_company_id 
ON geofence_groups(company_id);

-- POI index
CREATE INDEX IF NOT EXISTS idx_poi_company_id 
ON points_of_interest(company_id);

-- POI visits indexes
CREATE INDEX IF NOT EXISTS idx_poi_visits_poi_arrival 
ON poi_visits(poi_id, arrival_at DESC);

CREATE INDEX IF NOT EXISTS idx_poi_visits_vehicle_arrival 
ON poi_visits(vehicle_id, arrival_at DESC);

CREATE INDEX IF NOT EXISTS idx_poi_visits_company_id 
ON poi_visits(company_id);

-- ============================================
-- 7. COMMENTS
-- ============================================

COMMENT ON COLUMN geofences.notification_cooldown_minutes IS 'Minimum minutes between notifications for same vehicle';
COMMENT ON COLUMN geofences.max_stay_duration_minutes IS 'Alert if vehicle stays longer than this';
COMMENT ON COLUMN geofences.active_start_time IS 'Zone only active after this time';
COMMENT ON COLUMN geofences.active_end_time IS 'Zone only active until this time';
COMMENT ON COLUMN geofences.active_days IS 'Days when zone is active: ["Mon","Tue","Wed","Thu","Fri"]';

COMMENT ON COLUMN geofence_events.duration_inside_seconds IS 'For exit events - time spent inside zone';
COMMENT ON COLUMN geofence_events.is_notified IS 'Whether notification was sent';

COMMENT ON COLUMN points_of_interest.radius IS 'Detection radius in meters';
COMMENT ON COLUMN points_of_interest.tags IS 'Tags for filtering: ["prioritaire", "24h"]';
COMMENT ON COLUMN points_of_interest.visit_count IS 'Total number of visits';

COMMENT ON TABLE poi_visits IS 'History of vehicle visits to points of interest';
COMMENT ON TABLE geofence_groups IS 'Optional grouping for geofences';
