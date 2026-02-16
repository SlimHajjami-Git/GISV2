-- Requête SQL: Positions d'un véhicule entre le 12/02/2026 et le 13/02/2026
-- Table: gps_positions (EF Core snake_case mapping)
-- Join: gps_devices → vehicles pour identifier le véhicule

SELECT 
    p.id,
    p.device_id,
    p.recorded_at,
    p.latitude,
    p.longitude,
    p.speed_kph,
    p.course_deg,
    p.altitude_m,
    p.ignition_on,
    p.fuel_raw,
    p.odometer_km,
    p.temperature_c,
    p.satellites,
    p.is_valid,
    p.address,
    v.id AS vehicle_id,
    v.name AS vehicle_name,
    v.license_plate
FROM gps_positions p
JOIN gps_devices d ON d.id = p.device_id
JOIN vehicles v ON v.gps_device_id = d.id
WHERE p.recorded_at >= '2026-02-12 00:00:00'
  AND p.recorded_at <  '2026-02-14 00:00:00'
  AND v.id = :vehicle_id          -- Remplacer par l'ID du véhicule souhaité
  AND p.is_valid = true
ORDER BY p.recorded_at ASC;

-- Variante: Toutes les positions de TOUS les véhicules sur cette période
-- SELECT 
--     v.name, v.license_plate, p.recorded_at, p.latitude, p.longitude, 
--     p.speed_kph, p.ignition_on
-- FROM gps_positions p
-- JOIN gps_devices d ON d.id = p.device_id
-- JOIN vehicles v ON v.gps_device_id = d.id
-- WHERE p.recorded_at >= '2026-02-12 00:00:00'
--   AND p.recorded_at <  '2026-02-14 00:00:00'
--   AND p.is_valid = true
-- ORDER BY v.id, p.recorded_at ASC;
