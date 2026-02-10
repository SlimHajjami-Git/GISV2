-- Debug fuel data for vehicle 1 / device 1
-- Run with: docker exec -i gisv2-postgres psql -U gisadmin -d gisdb < services/migrations/debug_fuel_vehicle1.sql

-- 1. Vehicle + device info
SELECT v.id, v.name, v.plate, v.fuel_type, v.fuel_tank_capacity, v.type, v.mileage,
       d.id as device_id, d.device_uid, d.firmware_version, d.fuel_sensor_mode
FROM vehicles v
JOIN gps_devices d ON d.id = v.gps_device_id
WHERE v.id = 1;

-- 2. Fuel records today (Feb 10)
SELECT id, vehicle_id, fuel_percent, fuel_liters, fuel_change, event_type, 
       odometer_km, latitude, longitude, recorded_at
FROM fuel_records 
WHERE vehicle_id = 1 
  AND recorded_at >= '2026-02-10 00:00:00Z'
ORDER BY recorded_at;

-- 3. Fuel records yesterday (for context)
SELECT id, fuel_percent, fuel_change, event_type, odometer_km, recorded_at
FROM fuel_records 
WHERE vehicle_id = 1 
  AND recorded_at >= '2026-02-09 00:00:00Z'
  AND recorded_at < '2026-02-10 00:00:00Z'
ORDER BY recorded_at;

-- 4. GPS positions today with fuel_raw (sample)
SELECT id, recorded_at, latitude, longitude, speed_kph, fuel_raw, odometer_km, ignition_on
FROM gps_positions 
WHERE device_id = 1 
  AND recorded_at >= '2026-02-10 00:00:00Z'
ORDER BY recorded_at
LIMIT 50;

-- 5. Active fuel pricing
SELECT fp.id, ft.code, ft.name, fp.price_per_liter, fp.is_active, fp.effective_from
FROM fuel_pricings fp
JOIN fuel_types ft ON ft.id = fp.fuel_type_id
WHERE fp.is_active = true;
