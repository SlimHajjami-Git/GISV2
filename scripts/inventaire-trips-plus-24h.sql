-- =============================================================================
-- TRAJETS QUI NE SE SONT JAMAIS REFERMÉS (plus de 24 h) — INVENTAIRE
--
-- CONSTAT — le détecteur de trajets Rust n'a AUCUNE durée maximale, aucun
-- balayage, aucune expiration : trip_detector.rs n'expose que process_frame()
-- et active_trip_count(), et aucune tâche de fond ne le touche. La seule sortie
-- est l'ARRIVÉE d'une trame à l'arrêt avec stop_duration >= 300 s — or
-- stop_duration = now - last_moving_time devient NÉGATIF quand un boîtier émet
-- par rafales (la trame arrivée en dernier est chronologiquement la plus
-- ancienne), donc la clôture est repoussée indéfiniment ; et transport.rs jette
-- justement les trames à l'arrêt espacées de moins de 30 min, c'est-à-dire
-- celles qui fermeraient le trajet.
--
-- RÉSULTAT — le véhicule 372 porte 24 trajets de plus de 24 h, dont un de
-- 13 jours (08/09 → 21/09, 1 912 km à 6 km/h de moyenne). Ces fenêtres
-- RECOUVRENT les trajets normaux du même véhicule : le recalcul SQL de db.rs
-- est « juste au sens de la requête » mais compte tout le mouvement de la
-- fenêtre, donc le même déplacement est facturé plusieurs fois.
-- Ampleur relevée sur 30 jours : HERTZ 385 trajets > 24 h pour 158 928 km sur
-- 1 398 179 ; PARENIN 24 pour 15 235 sur 59 667 ; SICOAC 7 pour 1 209.
--
-- POURQUOI MARQUER ET NON RECALCULER — recalculer la distance de ces lignes ne
-- réparerait rien : leur fenêtre n'a pas de sens, elle englobe d'autres
-- trajets. Un chiffre recalculé leur donnerait au contraire une apparence de
-- validité. On propose donc de les SORTIR des totaux en les marquant
-- "Status" = 'distance_unverified', et de ne jamais les supprimer : la trace
-- reste consultable, la décision reste réversible.
--
-- -----------------------------------------------------------------------------
-- ORDRE IMPÉRATIF
--
--   1. DÉPLOYER D'ABORD le correctif d'ingestion (monotonie de last_moving_time
--      + clôture des trajets ouverts). Tant qu'il n'est pas en ligne, l'ingest
--      continue d'en fabriquer de nouveaux : marquer le stock ne tiendrait pas.
--   2. Réparer les distances gonflées des trajets NORMAUX :
--      scripts/fix-trips-distance.sql (qui écarte explicitement les trajets
--      de plus de 24 h, traités ici).
--   3. Lancer cet inventaire, présenter les chiffres, obtenir un accord
--      EXPLICITE avant tout marquage.
--   4. Comme pour l'autre script : TOUT CECI PASSE AVANT une purge de
--      gps_positions.
--
-- -----------------------------------------------------------------------------
-- CE QUE LE MARQUAGE CHANGE — À DIRE AU CLIENT AVANT, PAS APRÈS
--
-- Les lignes marquées SORTENT de tous les totaux, parce que tout ce qui lit la
-- table trips filtre "Status" = 'completed' :
--   • DashboardController.cs:386 et :793 — KPI de la période et km du jour ;
--   • DashboardService.cs:173 et :452 — carte « Kilométrage », classement des
--     km par véhicule, ventilation par type ;
--   • TripsController.cs:150 — écran /trips/summary (nb trajets, km, durée,
--     carburant, vitesse moyenne) ;
--   • GetMonthlyFleetReportQueryHandler.cs:470 et :1415 — rapport mensuel :
--     les km par véhicule ET les HEURES DE CONDUITE ;
--   • OperatingCostAggregator.cs:277 — base kilométrique du coût d'exploitation :
--     retirer des km sans retirer de coûts fait MONTER le coût au kilomètre ;
--   • AiChatController.cs:482 et :824 — rapports et réponses de l'assistant IA.
--
-- DONC : LES HISTORIQUES MENSUELS DÉJÀ PRÉSENTÉS AU CLIENT CHANGERONT. Des km
-- disparaîtront, des heures de conduite aussi, et le coût au km augmentera sur
-- les mois concernés. C'est le but (ces km étaient comptés deux fois), mais
-- cela doit être annoncé, chiffres à l'appui, avant le marquage.
--
-- N'EST PAS AFFECTÉ : le rapport « Trajets » de l'application
-- (GetTripsReportQueryHandler) ne lit PAS la table trips — il redétecte les
-- trajets directement depuis gps_positions. Il continuera d'afficher la même
-- chose, marquage ou non.
--
-- TECHNIQUE : "Status" est une colonne text sans contrainte CHECK et Trip.Status
-- est une simple string côté EF — la valeur 'distance_unverified' est acceptée
-- telle quelle, et aucun code de l'API ne l'écrit ni ne la réécrit.
--
-- -----------------------------------------------------------------------------
-- USAGE
--
--   # Inventaire seul (LECTURE SEULE) — toute l'histoire par défaut
--   kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2 \
--     < scripts/inventaire-trips-plus-24h.sql
--
--   # Inventaire sur une période
--   ... -v debut=2026-08-01 -v fin=2026-09-01 < scripts/inventaire-trips-plus-24h.sql
--
--   # Marquage, une fois les chiffres validés
--   ... -v debut=2026-08-01 -v fin=2026-09-01 -v marquer=1 -v lignes_attendues=385 \
--       < scripts/inventaire-trips-plus-24h.sql
--
-- SANS -v marquer=1, CE SCRIPT NE MODIFIE RIEN.
-- =============================================================================

