-- =============================================================================
-- RÉPARATION DU STOCK trips."DistanceKm" GONFLÉ — script lancé À LA MAIN
--
-- CONSTAT — le tableau de bord annonçait 58 597 km sur 30 jours pour 263 TU 6995
-- (PARENIN) là où la mesure réelle donne ~3 000 km : facteur 19. Le tableau de
-- bord somme trips."DistanceKm", et cette colonne a été écrite gonflée par deux
-- chemins distincts :
--
--   • AVANT le 2026-08-05, insert_trip stockait directement la distance cumulée
--     par le TripDetector dans l'ordre d'ARRIVÉE des trames. Or les boîtiers
--     émettent par rafales (tête = position la plus récente, puis rejeu du
--     tampon) : chaque rafale ajoutait un aller-retour fantôme (~2× la corde).
--     Prouvé par reproduction : trajet 618 stocké à 40,36 km, simulation en
--     ordre d'arrivée 40,28 km (-0,19 %), réel chronologique 17,7 km.
--
--   • DEPUIS le 2026-08-05, insert_trip recalcule en SQL, en ordre
--     chronologique (db.rs, « WITH ordered AS »), MAIS retombe sur la valeur du
--     détecteur quand ce recalcul rend moins de 0,05 km (db.rs:997-999). Ce
--     repli est bien emprunté : le trajet 502679 du 2026-09-09 est stocké à
--     1 033 km pour 199,6 km réels.
--
-- CE QUE FAIT CE SCRIPT — il rejoue le MÊME calcul chronologique que db.rs
-- (plafond 5,0 km par segment, speed_kph >= 5, COALESCE(ignition_on, TRUE)) sur
-- la fenêtre de chaque trajet, et n'écrit que si le résultat est PLUS PETIT que
-- la valeur stockée. Sur un trajet écrit par le chemin nominal après le
-- 2026-08-05, il est un NO-OP : même formule, mêmes constantes.
--
-- CE QU'IL NE FAIT PAS :
--   • Il ne touche PAS aux trajets de plus de 24 h. Ces trajets-là ne se sont
--     jamais refermés : leur fenêtre RECOUVRE les trajets voisins, et les
--     recalculer donnerait un chiffre « juste au sens de la requête » qui
--     compte plusieurs fois le même déplacement. Voir le script frère
--     scripts/inventaire-trips-plus-24h.sql.
--   • Il ne répare PAS les trajets dont les positions ont été purgées : la
--     fenêtre est vide, il n'y a plus rien à recalculer. CES LIGNES RESTERONT
--     FAUSSES POUR TOUJOURS — c'est un choix (ne jamais écrire un zéro qui
--     ferait disparaître un trajet), pas un oubli. La section 4 de la
--     prévisualisation les compte, pour qu'on sache combien on abandonne.
--   • Il ne répare PAS les trajets À FIN TRONQUÉE (motif H de la section 4).
--     Ce sont précisément ceux qui sont passés par le repli de db.rs : leur
--     "EndTime" est antérieur à la vraie fin, donc leur FENÊTRE ELLE-MÊME est
--     fausse. Les recalculer écrirait une distance ridiculement PETITE à la
--     place d'une distance ridiculement GRANDE — tout aussi faux, et plus
--     difficile à détecter ensuite. On les détecte par un fait physique : un
--     trajet ne se clôture que sur un arrêt d'au moins 5 minutes, donc si la
--     trame qui suit la fin enregistrée montre le véhicule EN TRAIN DE ROULER,
--     la fin est tronquée. CES LIGNES-LÀ NON PLUS NE SERONT JAMAIS RÉPARABLES
--     par recalcul : leur seule issue est le marquage 'distance_unverified',
--     comme pour les trajets de plus de 24 h — décision à prendre avec Slim,
--     chiffres de la section 4 en main. Aucun script ne la prend à sa place.
--
-- -----------------------------------------------------------------------------
-- ORDRE IMPÉRATIF — à lire avant de lancer quoi que ce soit
--
--   1. DÉPLOYER D'ABORD le correctif d'ingestion (monotonie de last_moving_time
--      + clôture des trajets qui ne se ferment jamais). Sans lui, le stock se
--      regonfle derrière le script : on répare le passé pendant que le présent
--      se recasse.
--   2. Lancer la PRÉVISUALISATION (partie 1), présenter les chiffres, obtenir
--      un accord EXPLICITE. Un « continue » n'est pas une autorisation.
--   3. Réparer PAR LOTS mensuels, du plus ancien au plus récent.
--   4. RÉPARER AVANT TOUTE PURGE DE gps_positions. Une fenêtre vidée rend le
--      trajet irréparable définitivement : le garde-fou laissera la ligne
--      fausse, et plus aucun script ne pourra retrouver le vrai chiffre. Si une
--      purge de gps_positions est prévue, ce script passe AVANT.
--   5. Les trajets de plus de 24 h se traitent ensuite, avec l'autre script.
--
-- -----------------------------------------------------------------------------
-- USAGE
--
--   # 1. Prévisualisation (LECTURE SEULE, rien n'est modifié)
--   kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2 \
--     -v debut=2026-08-01 -v fin=2026-09-01 \
--     < scripts/fix-trips-distance.sql
--
--   # 2. Réparation du même lot, une fois les chiffres validés
--   #    lignes_attendues = le nombre lu en section « 1 · TOTAL À RÉPARER »
--   kubectl exec -i postgres-0 -n gisv2 -- psql -U postgres -d gis_v2 \
--     -v debut=2026-08-01 -v fin=2026-09-01 \
--     -v reparer=1 -v lignes_attendues=1234 \
--     < scripts/fix-trips-distance.sql
--
-- SANS -v reparer=1, LE SCRIPT NE MODIFIE RIEN : la partie qui écrit est
-- derrière ce garde, et elle refuse en plus de démarrer si lignes_attendues
-- n'est pas renseigné. Une exécution distraite du fichier entier ne change
-- aucune donnée.
-- =============================================================================

