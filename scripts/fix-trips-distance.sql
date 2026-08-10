-- =============================================================================
-- Réparation des trips.DistanceKm gonflées (bug ordre-d'arrivée des trames NEMS)
--
-- CONTEXTE — le TripDetector Rust cumulait la distance dans l'ordre d'ARRIVÉE
-- des trames ; or les boîtiers NEMS émettent des rafales dont la tête est la
-- position la plus récente suivie du replay du tampon : chaque rafale ajoutait
-- un aller-retour fantôme (~2× la corde). Prouvé par reproduction : trip 618
-- stocké 40,36 km, simulation ordre-d'arrivée 40,28 km (-0,19 %), réel
-- chronologique 17,7 km. Corrigé dans insert_trip (db.rs) le 2026-08-05 —
-- ce script répare le STOCK existant avec le même calcul chronologique.
--
-- USAGE (à lancer par lots — table gps_positions volumineuse) :
--   1. Prévisualiser l'ampleur :   la requête SELECT du bas
--   2. Réparer :                    le bloc UPDATE (idempotent)
-- Les trips dont la fenêtre n'a plus de positions (purge) sont laissés intacts.
-- =============================================================================

-- ── Prévisualisation : les 30 pires écarts ──
-- WITH corrected AS (
--     SELECT t."Id",
--            t."DistanceKm" AS old_km,
--            (SELECT COALESCE(SUM(
--                 CASE WHEN prev_lat IS NOT NULL AND speed_kph >= 5 AND COALESCE(ignition_on, TRUE) THEN
--                     LEAST(111.0 * SQRT(POWER(latitude - prev_lat, 2) +
--                           POWER((longitude - prev_lng) * COS(RADIANS(latitude)), 2)), 5.0)
--                 ELSE 0 END), 0)
--             FROM (SELECT latitude, longitude, speed_kph, ignition_on,
--                          LAG(latitude)  OVER (ORDER BY recorded_at) AS prev_lat,
--                          LAG(longitude) OVER (ORDER BY recorded_at) AS prev_lng
--                   FROM gps_positions gp
--                   JOIN vehicles v ON v.gps_device_id = gp.device_id
--                   WHERE v.id = t."VehicleId"
--                     AND gp.recorded_at BETWEEN t."StartTime" AND t."EndTime") seg
--            ) AS new_km
--     FROM trips t WHERE t."Status" = 'completed'
-- )
-- SELECT * FROM corrected WHERE new_km > 0.05 AND old_km > new_km * 1.2
-- ORDER BY old_km - new_km DESC LIMIT 30;

-- ── Réparation (idempotente ; relançable par périodes via le filtre StartTime) ──
UPDATE trips t
SET "DistanceKm" = c.new_km,
    "AverageSpeedKph" = CASE WHEN t."DurationMinutes" > 0
        THEN LEAST(c.new_km / (t."DurationMinutes" / 60.0), GREATEST(t."MaxSpeedKph", 1))
        ELSE t."AverageSpeedKph" END,
    "UpdatedAt" = NOW()
FROM (
    SELECT t2."Id",
           (SELECT COALESCE(SUM(
                CASE WHEN prev_lat IS NOT NULL AND speed_kph >= 5 AND COALESCE(ignition_on, TRUE) THEN
                    LEAST(111.0 * SQRT(POWER(latitude - prev_lat, 2) +
                          POWER((longitude - prev_lng) * COS(RADIANS(latitude)), 2)), 5.0)
                ELSE 0 END), 0)
            FROM (SELECT latitude, longitude, speed_kph, ignition_on,
                         LAG(latitude)  OVER (ORDER BY recorded_at) AS prev_lat,
                         LAG(longitude) OVER (ORDER BY recorded_at) AS prev_lng
                  FROM gps_positions gp
                  JOIN vehicles v ON v.gps_device_id = gp.device_id
                  WHERE v.id = t2."VehicleId"
                    AND gp.recorded_at BETWEEN t2."StartTime" AND t2."EndTime") seg
           ) AS new_km
    FROM trips t2
    WHERE t2."Status" = 'completed'
      -- Par lots si besoin : AND t2."StartTime" >= '2026-06-01' AND t2."StartTime" < '2026-07-01'
) c
WHERE c."Id" = t."Id"
  AND c.new_km > 0.05          -- fenêtre vide (positions purgées) → intact
  AND t."DistanceKm" > c.new_km * 1.05;  -- ne touche que les gonflées
