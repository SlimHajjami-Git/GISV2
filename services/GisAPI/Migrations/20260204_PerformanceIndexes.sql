-- Performance optimization indexes for GPS positions
-- Reduces query latency for real-time monitoring endpoints

-- Index for latest position queries (device + time descending)
-- Covers: GetVehiclesWithPositions, GetLatestPosition
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_gps_positions_device_recorded_desc 
ON gps_positions (device_id, recorded_at DESC) 
INCLUDE (latitude, longitude, speed_kph, course_deg, ignition_on, fuel_raw, temperature_c, address, odometer_km);

-- Index for time-range queries (stats, history)
-- Uses BRIN for efficient range scans on time-ordered data
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_gps_positions_recorded_at_brin 
ON gps_positions USING BRIN (recorded_at) WITH (pages_per_range = 128);

-- Index for device lookup by UID (info frame registration)
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_gps_devices_device_uid 
ON gps_devices (device_uid);

-- Index for vehicle-company relationship
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_vehicles_company_id 
ON vehicles (company_id) INCLUDE (name, plate, status);

-- Analyze tables to update statistics
ANALYZE gps_positions;
ANALYZE gps_devices;
ANALYZE vehicles;
