-- Détection des doublons : SIM (numéro tél), IMEI, matricule boîtier, immatriculation.
-- Valeurs normalisées (espaces/casse) pour attraper "216 20 123 456" vs "21620123456".
-- Usage : kubectl exec -n gisv2 -i postgres-0 -- psql -U postgres -d gis_v2 < scripts/find-doublons.sql

\echo ''
\echo '════ 1) DOUBLONS NUMÉRO TEL (SIM) — gps_devices ════'
SELECT regexp_replace(TRIM(sim_number), '\s', '', 'g') AS sim,
       COUNT(*) AS nb,
       string_agg('id='||id||' imei='||device_uid||' mat='||COALESCE(mat,'-')||' societe='||COALESCE(company_id::text,'-'), '  |  ' ORDER BY id) AS appareils
FROM gps_devices
WHERE sim_number IS NOT NULL AND TRIM(sim_number) <> ''
GROUP BY 1 HAVING COUNT(*) > 1 ORDER BY nb DESC;

\echo ''
\echo '════ 2) DOUBLONS IMEI — gps_devices (index unique : seules des variantes d''écriture possibles) ════'
SELECT regexp_replace(TRIM(device_uid), '\s', '', 'g') AS imei,
       COUNT(*) AS nb,
       string_agg('id='||id||' mat='||COALESCE(mat,'-')||' societe='||COALESCE(company_id::text,'-'), '  |  ' ORDER BY id) AS appareils
FROM gps_devices
GROUP BY 1 HAVING COUNT(*) > 1 ORDER BY nb DESC;

\echo ''
\echo '════ 3) DOUBLONS MAT (matricule boîtier) — gps_devices ════'
SELECT UPPER(regexp_replace(TRIM(mat), '\s+', ' ', 'g')) AS mat,
       COUNT(*) AS nb,
       string_agg('id='||id||' imei='||device_uid||' sim='||COALESCE(sim_number,'-')||' societe='||COALESCE(company_id::text,'-'), '  |  ' ORDER BY id) AS appareils
FROM gps_devices
WHERE mat IS NOT NULL AND TRIM(mat) <> ''
GROUP BY 1 HAVING COUNT(*) > 1 ORDER BY nb DESC;

\echo ''
\echo '════ 4) DOUBLONS IMMATRICULATION — vehicles (plate_number) ════'
SELECT UPPER(regexp_replace(TRIM(plate_number), '\s+', ' ', 'g')) AS plaque,
       COUNT(*) AS nb,
       string_agg('id='||id||' societe='||COALESCE(company_id::text,'-'), '  |  ' ORDER BY id) AS vehicules
FROM vehicles
WHERE plate_number IS NOT NULL AND TRIM(plate_number) <> ''
GROUP BY 1 HAVING COUNT(*) > 1 ORDER BY nb DESC;