\set ON_ERROR_STOP on
\timing on

-- Fuseau explicite : trips."StartTime"/"EndTime" et gps_positions.recorded_at
-- sont des timestamptz ; les bornes du lot sont écrites en UTC, comme l'ingest.
SET TIME ZONE 'UTC';

\if :{?debut}
\else
\echo 'ERREUR : passer -v debut=YYYY-MM-DD et -v fin=YYYY-MM-DD (bornes du lot). Aucune modification.'
\quit
\endif
\if :{?fin}
\else
\echo 'ERREUR : passer -v fin=YYYY-MM-DD (borne haute du lot). Aucune modification.'
\quit
\endif

-- Nom de la table de sauvegarde. Un nom par campagne de réparation (la date du
-- jour convient) : les lots mensuels successifs s'y empilent, un trajet n'y
-- entre qu'une fois (PRIMARY KEY + ON CONFLICT DO NOTHING), donc la valeur
-- gardée est TOUJOURS la valeur d'origine, même si on relance.
\if :{?sauvegarde}
\else
\set sauvegarde trips_distance_backup_20260923
\endif


-- =============================================================================
-- PARTIE 0 — CONTRÔLE DES COLONNES (lecture seule)
--
-- Les conventions de nommage sont incohérentes dans cette base : trips est en
-- PascalCase entre guillemets, gps_positions et vehicles en snake_case. On
-- vérifie que les colonnes lues existent bien avant de leur faire confiance.
-- =============================================================================
\echo ''
\echo '════ 0) COLONNES UTILISÉES PAR CE SCRIPT ════'

SELECT table_name, column_name, data_type
FROM information_schema.columns
WHERE (table_name = 'trips'         AND column_name IN ('Id','VehicleId','CompanyId','StartTime','EndTime',
                                                        'StartLatitude','StartLongitude','EndLatitude','EndLongitude',
                                                        'DistanceKm','DurationMinutes','AverageSpeedKph','MaxSpeedKph',
                                                        'Status','UpdatedAt'))
   OR (table_name = 'gps_positions' AND column_name IN ('device_id','recorded_at','latitude','longitude','speed_kph','ignition_on'))
   OR (table_name = 'vehicles'      AND column_name IN ('id','company_id','gps_device_id','plate_number','name'))
ORDER BY table_name, column_name;


-- =============================================================================
-- PARTIE 1 — PRÉVISUALISATION (LECTURE SEULE)
--
-- Une seule requête, un seul passage sur gps_positions, quatre sections :
--   1 · le total à réparer,
--   2 · la ventilation PAR SOCIÉTÉ,
--   3 · les 30 pires écarts,
--   4 · ce que les garde-fous ÉCARTENT, et pourquoi.
--
-- La section 4 est la plus importante à lire : elle dit tout ce que le script
-- renonce à réparer. Mieux vaut réparer moins que réparer faux.
-- =============================================================================
\echo ''
\echo '════ 1) PRÉVISUALISATION — aucune écriture ════'