\set ON_ERROR_STOP on
\timing on

SET TIME ZONE 'UTC';

\if :{?debut}
\else
\set debut 2000-01-01
\endif
\if :{?fin}
\else
\set fin 2100-01-01
\endif
\if :{?sauvegarde}
\else
\set sauvegarde trips_statut_backup_20260923
\endif


-- =============================================================================
-- PARTIE 1 — INVENTAIRE PAR SOCIÉTÉ (lecture seule)
--
-- Pour chaque société : les trajets de plus de 24 h, leur poids dans le total
-- de la période, et surtout le DOUBLE COMPTAGE qu'ils provoquent — c'est-à-dire
-- les trajets normaux du même véhicule dont le départ tombe DANS la fenêtre du
-- trajet long. Ces km-là sont comptés deux fois dans tous les totaux.
-- =============================================================================
\echo ''
\echo '════ 1) TRAJETS DE PLUS DE 24 H, PAR SOCIÉTÉ ════'

WITH longs AS (
    SELECT t."Id"             AS trip_id,
           t."VehicleId"      AS vehicle_id,
           t."CompanyId"      AS company_id,
           t."StartTime"      AS debut,
           t."EndTime"        AS fin,
           t."DistanceKm"     AS km
    FROM trips t
    WHERE t."Status" = 'completed'
      AND t."EndTime" IS NOT NULL
      AND t."EndTime" - t."StartTime" > interval '24 hours'
      AND t."StartTime" >= :'debut'::timestamptz
      AND t."StartTime" <  :'fin'::timestamptz
),
recouvrement AS (
    SELECT l.company_id, l.km, r.nb_recouverts, r.km_recouverts
    FROM longs l
    LEFT JOIN LATERAL (
        -- Trajets NORMAUX du même véhicule qui démarrent dans la fenêtre du
        -- trajet long : leur kilométrage est déjà inclus dans celui du long.
        SELECT count(*)                            AS nb_recouverts,
               coalesce(sum(n."DistanceKm"), 0)    AS km_recouverts
        FROM trips n
        WHERE n."VehicleId" = l.vehicle_id
          AND n."Id" <> l.trip_id
          AND n."Status" = 'completed'
          AND n."EndTime" IS NOT NULL
          AND n."StartTime" >= l.debut
          AND n."StartTime" <  l.fin
          AND n."EndTime" - n."StartTime" <= interval '24 hours'
    ) r ON TRUE
),
periode AS (
    SELECT t."CompanyId"                AS company_id,
           count(*)                     AS trajets_periode,
           sum(t."DistanceKm")          AS km_periode
    FROM trips t
    WHERE t."Status" = 'completed'
      AND t."StartTime" >= :'debut'::timestamptz
      AND t."StartTime" <  :'fin'::timestamptz
    GROUP BY 1
)
SELECT coalesce(s.name, 'société #' || r.company_id)                      AS societe,
       count(*)                                                           AS trajets_longs,
       round(sum(r.km), 1)                                                AS km_longs,
       p.trajets_periode                                                  AS trajets_periode,
       round(p.km_periode, 1)                                             AS km_periode,
       round(100.0 * sum(r.km) / nullif(p.km_periode, 0), 1)              AS pct_km_longs,
       sum(r.nb_recouverts)                                               AS trajets_recouverts,
       round(sum(r.km_recouverts), 1)                                     AS km_comptes_deux_fois
