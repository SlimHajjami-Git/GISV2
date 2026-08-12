-- 030: Index partiel pour les requêtes de taux FMS (avg fuel_rate par device)
-- Seules ~54k trames sur 18M portent un fuel_rate (25 boîtiers CAN) : sans
-- index, chaque calcul de taux balayait les positions de toute la flotte sur
-- la fenêtre (21s+ à cache froid, retries EF en cascade — lenteurs TN des
-- 11-12/08). Avec l'index partiel (~2 Mo), lecture index-only en ms.
-- CONCURRENTLY : aucune interruption d'écriture pendant la création.
-- (Ne peut pas s'exécuter dans une transaction — lancer tel quel via psql.)

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_gps_positions_fms_rate
ON gps_positions (device_id, recorded_at)
INCLUDE (fuel_rate_l_per_100km)
WHERE fuel_rate_l_per_100km IS NOT NULL AND fuel_rate_l_per_100km > 0;
