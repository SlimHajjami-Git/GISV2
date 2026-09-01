-- 038 — Rattrapage du kilométrage des véhicules depuis les relevés au plein.
--
-- Contexte (recette client du 25/08/2026, partie « sans GPS ») : pour un
-- véhicule sans boîtier, le compteur (vehicles.mileage) doit refléter le
-- DERNIER relevé saisi au plein. Le mécanisme est en place depuis le lot
-- précédent (CreateFuelEntryCommandHandler fait avancer le compteur à chaque
-- nouveau plein), mais les pleins ENREGISTRÉS AVANT ce correctif n'avaient pas
-- mis le compteur à jour — d'où un véhicule affiché à 48 285 km alors que son
-- dernier plein était à 52 220 km.
--
-- Ce backfill est NON DESTRUCTIF : GREATEST ne fait qu'AVANCER le compteur au
-- plus haut relevé connu, jamais reculer. Un véhicule dont le boîtier remonte
-- déjà un kilométrage supérieur n'est pas touché. Idempotent.

UPDATE vehicles v
SET mileage = sub.max_km,
    updated_at = now()
FROM (
    SELECT f.vehicle_id, MAX(f.odometer_km) AS max_km
    FROM fuel_entries f
    WHERE f.odometer_km IS NOT NULL AND f.odometer_km > 0
    GROUP BY f.vehicle_id
) sub
WHERE v.id = sub.vehicle_id
  AND sub.max_km > v.mileage;
