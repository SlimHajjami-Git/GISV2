-- Sauvegarde AVANT scripts/nettoyage-boitiers-fantomes-2026-11-06.sql — LECTURE SEULE.
--
-- Lancement (sortie = une ligne JSON par enregistrement, à garder chez soi) :
--   ssh belive-tn 'kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2 -Atq' < scripts/nettoyage-boitiers-fantomes-2026-11-06.sauvegarde.sql > fantomes-sauvegarde-20261106.json
--
-- Attendu au 06/11/2026 : 13 lignes gps_devices, 294 lignes gps_positions, 1 ligne
-- battery_start_readings (308 lignes en tout).
--
-- Restauration éventuelle : réinsérer chaque « ligne » dans sa « table » avec
-- json_populate_record (les fiches gps_devices d'abord), et remettre device_id à sa valeur
-- sauvegardée pour les positions et le relevé rattachés au vrai boîtier (mêmes id).

SET statement_timeout = '300s';
SET default_transaction_read_only = on;

SELECT coalesce(string_agg(id::text, ','), '0') AS fiches
FROM gps_devices WHERE device_uid ~ '^[0-9]{15}at\+cclk' \gset

SELECT json_build_object('table', 'gps_devices', 'ligne', row_to_json(f))
FROM gps_devices f WHERE f.id IN (:fiches);

SELECT json_build_object('table', 'gps_positions', 'ligne', row_to_json(p))
FROM gps_positions p WHERE p.device_id IN (:fiches);

SELECT json_build_object('table', 'battery_start_readings', 'ligne', row_to_json(b))
FROM battery_start_readings b WHERE b.device_id IN (:fiches);