FROM recouvrement r
JOIN periode p       ON p.company_id = r.company_id
LEFT JOIN societes s ON s.id = r.company_id
GROUP BY s.name, r.company_id, p.trajets_periode, p.km_periode
ORDER BY 3 DESC NULLS LAST;

\echo ''
\echo 'km_comptes_deux_fois = km de trajets normaux déjà inclus dans la fenêtre du trajet long.'


-- =============================================================================
-- PARTIE 2 — LES 30 PLUS LONGS, EN DÉTAIL (lecture seule)
-- =============================================================================
\echo ''
\echo '════ 2) LES 30 TRAJETS LES PLUS LONGS ════'

WITH longs AS (
    SELECT t."Id"                                  AS trip_id,
           t."VehicleId"                           AS vehicle_id,
           t."CompanyId"                           AS company_id,
           t."StartTime"                           AS debut,
           t."EndTime"                             AS fin,
           t."DistanceKm"                          AS km,
           t."AverageSpeedKph"                     AS vmoy,
           EXTRACT(EPOCH FROM (t."EndTime" - t."StartTime")) / 86400.0 AS jours
    FROM trips t
    WHERE t."Status" = 'completed'
      AND t."EndTime" IS NOT NULL
      AND t."EndTime" - t."StartTime" > interval '24 hours'
      AND t."StartTime" >= :'debut'::timestamptz
      AND t."StartTime" <  :'fin'::timestamptz
    ORDER BY t."EndTime" - t."StartTime" DESC
    LIMIT 30
)
SELECT l.trip_id,
       coalesce(nullif(v.plate_number, ''), v.name, 'véhicule #' || l.vehicle_id) AS vehicule,
       coalesce(s.name, 'société #' || l.company_id)                              AS societe,
       to_char(l.debut, 'YYYY-MM-DD HH24:MI')                                     AS debut,
       to_char(l.fin,   'YYYY-MM-DD HH24:MI')                                     AS fin,
       round(l.jours::numeric, 1)                                                 AS jours,
       round(l.km, 1)                                                             AS km,
       l.vmoy                                                                     AS vitesse_moy,
       r.nb_recouverts                                                            AS trajets_recouverts,
       round(r.km_recouverts, 1)                                                  AS km_recouverts
FROM longs l
LEFT JOIN vehicles v ON v.id = l.vehicle_id
LEFT JOIN societes s ON s.id = l.company_id
LEFT JOIN LATERAL (
    SELECT count(*)                         AS nb_recouverts,
           coalesce(sum(n."DistanceKm"), 0) AS km_recouverts
    FROM trips n
    WHERE n."VehicleId" = l.vehicle_id
      AND n."Id" <> l.trip_id
      AND n."Status" = 'completed'
      AND n."EndTime" IS NOT NULL
      AND n."StartTime" >= l.debut
      AND n."StartTime" <  l.fin
      AND n."EndTime" - n."StartTime" <= interval '24 hours'
) r ON TRUE
ORDER BY l.jours DESC;

\echo ''
\echo 'Vitesse moyenne très basse sur plusieurs jours = signature du trajet jamais clôturé.'


