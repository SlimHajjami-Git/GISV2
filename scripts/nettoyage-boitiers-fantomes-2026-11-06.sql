-- =============================================================================================
-- Nettoyage des fiches boîtiers « fantômes » — constat du 06/11/2026 (Karim)
-- À JOUER PAR SLIM, SUR SA DÉCISION. Jamais exécuté par Claude (ni en local ni en production).
-- =============================================================================================
--
-- Constat : le serveur de réception GPS (gps-ingest-rust, telemetry/hh.rs) prenait pour IMEI tout
-- le texte qui suit « IMEI: ». Des boîtiers NEMS de HERTZ ont collé à leur IMEI l'écho d'une
-- commande de réglage d'heure (« 860141076681403at+cclk="26/10/25 ») : pour chaque cas, une fiche
-- boîtier a été créée chez BELIVE (société 1), sans véhicule, et les positions de la connexion
-- s'y sont rangées au lieu du vrai boîtier. La cause est corrigée dans le même envoi (hh.rs ramène
-- l'IMEI à ses chiffres ; db.rs refuse de créer une fiche pour un identifiant non alphanumérique).
--
-- Production au 06/11/2026 (relevé en lecture seule) :
--   13 fiches fantômes (ids 344186 à 656924), du 23/07 au 06/11, issues de 5 boîtiers HERTZ :
--   222 TU 4528 (9 fiches), 233 TU 5094, 261 TU 4104, 242 TU 8177, 259 TU 9650.
--   294 positions sous ces fiches : 140 MANQUENT au vrai boîtier (222 TU 4528 : 112,
--   242 TU 8177 : 23, 261 TU 4104 : 5) ; 154 y sont déjà au même instant = doublons.
--   1 relevé de démarrage (battery_start_readings : 261 TU 4104, 25/10 à 14:53 UTC).
--   Rien d'autre ne pointe vers ces fiches (alertes, arrêts, événements, commandes, carburant,
--   remorquages, zones, accidents, journal des trames : 0).
--
-- Ce que fait ce script, dans UNE transaction :
--   1. rattache au vrai boîtier les 140 positions qui lui manquent, et le relevé de démarrage ;
--   2. supprime les 13 fiches fantômes ; leurs 154 positions en double partent avec elles
--      (clé étrangère ON DELETE CASCADE) ;
--   3. annule TOUT si un nombre diffère de l'attendu (bloc DO … RAISE EXCEPTION).
--
-- Ordre conseillé :
--   1. déployer d'abord le correctif de gps-ingest (sinon de nouvelles fiches peuvent naître :
--      sans danger, mais les nombres attendus ne tomberaient plus juste) ;
--   2. sauvegarder (fichier JSON gardé chez soi, une ligne par enregistrement) :
--        ssh belive-tn 'kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2 -Atq' < scripts/nettoyage-boitiers-fantomes-2026-11-06.sauvegarde.sql > fantomes-sauvegarde-20261106.json
--   3. ESSAI À BLANC (affiche les nombres, puis annule tout) :
--        ssh belive-tn 'kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2 -v ON_ERROR_STOP=1' < scripts/nettoyage-boitiers-fantomes-2026-11-06.sql
--   4. APPLICATION (même commande avec -v confirmer=1) :
--        ssh belive-tn 'kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2 -v ON_ERROR_STOP=1 -v confirmer=1' < scripts/nettoyage-boitiers-fantomes-2026-11-06.sql
--   Le fichier passe par l'entrée standard : le shell distant ne touche pas aux $$ du bloc DO.
--   Attendu : « NOTICE: Nettoyage : 13 fiches supprimées, 140 positions et 1 relevé(s) rattachés
--   au vrai boîtier, 154 doublons supprimés. » puis COMMIT (ou ROLLBACK pour l'essai à blanc).
--   Si de nouvelles fiches sont nées entre-temps, l'essai à blanc s'arrête en disant combien il
--   en trouve : relire les nombres et ajuster les trois SET LOCAL ci-dessous.
-- =============================================================================================

SET statement_timeout = '300s';
BEGIN;

SET LOCAL nettoyage.fiches_attendues = '13';
SET LOCAL nettoyage.positions_a_rattacher = '140';
SET LOCAL nettoyage.doublons_a_supprimer = '154';

DO $$
DECLARE
    fiches int[];
    n int;
    rattachees int;
    releves int;
    doublons int;
BEGIN
    -- Fiches fantômes : 15 chiffres suivis de « at+cclk », sans véhicule, et dont les 15 chiffres
    -- sont l'IMEI d'une AUTRE fiche (le vrai boîtier).
    SELECT array_agg(f.id ORDER BY f.id) INTO fiches
    FROM gps_devices f
    WHERE f.device_uid ~ '^[0-9]{15}at\+cclk'
      AND NOT EXISTS (SELECT 1 FROM vehicles v WHERE v.gps_device_id = f.id)
      AND EXISTS (SELECT 1 FROM gps_devices r
                  WHERE r.device_uid = substring(f.device_uid FROM 1 FOR 15) AND r.id <> f.id);

    n := coalesce(array_length(fiches, 1), 0);
    IF n <> current_setting('nettoyage.fiches_attendues')::int THEN
        RAISE EXCEPTION 'Fiches fantômes : % trouvées, % attendues. Rien n''est modifié.',
            n, current_setting('nettoyage.fiches_attendues');
    END IF;

    -- 1. Positions qui manquent au vrai boîtier. Une seule par instant : l'index unique
    --    ix_gps_positions_device_time (device_id, recorded_at) l'impose de toute façon.
    WITH a_rattacher AS (
        SELECT DISTINCT ON (r.id, p.recorded_at) p.id AS position_id, r.id AS vrai_id
        FROM gps_positions p
        JOIN gps_devices f ON f.id = p.device_id
        JOIN gps_devices r ON r.device_uid = substring(f.device_uid FROM 1 FOR 15) AND r.id <> f.id
        WHERE p.device_id = ANY (fiches)
          AND NOT EXISTS (SELECT 1 FROM gps_positions q
                          WHERE q.device_id = r.id AND q.recorded_at = p.recorded_at)
        ORDER BY r.id, p.recorded_at, p.id)
    UPDATE gps_positions p SET device_id = a.vrai_id
    FROM a_rattacher a WHERE p.id = a.position_id;
    GET DIAGNOSTICS rattachees = ROW_COUNT;
    IF rattachees <> current_setting('nettoyage.positions_a_rattacher')::int THEN
        RAISE EXCEPTION 'Positions rattachées : %, attendues %. Tout est annulé.',
            rattachees, current_setting('nettoyage.positions_a_rattacher');
    END IF;

    -- Relevés de démarrage (batterie) : même rattachement (index unique device_id + start_at).
    UPDATE battery_start_readings b SET device_id = r.id
    FROM gps_devices f
    JOIN gps_devices r ON r.device_uid = substring(f.device_uid FROM 1 FOR 15) AND r.id <> f.id
    WHERE b.device_id = f.id AND f.id = ANY (fiches)
      AND NOT EXISTS (SELECT 1 FROM battery_start_readings x
                      WHERE x.device_id = r.id AND x.start_at = b.start_at);
    GET DIAGNOSTICS releves = ROW_COUNT;

    -- 2. Ce qui reste sous les fiches est déjà sur le vrai boîtier : des doublons.
    SELECT count(*) INTO doublons FROM gps_positions WHERE device_id = ANY (fiches);
    IF doublons <> current_setting('nettoyage.doublons_a_supprimer')::int THEN
        RAISE EXCEPTION 'Doublons restants : %, attendus %. Tout est annulé.',
            doublons, current_setting('nettoyage.doublons_a_supprimer');
    END IF;

    DELETE FROM gps_devices WHERE id = ANY (fiches);
    GET DIAGNOSTICS n = ROW_COUNT;
    IF n <> current_setting('nettoyage.fiches_attendues')::int THEN
        RAISE EXCEPTION 'Fiches supprimées : %, attendues %. Tout est annulé.',
            n, current_setting('nettoyage.fiches_attendues');
    END IF;

    RAISE NOTICE 'Nettoyage : % fiches supprimées, % positions et % relevé(s) rattachés au vrai boîtier, % doublons supprimés.',
        n, rattachees, releves, doublons;
END $$;

\if :{?confirmer}
COMMIT;
\echo 'Appliqué.'
\else
ROLLBACK;
\echo 'Essai à blanc : tout est annulé. Relancer avec -v confirmer=1 pour appliquer.'
\endif

-- Contrôle : plus aucune fiche dont l'identifiant n'est pas fait de chiffres et de lettres
-- (0 ligne après application).
SELECT id, device_uid, company_id, created_at FROM gps_devices WHERE device_uid !~ '^[A-Za-z0-9]+$';
