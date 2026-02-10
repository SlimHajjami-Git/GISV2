-- Debug fuel data for vehicle 1 / device 1
-- Run: docker compose -f docker-compose.prod.yml exec -T postgres psql -U postgres -d gis_v2 < services/migrations/debug_fuel_vehicle1.sql

-- 1. Vehicle + device info
SELECT v.id, v.name, v.license_plate, v.fuel_type, v.fuel_tank_capacity, v.type, v.mileage,
       d.id as device_id, d.device_uid, d.firmware_version, d.fuel_sensor_mode
FROM vehicles v
JOIN gps_devices d ON d.id = v.gps_device_id
WHERE v.id = 1;

-- 2. Fuel records today (Feb 10)
SELECT id, vehicle_id, fuel_percent, fuel_liters, fuel_change, event_type,
       odometer_km, recorded_at
FROM fuel_records
WHERE vehicle_id = 1
  AND recorded_at >= '2026-02-10 00:00:00Z'
ORDER BY recorded_at;

-- 3. GPS positions today with fuel_raw - AFTER refuel (11:00+)
SELECT id, recorded_at, speed_kph, fuel_raw, odometer_km, ignition_on
FROM gps_positions
WHERE device_id = 1
  AND recorded_at >= '2026-02-10 11:00:00Z'
ORDER BY recorded_at
LIMIT 60;

-- 4. Fuel pricing table
SELECT fp."Id", ft."Code", ft."Name", fp."PricePerLiter", fp."IsActive", fp."EffectiveFrom"
FROM fuel_pricing fp
JOIN fuel_types ft ON ft."Id" = fp."FuelTypeId"
WHERE fp."IsActive" = true;

-- 5. Check fuel_types
SELECT * FROM fuel_types;

-- 6. Check fuel_pricing (all rows)
SELECT * FROM fuel_pricing;