-- =============================================================================
-- PARTIE 3 — MARQUAGE "Status" = 'distance_unverified' (écriture, INERTE)
--
-- Ne s'exécute qu'avec -v marquer=1 ET -v lignes_attendues=<n>, où n est le
-- nombre de trajets longs lu dans la partie 1 pour la période visée.
--
-- RAPPEL : ces lignes sortiront de TOUS les totaux (liste des lecteurs en tête
-- de fichier), donc les historiques mensuels déjà présentés au client
-- CHANGERONT. Ne lancer qu'après accord explicite, chiffres en main.
--
-- On ne supprime rien : aucune ligne de trips n'est effacée, ni ici ni ailleurs
-- dans ce script. Le marquage est entièrement réversible (partie 5).
-- =============================================================================
\if :{?marquer}
\if :{?lignes_attendues}

\echo ''
\echo '════ 3) MARQUAGE ════'

BEGIN;

SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '300s';
SET LOCAL gisv2.lignes_attendues = :'lignes_attendues';
SET LOCAL gisv2.sauvegarde       = :'sauvegarde';

CREATE TEMP TABLE marquage_cible ON COMMIT DROP AS
SELECT t."Id"        AS trip_id,
       t."CompanyId" AS company_id,
       t."DistanceKm" AS km,
       t."EndTime"   AS fin
FROM trips t
WHERE t."Status" = 'completed'
  AND t."EndTime" IS NOT NULL
  AND t."EndTime" - t."StartTime" > interval '24 hours'
  AND t."StartTime" >= :'debut'::timestamptz
  AND t."StartTime" <  :'fin'::timestamptz;

ALTER TABLE marquage_cible ADD PRIMARY KEY (trip_id);

-- ── Sauvegarde AVANT écriture ────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS :sauvegarde (
    trip_id       bigint PRIMARY KEY,
    sauvegarde_a  timestamptz NOT NULL DEFAULT now(),
    lot           text        NOT NULL,
    statut_avant  text        NOT NULL,
    ligne         jsonb       NOT NULL
);

COMMENT ON TABLE :sauvegarde IS
    'Sauvegarde des lignes trips marquées distance_unverified par scripts/inventaire-trips-plus-24h.sql (trajets jamais clôturés). Ne pas supprimer sans accord : c''est le seul moyen de revenir en arrière.';

INSERT INTO :sauvegarde (trip_id, lot, statut_avant, ligne)
SELECT c.trip_id,
       :'debut' || ' → ' || :'fin',
       t."Status",
       to_jsonb(t)
FROM marquage_cible c
JOIN trips t ON t."Id" = c.trip_id
ON CONFLICT (trip_id) DO NOTHING;

DO $$
DECLARE
    v_cible        bigint;
    v_attendu      bigint;
    v_sauvegardees bigint;
    v_touchees     bigint;
    v_plus_recent  timestamptz;
    v_km           numeric;
BEGIN
    v_attendu := current_setting('gisv2.lignes_attendues')::bigint;

    SELECT count(*), max(fin), round(sum(km), 1)
      INTO v_cible, v_plus_recent, v_km
      FROM marquage_cible;

    IF v_attendu <= 0 THEN
        RAISE EXCEPTION
            'Garde-fou : lignes_attendues vaut %. Lancer d''abord l''INVENTAIRE (partie 1) et reporter ici le nombre de trajets longs de la période. Aucune modification.',
            v_attendu;
    END IF;

    IF v_cible <> v_attendu THEN
        RAISE EXCEPTION
            'Garde-fou : % trajet(s) visé(s), % attendu(s). La base a bougé depuis l''inventaire (ou les bornes ne sont pas les mêmes). Relancer l''inventaire. Aucune modification.',
            v_cible, v_attendu;
    END IF;

    -- Un trajet encore ouvert côté ingest peut être clôturé d'une seconde à
    -- l'autre : on ne marque que ce qui est stabilisé depuis 24 h.
    IF v_plus_recent > now() - interval '24 hours' THEN
        RAISE EXCEPTION
            'Garde-fou : le lot contient un trajet terminé le %, il y a moins de 24 h. Reculer la borne « fin ». Aucune modification.',
            v_plus_recent;
    END IF;

    EXECUTE format('SELECT count(*) FROM %I b JOIN marquage_cible c ON c.trip_id = b.trip_id',
                   current_setting('gisv2.sauvegarde'))
       INTO v_sauvegardees;

    IF v_sauvegardees <> v_cible THEN
        RAISE EXCEPTION
            'Garde-fou : % ligne(s) sauvegardée(s) pour % visée(s). On n''écrit pas sans filet complet. Aucune modification.',
            v_sauvegardees, v_cible;
    END IF;

    UPDATE trips t
       SET "Status"    = 'distance_unverified',
           "UpdatedAt" = now()
      FROM marquage_cible c
     WHERE t."Id" = c.trip_id
       AND t."Status" = 'completed';

    GET DIAGNOSTICS v_touchees = ROW_COUNT;

    IF v_touchees <> v_cible THEN
        RAISE EXCEPTION
            'Garde-fou : % ligne(s) marquée(s) pour % visée(s). Incohérence inexpliquée : tout est annulé.',
            v_touchees, v_cible;
    END IF;

    RAISE NOTICE 'Marquage : % trajet(s) sortis des totaux, soit % km retirés des historiques. Sauvegarde dans %.',
        v_touchees, v_km, current_setting('gisv2.sauvegarde');