-- ⚠ LE BLOC D'ANALYSE CI-DESSOUS (candidats → mesure → ecarts → qualifie) EST
-- REPRIS DANS LA PARTIE 2, mêmes conditions et même ordre (seuls les libellés
-- y sont abrégés). Toute modification ici doit être reportée là-bas, sinon on
-- répare autre chose que ce qui a été présenté.
WITH candidats AS (
    SELECT t."Id"                                AS trip_id,
           t."VehicleId"                         AS vehicle_id,
           t."CompanyId"                         AS company_id,
           t."DistanceKm"::float8                AS km_avant,
           t."DurationMinutes"                   AS duree_min,
           -- Défense en profondeur : aucune ligne stockée ne devrait avoir ses
           -- bornes inversées (une durée négative échoue le test duration_secs
           -- >= 60 du détecteur et le trajet part en « Trip too short »), mais
           -- un BETWEEN sur des bornes inversées rendrait zéro en silence.
           LEAST(t."StartTime", t."EndTime")     AS fenetre_debut,
           GREATEST(t."StartTime", t."EndTime")  AS fenetre_fin,
           t."StartLatitude"                     AS lat_trip_debut,
           t."StartLongitude"                    AS lng_trip_debut,
           t."EndLatitude"                       AS lat_trip_fin,
           t."EndLongitude"                      AS lng_trip_fin,
           v.gps_device_id                       AS device_id,
           -- Un boîtier partagé par plusieurs véhicules = on ne sait pas de qui
           -- sont les positions. Le dépôt signale déjà 3 matricules de boîtiers
           -- en double sur TN.
           (SELECT count(*) FROM vehicles v2 WHERE v2.gps_device_id = v.gps_device_id) AS vehicules_du_boitier
    FROM trips t
    JOIN vehicles v ON v.id = t."VehicleId"
    WHERE t."Status" = 'completed'
      AND t."EndTime" IS NOT NULL
      AND t."EndLatitude" IS NOT NULL
      AND t."EndLongitude" IS NOT NULL
      AND v.gps_device_id IS NOT NULL
      AND t."StartTime" >= :'debut'::timestamptz
      AND t."StartTime" <  :'fin'::timestamptz
),
mesure AS (
    SELECT c.*,
           m.km_apres, m.nb_positions,
           m.lat_pos_debut, m.lng_pos_debut, m.lat_pos_fin, m.lng_pos_fin,
           s.vitesse_apres
    FROM candidats c
    CROSS JOIN LATERAL (
        -- Recalcul chronologique, COPIE CONFORME de db.rs (insert_trip) : même
        -- formule, même plafond de segment, mêmes filtres. Pas de filtre
        -- is_valid : le validateur Rust rejette déjà is_valid = false en amont
        -- sur les quatre protocoles, et en ajouter un ici ferait DIVERGER ce
        -- script de l'ingestion — donc écrire sur des trajets pourtant corrects.
        SELECT COALESCE(SUM(
                   CASE WHEN o.prev_lat IS NOT NULL
                             AND o.speed_kph >= 5
                             AND COALESCE(o.ignition_on, TRUE) THEN
                        LEAST(111.0 * SQRT(POWER(o.latitude - o.prev_lat, 2) +
                              POWER((o.longitude - o.prev_lng) * COS(RADIANS(o.latitude)), 2)), 5.0)
                   ELSE 0 END), 0)::float8                                 AS km_apres,
               count(*)                                                    AS nb_positions,
               (array_agg(o.latitude  ORDER BY o.recorded_at ASC ))[1]     AS lat_pos_debut,
               (array_agg(o.longitude ORDER BY o.recorded_at ASC ))[1]     AS lng_pos_debut,
               (array_agg(o.latitude  ORDER BY o.recorded_at DESC))[1]     AS lat_pos_fin,
               (array_agg(o.longitude ORDER BY o.recorded_at DESC))[1]     AS lng_pos_fin
        FROM (
            SELECT gp.recorded_at, gp.latitude, gp.longitude, gp.speed_kph, gp.ignition_on,
                   LAG(gp.latitude)  OVER (ORDER BY gp.recorded_at) AS prev_lat,
                   LAG(gp.longitude) OVER (ORDER BY gp.recorded_at) AS prev_lng
            FROM gps_positions gp
            WHERE gp.device_id = c.device_id
              AND gp.recorded_at BETWEEN c.fenetre_debut AND c.fenetre_fin
        ) o
    ) m
    -- LA FENÊTRE EST-ELLE UNE VRAIE FIN DE TRAJET ? Un trajet ne se clôture que
    -- sur un arrêt d'au moins 5 minutes : juste après la fin enregistrée, le
    -- véhicule doit donc être À L'ARRÊT. Si la trame qui suit immédiatement le
    -- montre EN TRAIN DE ROULER, c'est que la fin enregistrée est tronquée —
    -- signature exacte de l'effondrement de fenêtre décrit plus haut. Recalculer
    -- une telle fenêtre écrirait une distance ridiculement petite à la place
    -- d'une distance ridiculement grande : on refuse.
    LEFT JOIN LATERAL (
        SELECT gp.speed_kph AS vitesse_apres
        FROM gps_positions gp
        WHERE gp.device_id = c.device_id
          AND gp.recorded_at >  c.fenetre_fin
          AND gp.recorded_at <= c.fenetre_fin + interval '5 minutes'
        ORDER BY gp.recorded_at
        LIMIT 1
    ) s ON TRUE
),
ecarts AS (
    -- PREUVE QUE LA FENÊTRE EST BIEN CELLE DU TRAJET — le point de défiance de
    -- ce script. trips ne stocke AUCUN device_id : on ne peut remonter au
    -- boîtier que par vehicles.gps_device_id, qui donne le boîtier ACTUEL, pas
    -- celui de l'époque. Et il n'existe NULLE PART d'historique d'affectation :
    -- aucune table de ce genre dans migrations/ ni dans les migrations EF, et
    -- audit_logs ne consigne que « POST /api/admin/vehicles/12 » (méthode,
    -- chemin, horodatage) — AuditTrailMiddleware laisse OldValues et NewValues
    -- vides, alors même que l'entité les porte. Un véhicule dont le boîtier a
    -- changé verrait donc son historique recalculé sur les positions d'un AUTRE
    -- boîtier, silencieusement et toujours à la baisse.
    --
    -- Deux chemins font bouger ce lien, et ils ne se valent pas :
    -- ReplaceVehicleDevice en mode RENOMMAGE garde la MÊME fiche boîtier (seul
    -- l'IMEI change) — invisible et inoffensif ici ; en mode RATTACHEMENT il
    -- bascule le véhicule sur une autre fiche, mais seulement si la sienne était
    -- STRICTEMENT VIDE. Reste UpdateAdminVehicle, qui réaffecte librement un
    -- gps_device_id : c'est celui-là qui peut faire mentir le recalcul.
    --
    -- Plutôt que de deviner, on vérifie DIRECTEMENT : le détecteur a écrit
    -- "StartLatitude"/"StartLongitude" et "EndLatitude"/"EndLongitude" à partir
    -- des trames qui portent exactement "StartTime" et "EndTime"
    -- (trip_detector.rs : last_lat/last_lng et last_moving_time sont mis à jour
    -- ensemble). Les extrémités de la fenêtre recalculée DOIVENT donc retomber
    -- sur ces coordonnées à quelques mètres près. Si elles n'y retombent pas,
    -- on regarde la trace de quelqu'un d'autre : on n'écrit pas.
    -- Ce contrôle vaut pour TOUS les cas — changement de boîtier, réaffectation
    -- de véhicule, fiche boîtier fusionnée — sans avoir à les énumérer.
    SELECT m.*,
           CASE WHEN m.lat_pos_debut IS NULL THEN NULL ELSE
                111.0 * SQRT(POWER(m.lat_pos_debut - m.lat_trip_debut, 2) +
                      POWER((m.lng_pos_debut - m.lng_trip_debut) * COS(RADIANS(m.lat_pos_debut)), 2))
           END AS ecart_debut_km,
           CASE WHEN m.lat_pos_fin IS NULL THEN NULL ELSE
                111.0 * SQRT(POWER(m.lat_pos_fin - m.lat_trip_fin, 2) +
                      POWER((m.lng_pos_fin - m.lng_trip_fin) * COS(RADIANS(m.lat_pos_fin)), 2))
           END AS ecart_fin_km
    FROM mesure m
),
qualifie AS MATERIALIZED (
    SELECT e.*,
           CASE
             WHEN e.fenetre_fin - e.fenetre_debut > interval '24 hours'
                  THEN 'A · trajet de plus de 24 h (jamais clôturé) — voir inventaire-trips-plus-24h.sql'
             WHEN e.vehicules_du_boitier <> 1
                  THEN 'B · boîtier partagé par plusieurs véhicules — positions non attribuables'
             WHEN e.nb_positions = 0
                  THEN 'C · fenêtre vide (positions purgées) — RESTERA FAUX POUR TOUJOURS'
             WHEN e.ecart_debut_km > 0.05 OR e.ecart_fin_km > 0.05
                  THEN 'D · extrémités discordantes — le boîtier actuel n''est pas celui de l''époque'
             WHEN e.vitesse_apres >= 5
                  THEN 'H · fin tronquée (le véhicule roulait encore juste après) — DISTANCE IRRÉCUPÉRABLE par recalcul'
             WHEN e.nb_positions < GREATEST(3, e.duree_min / 10)
                  THEN 'E · trop peu de positions pour la durée — fenêtre partiellement purgée'
             WHEN e.km_apres <= 0.05
                  THEN 'F · recalcul quasi nul — même repli que db.rs, on n''écrit pas un zéro'
             WHEN e.km_avant <= e.km_apres * 1.05
                  THEN 'G · déjà juste (écart < 5 %) — no-op'
             ELSE NULL
           END AS motif_exclusion
    FROM ecarts e
),
aretenir AS (
    SELECT * FROM qualifie WHERE motif_exclusion IS NULL
)
SELECT '1 · TOTAL À RÉPARER'::text                                       AS section,
       'toutes sociétés confondues'::text                                AS detail,
       count(*)                                                          AS lignes,
       round(sum(km_avant)::numeric, 1)                                  AS km_avant,
       round(sum(km_apres)::numeric, 1)                                  AS km_apres,
       round(sum(km_avant - km_apres)::numeric, 1)                       AS km_retires
FROM aretenir

UNION ALL
SELECT '2 · PAR SOCIÉTÉ',
       coalesce(s.name, 'société #' || a.company_id),
       count(*),
       round(sum(a.km_avant)::numeric, 1),
       round(sum(a.km_apres)::numeric, 1),
       round(sum(a.km_avant - a.km_apres)::numeric, 1)
FROM aretenir a
LEFT JOIN societes s ON s.id = a.company_id
GROUP BY 2

UNION ALL
SELECT '3 · 30 PIRES ÉCARTS',
       format('trajet %s · %s · %s → %s · %s min · %s pos',
              w.trip_id,
              coalesce(nullif(v.plate_number, ''), v.name, 'véhicule #' || w.vehicle_id),
              to_char(w.fenetre_debut, 'YYYY-MM-DD HH24:MI'),
              to_char(w.fenetre_fin,   'YYYY-MM-DD HH24:MI'),
              w.duree_min, w.nb_positions),
       1,
       round(w.km_avant::numeric, 1),
       round(w.km_apres::numeric, 1),
       round((w.km_avant - w.km_apres)::numeric, 1)
FROM (SELECT * FROM aretenir ORDER BY km_avant - km_apres DESC LIMIT 30) w
JOIN vehicles v ON v.id = w.vehicle_id

UNION ALL
SELECT '4 · ÉCARTÉS PAR LES GARDE-FOUS',
       q.motif_exclusion,
       count(*),
       round(sum(q.km_avant)::numeric, 1),
       NULL,
       NULL
FROM qualifie q
WHERE q.motif_exclusion IS NOT NULL
GROUP BY 2

ORDER BY 1, 6 DESC NULLS LAST, 3 DESC, 2;

\echo ''
\echo 'Lire la section 4 : elle dit tout ce que le script REFUSE de réparer.'
\echo 'Motifs C, E et H : JAMAIS réparables par recalcul, ces lignes restent fausses.'
\echo 'Motif H = les trajets passés par le repli de db.rs. Seule issue : marquage, à décider.'
\echo 'Pour réparer : relancer avec -v reparer=1 -v lignes_attendues=<lignes de la section 1>.'


-- =============================================================================
-- PARTIE 2 — SAUVEGARDE PUIS RÉPARATION (écriture, INERTE par défaut)
--
-- Ne s'exécute qu'avec -v reparer=1 ET -v lignes_attendues=<n>.
-- Tout tient dans UNE transaction : sauvegarde, garde-fous, écriture. Le
-- moindre écart lève une exception et la transaction entière est annulée.
-- =============================================================================
\if :{?reparer}
\if :{?lignes_attendues}

\echo ''
\echo '════ 2) RÉPARATION ════'

BEGIN;

-- gps_positions fait 28 Go / 24,8 M lignes et l'ingest y écrit en continu.
--   • lock_timeout court : on ne reste jamais en file derrière un verrou.
--   • statement_timeout large mais fini : un lot mensuel doit tenir dedans ;
--     s'il ne tient pas, RÉDUIRE LE LOT plutôt qu'augmenter le délai.
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '900s';

-- Paramètres relus par le bloc DO (psql n'interpole pas dans un corps $$).
SET LOCAL gisv2.lignes_attendues = :'lignes_attendues';
SET LOCAL gisv2.sauvegarde       = :'sauvegarde';

-- ── La cible, calculée UNE fois ───────────────────────────────────────────────
-- ⚠ MÊME BLOC D'ANALYSE QU'EN PARTIE 1 : mêmes CTE, mêmes conditions, dans le
-- MÊME ORDRE. Seuls les libellés des motifs sont abrégés (A…H), puisqu'ici on
-- ne garde que les lignes SANS motif. Si l'un des deux blocs change, changer
-- l'autre : c'est la seule garantie que l'on répare exactement ce qui a été
-- présenté et validé.
CREATE TEMP TABLE reparation_cible ON COMMIT DROP AS
WITH candidats AS (
    SELECT t."Id"                                AS trip_id,
           t."VehicleId"                         AS vehicle_id,
           t."CompanyId"                         AS company_id,
           t."DistanceKm"::float8                AS km_avant,
           t."DurationMinutes"                   AS duree_min,
           LEAST(t."StartTime", t."EndTime")     AS fenetre_debut,
           GREATEST(t."StartTime", t."EndTime")  AS fenetre_fin,
           t."StartLatitude"                     AS lat_trip_debut,
           t."StartLongitude"                    AS lng_trip_debut,
           t."EndLatitude"                       AS lat_trip_fin,
           t."EndLongitude"                      AS lng_trip_fin,
           v.gps_device_id                       AS device_id,
           (SELECT count(*) FROM vehicles v2 WHERE v2.gps_device_id = v.gps_device_id) AS vehicules_du_boitier
    FROM trips t
    JOIN vehicles v ON v.id = t."VehicleId"
    WHERE t."Status" = 'completed'
      AND t."EndTime" IS NOT NULL
      AND t."EndLatitude" IS NOT NULL
      AND t."EndLongitude" IS NOT NULL
      AND v.gps_device_id IS NOT NULL
      AND t."StartTime" >= :'debut'::timestamptz
      AND t."StartTime" <  :'fin'::timestamptz
),
mesure AS (
    SELECT c.*,
           m.km_apres, m.nb_positions,
           m.lat_pos_debut, m.lng_pos_debut, m.lat_pos_fin, m.lng_pos_fin,
           s.vitesse_apres
    FROM candidats c
    CROSS JOIN LATERAL (
        SELECT COALESCE(SUM(
                   CASE WHEN o.prev_lat IS NOT NULL
                             AND o.speed_kph >= 5
                             AND COALESCE(o.ignition_on, TRUE) THEN
                        LEAST(111.0 * SQRT(POWER(o.latitude - o.prev_lat, 2) +
                              POWER((o.longitude - o.prev_lng) * COS(RADIANS(o.latitude)), 2)), 5.0)
                   ELSE 0 END), 0)::float8                                 AS km_apres,
               count(*)                                                    AS nb_positions,
               (array_agg(o.latitude  ORDER BY o.recorded_at ASC ))[1]     AS lat_pos_debut,
               (array_agg(o.longitude ORDER BY o.recorded_at ASC ))[1]     AS lng_pos_debut,
               (array_agg(o.latitude  ORDER BY o.recorded_at DESC))[1]     AS lat_pos_fin,
               (array_agg(o.longitude ORDER BY o.recorded_at DESC))[1]     AS lng_pos_fin
        FROM (
            SELECT gp.recorded_at, gp.latitude, gp.longitude, gp.speed_kph, gp.ignition_on,
                   LAG(gp.latitude)  OVER (ORDER BY gp.recorded_at) AS prev_lat,
                   LAG(gp.longitude) OVER (ORDER BY gp.recorded_at) AS prev_lng
            FROM gps_positions gp
            WHERE gp.device_id = c.device_id
              AND gp.recorded_at BETWEEN c.fenetre_debut AND c.fenetre_fin
        ) o
    ) m
    LEFT JOIN LATERAL (
        SELECT gp.speed_kph AS vitesse_apres
        FROM gps_positions gp
        WHERE gp.device_id = c.device_id
          AND gp.recorded_at >  c.fenetre_fin
          AND gp.recorded_at <= c.fenetre_fin + interval '5 minutes'
        ORDER BY gp.recorded_at
        LIMIT 1
    ) s ON TRUE
),
ecarts AS (
    SELECT m.*,
           CASE WHEN m.lat_pos_debut IS NULL THEN NULL ELSE
                111.0 * SQRT(POWER(m.lat_pos_debut - m.lat_trip_debut, 2) +
                      POWER((m.lng_pos_debut - m.lng_trip_debut) * COS(RADIANS(m.lat_pos_debut)), 2))
           END AS ecart_debut_km,
           CASE WHEN m.lat_pos_fin IS NULL THEN NULL ELSE
                111.0 * SQRT(POWER(m.lat_pos_fin - m.lat_trip_fin, 2) +
                      POWER((m.lng_pos_fin - m.lng_trip_fin) * COS(RADIANS(m.lat_pos_fin)), 2))
           END AS ecart_fin_km
    FROM mesure m
),
qualifie AS (
    SELECT e.*,
           CASE
             WHEN e.fenetre_fin - e.fenetre_debut > interval '24 hours' THEN 'A'
             WHEN e.vehicules_du_boitier <> 1                           THEN 'B'
             WHEN e.nb_positions = 0                                    THEN 'C'
             WHEN e.ecart_debut_km > 0.05 OR e.ecart_fin_km > 0.05      THEN 'D'
             WHEN e.vitesse_apres >= 5                                  THEN 'H'
             WHEN e.nb_positions < GREATEST(3, e.duree_min / 10)        THEN 'E'
             WHEN e.km_apres <= 0.05                                    THEN 'F'
             WHEN e.km_avant <= e.km_apres * 1.05                       THEN 'G'
             ELSE NULL
           END AS motif_exclusion
    FROM ecarts e
)
SELECT trip_id, vehicle_id, company_id, km_avant, km_apres, duree_min, nb_positions, fenetre_fin
FROM qualifie
WHERE motif_exclusion IS NULL;

-- Un trajet ne peut apparaître qu'une fois : si ce n'était pas le cas, l'UPDATE
-- ... FROM choisirait une ligne au hasard. On le rend impossible.
ALTER TABLE reparation_cible ADD PRIMARY KEY (trip_id);

-- ── Sauvegarde AVANT écriture ────────────────────────────────────────────────
-- La ligne entière part en JSON : on peut tout restaurer, pas seulement les deux
-- colonnes modifiées. ON CONFLICT DO NOTHING : sur un relancement, la valeur
-- d'ORIGINE est conservée et jamais écrasée par une valeur déjà réparée.
CREATE TABLE IF NOT EXISTS :sauvegarde (
    trip_id      bigint PRIMARY KEY,
    sauvegarde_a timestamptz   NOT NULL DEFAULT now(),
    lot          text          NOT NULL,
    km_avant     numeric(10,2) NOT NULL,
    km_apres     numeric(10,2) NOT NULL,
    ligne        jsonb         NOT NULL
);

COMMENT ON TABLE :sauvegarde IS
    'Sauvegarde des lignes trips modifiées par scripts/fix-trips-distance.sql (distances gonflées par le bug ordre-d''arrivée). Ne pas supprimer sans accord : c''est le seul moyen de revenir en arrière.';

INSERT INTO :sauvegarde (trip_id, lot, km_avant, km_apres, ligne)
SELECT c.trip_id,
       :'debut' || ' → ' || :'fin',
       t."DistanceKm",
       round(c.km_apres::numeric, 2),
       to_jsonb(t)
FROM reparation_cible c
JOIN trips t ON t."Id" = c.trip_id
ON CONFLICT (trip_id) DO NOTHING;

-- ── Garde-fous puis écriture, dans le même bloc ──────────────────────────────
DO $$
DECLARE
    v_cible        bigint;
    v_attendu      bigint;
    v_sauvegardees bigint;
    v_touchees     bigint;
    v_plus_recent  timestamptz;
    v_km_retires   numeric;
BEGIN
    v_attendu := current_setting('gisv2.lignes_attendues')::bigint;

    SELECT count(*), max(fenetre_fin), round(sum(km_avant - km_apres)::numeric, 1)
      INTO v_cible, v_plus_recent, v_km_retires
      FROM reparation_cible;

    IF v_attendu <= 0 THEN
        RAISE EXCEPTION
            'Garde-fou : lignes_attendues vaut %. Lancer d''abord la PRÉVISUALISATION et reporter ici le nombre de la section « 1 · TOTAL À RÉPARER ». Aucune modification.',
            v_attendu;
    END IF;

    IF v_cible <> v_attendu THEN
        RAISE EXCEPTION
            'Garde-fou : % trajet(s) visé(s), % attendu(s). La base a bougé depuis la prévisualisation (ou les bornes du lot ne sont pas les mêmes). Relancer la prévisualisation. Aucune modification.',
            v_cible, v_attendu;
    END IF;

    -- On ne répare que du passé stabilisé : un trajet clôturé il y a moins de
    -- 24 h peut encore recevoir des trames en retard (rejeu de tampon), ce qui
    -- changerait le recalcul juste après l'avoir figé.
    IF v_plus_recent > now() - interval '24 hours' THEN
        RAISE EXCEPTION
            'Garde-fou : le lot contient un trajet terminé le %, il y a moins de 24 h. Reculer la borne « fin ». Aucune modification.',
            v_plus_recent;
    END IF;

    EXECUTE format('SELECT count(*) FROM %I b JOIN reparation_cible c ON c.trip_id = b.trip_id',
                   current_setting('gisv2.sauvegarde'))
       INTO v_sauvegardees;

    IF v_sauvegardees <> v_cible THEN
        RAISE EXCEPTION
            'Garde-fou : % ligne(s) sauvegardée(s) pour % visée(s). On n''écrit pas sans filet complet. Aucune modification.',
            v_sauvegardees, v_cible;
    END IF;

    UPDATE trips t
       SET "DistanceKm"      = round(c.km_apres::numeric, 2),
           -- Même recalage que db.rs : la vitesse moyenne suit la distance,
           -- plafonnée par la vitesse max réellement observée sur le trajet.
           -- "MaxSpeedKph" est nullable : GREATEST(NULL, 1) rendrait 1 et
           -- écraserait la vitesse moyenne par 1 km/h. On plafonne alors au
           -- maximum du type numeric(6,2), ce qui revient à ne pas plafonner
           -- tout en rendant un débordement impossible.
           "AverageSpeedKph" = CASE WHEN t."DurationMinutes" > 0
                THEN LEAST(round((c.km_apres / (t."DurationMinutes"::float8 / 60.0))::numeric, 2),
                           CASE WHEN t."MaxSpeedKph" IS NULL THEN 9999.99
                                ELSE GREATEST(t."MaxSpeedKph", 1) END)
                ELSE t."AverageSpeedKph" END,
           "UpdatedAt"       = now()
      FROM reparation_cible c
     WHERE t."Id" = c.trip_id;

    GET DIAGNOSTICS v_touchees = ROW_COUNT;

    IF v_touchees <> v_cible THEN
        RAISE EXCEPTION
            'Garde-fou : % ligne(s) modifiée(s) pour % visée(s). Incohérence inexpliquée : tout est annulé.',
            v_touchees, v_cible;
    END IF;

    RAISE NOTICE 'Réparation : % trajet(s) corrigé(s), % km retirés du total. Sauvegarde dans %.',
        v_touchees, v_km_retires, current_setting('gisv2.sauvegarde');
END $$;

COMMIT;

\else
\echo ''
\echo 'ERREUR : -v reparer=1 exige aussi -v lignes_attendues=<n>, lu en section 1. Aucune modification.'
\endif
\else
\echo ''
\echo '(Mode lecture seule : partie 2 non exécutée. Ajouter -v reparer=1 -v lignes_attendues=<n> pour réparer.)'
\endif


-- =============================================================================
-- PARTIE 3 — VÉRIFICATION APRÈS COUP (lecture seule)
--
-- Se lance après la réparation, y compris dans une session en lecture seule.
-- Trois contrôles :
--   • ce que la sauvegarde contient, avant/après, par société ;
--   • qu'aucune ligne réparée n'a une valeur différente de celle calculée ;
--   • qu'il ne reste rien à réparer sur la période (relancer la partie 1 :
--     elle doit rendre 0 ligne en section 1).
-- =============================================================================
\echo ''
\echo '════ 3) VÉRIFICATION ════'

-- Tant qu'aucune réparation n'a été lancée, la table de sauvegarde n'existe
-- pas : on le constate au lieu de planter sur ON_ERROR_STOP.
SELECT CASE WHEN to_regclass(:'sauvegarde') IS NULL THEN 'false' ELSE 'true' END AS sauvegarde_existe \gset

\if :sauvegarde_existe

SELECT coalesce(s.name, 'société #' || t."CompanyId")     AS societe,
       count(*)                                           AS lignes_reparees,
       round(sum(b.km_avant), 1)                          AS km_avant,
       round(sum(t."DistanceKm"), 1)                      AS km_apres,
       round(sum(b.km_avant - t."DistanceKm"), 1)         AS km_retires,
       count(*) FILTER (WHERE t."DistanceKm" <> b.km_apres) AS lignes_divergentes
FROM :sauvegarde b
JOIN trips t     ON t."Id" = b.trip_id
LEFT JOIN societes s ON s.id = t."CompanyId"
GROUP BY 1
ORDER BY 5 DESC NULLS LAST;

\echo ''
\echo 'lignes_divergentes doit valoir 0. Sinon : une autre écriture est passée après la réparation.'
\echo 'Relancer la PARTIE 1 sur le même lot : la section 1 doit désormais rendre 0 ligne.'

\else
\echo '(Aucune table de sauvegarde : réparation jamais lancée, donc rien à vérifier.)'
\endif


-- =============================================================================
-- PARTIE 4 — RESTAURATION (volontairement INERTE : à décommenter à la main)
--
-- Remet les lignes sauvegardées dans leur état d'origine, valeurs gonflées
-- comprises. À n'utiliser que si la réparation s'est révélée fausse.
-- La table de sauvegarde n'est JAMAIS supprimée par ce script : aucun DROP,
-- aucun DELETE nulle part. Sa suppression éventuelle est une décision à part.
-- =============================================================================
--
-- BEGIN;
-- SET LOCAL lock_timeout = '5s';
-- SET LOCAL statement_timeout = '900s';
--
-- UPDATE trips t
--    SET "DistanceKm"      = (b.ligne->>'DistanceKm')::numeric,
--        "AverageSpeedKph" = (b.ligne->>'AverageSpeedKph')::numeric,
--        "UpdatedAt"       = (b.ligne->>'UpdatedAt')::timestamptz
--   FROM :sauvegarde b
--  WHERE t."Id" = b.trip_id
--    AND t."DistanceKm" = b.km_apres;   -- ne restaure que ce qu'on a écrit
--
-- COMMIT;
--
-- Contrôle : la requête de la partie 3 doit rendre km_apres = km_avant.
