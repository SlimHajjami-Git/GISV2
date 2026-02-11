-- Migration: Add company_id to tables missing multi-tenancy support
-- Date: 2026-02-11
-- Tables fixed: gps_alerts, geofence_events, vehicle_maintenance_schedules, maintenance_logs

-- 1. gps_alerts: Add company_id (populate from device's company)
ALTER TABLE gps_alerts ADD COLUMN IF NOT EXISTS company_id integer NOT NULL DEFAULT 0;

UPDATE gps_alerts ga SET company_id = gd.company_id
FROM gps_devices gd WHERE ga.device_id = gd.id AND ga.company_id = 0;

UPDATE gps_alerts ga SET company_id = v.company_id
FROM vehicles v WHERE ga.vehicle_id = v.id AND ga.company_id = 0;

CREATE INDEX IF NOT EXISTS idx_gps_alerts_company_id ON gps_alerts (company_id);

-- 2. geofence_events: Add company_id (populate from geofence's company)
ALTER TABLE geofence_events ADD COLUMN IF NOT EXISTS company_id integer NOT NULL DEFAULT 0;

UPDATE geofence_events ge SET company_id = g.company_id
FROM geofences g WHERE ge.geofence_id = g.id AND ge.company_id = 0;

CREATE INDEX IF NOT EXISTS idx_geofence_events_company_id ON geofence_events (company_id);

-- 3. vehicle_maintenance_schedules: Add company_id (populate from vehicle's company)
ALTER TABLE vehicle_maintenance_schedules ADD COLUMN IF NOT EXISTS company_id integer NOT NULL DEFAULT 0;

UPDATE vehicle_maintenance_schedules vms SET company_id = v.company_id
FROM vehicles v WHERE vms.vehicle_id = v.id AND vms.company_id = 0;

CREATE INDEX IF NOT EXISTS idx_vehicle_maintenance_schedules_company_id ON vehicle_maintenance_schedules (company_id);

-- 4. maintenance_logs: Add company_id (populate from vehicle's company)
ALTER TABLE maintenance_logs ADD COLUMN IF NOT EXISTS company_id integer NOT NULL DEFAULT 0;

UPDATE maintenance_logs ml SET company_id = v.company_id
FROM vehicles v WHERE ml.vehicle_id = v.id AND ml.company_id = 0;

CREATE INDEX IF NOT EXISTS idx_maintenance_logs_company_id ON maintenance_logs (company_id);