END $$;

COMMIT;

\else
\echo ''
\echo 'ERREUR : -v marquer=1 exige aussi -v lignes_attendues=<n>, lu en partie 1. Aucune modification.'
\endif
\else
\echo ''
\echo '(Mode lecture seule : aucun marquage. Ajouter -v marquer=1 -v lignes_attendues=<n> pour marquer.)'
\endif


-- =============================================================================
-- PARTIE 4 — VÉRIFICATION APRÈS MARQUAGE (lecture seule)
--
-- À lancer après le marquage ET à comparer avec les chiffres présentés au
-- client : c'est exactement ce que les totaux perdent.
-- =============================================================================
\echo ''
\echo '════ 4) VÉRIFICATION — ce que les totaux perdent ════'

-- Tant qu'aucun marquage n'a été lancé, la table de sauvegarde n'existe pas :
-- on le constate au lieu de planter sur ON_ERROR_STOP.
SELECT CASE WHEN to_regclass(:'sauvegarde') IS NULL THEN 'false' ELSE 'true' END AS sauvegarde_existe \gset

\if :sauvegarde_existe

SELECT coalesce(s.name, 'société #' || t."CompanyId")                        AS societe,
       count(*)                                                              AS lignes_marquees,
       round(sum(t."DistanceKm"), 1)                                         AS km_sortis_des_totaux,
       round(sum(t."DurationMinutes") / 60.0, 1)                             AS heures_sorties,
       to_char(min(t."StartTime"), 'YYYY-MM-DD')                             AS du,
       to_char(max(t."StartTime"), 'YYYY-MM-DD')                             AS au,
       count(*) FILTER (WHERE t."Status" <> 'distance_unverified')           AS lignes_divergentes
FROM :sauvegarde b
JOIN trips t         ON t."Id" = b.trip_id
LEFT JOIN societes s ON s.id = t."CompanyId"
GROUP BY 1
ORDER BY 3 DESC NULLS LAST;

\echo ''
\echo 'lignes_divergentes doit valoir 0. Relancer la partie 1 : elle doit rendre 0 trajet long sur la période marquée.'

\else
\echo '(Aucune table de sauvegarde : marquage jamais lancé, donc rien à vérifier.)'
\endif


-- =============================================================================
-- PARTIE 5 — RETOUR EN ARRIÈRE (volontairement INERTE : à décommenter)
--
-- Remet le statut d'origine et fait revenir les lignes dans tous les totaux.
-- Aucun DROP, aucun DELETE : la table de sauvegarde reste en place.
-- =============================================================================
--
-- BEGIN;
-- SET LOCAL lock_timeout = '5s';
-- SET LOCAL statement_timeout = '300s';
--
-- UPDATE trips t
--    SET "Status"    = b.statut_avant,
--        "UpdatedAt" = now()
--   FROM :sauvegarde b
--  WHERE t."Id" = b.trip_id
--    AND t."Status" = 'distance_unverified';   -- ne défait que ce qu'on a écrit
--
-- COMMIT;
