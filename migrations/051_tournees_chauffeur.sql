-- 051 — Tournée envoyée au chauffeur, suivie par le boîtier et par son téléphone
-- (lot 1 de la fonctionnalité décidée le 18/09/2026, comptes chauffeurs = migration 050).
--
-- CONSTAT — Une tournée est planifiée pour UN véhicule et, depuis le lot 0, pour UN
-- chauffeur (fiche `drivers`). Rien ne dit si elle lui a été envoyée, s'il l'a ouverte,
-- s'il est parti ; les étapes ne connaissent que l'arrivée détectée par le boîtier ;
-- et un véhicule sans boîtier (offre GPA) ou dont le boîtier se tait n'a aucun suivi.
--
-- POURQUOI CES COLONNES —
--   • tours."SentAt" / "SentByUserId" / "OpenedAt" : l'envoi (notification push au
--     compte du chauffeur) et la première ouverture sur le téléphone ; le gestionnaire
--     lit « Envoyée 06:50 · Ouverte 07:12 » et sait à qui il l'a envoyée.
--   • tours."TrackingSource" / "TrackingSourceSince" : la source qui suit la tournée en ce
--     moment (device | phone | none) et depuis quand — c'est ce que l'écran affiche
--     (« Suivi par boîtier », « Suivi interrompu depuis 12 min »).
--   • tour_waypoints."DriverArrivedAt" / "DriverDepartedAt" : ce que le chauffeur a
--     DÉCLARÉ (« Je suis arrivé », « Je repars ») ; "ActualDepartureTime" : le départ
--     de l'étape retenu ; "ArrivalSource" : qui a validé l'étape (device | phone |
--     geofence | driver | manager), pour que l'heure déclarée et l'heure détectée ne se
--     confondent jamais ; "DriverDeclarationDistanceM" : distance à l'étape au moment de
--     la déclaration, mesurée par le boîtier ou le téléphone — un « arrivé » déclaré à
--     3 km de l'étape s'affiche comme tel au gestionnaire.
--   • driver_app_positions : les positions envoyées par le téléphone du chauffeur PENDANT
--     une tournée en cours, et rien d'autre. Table SÉPARÉE de gps_positions : y écrire
--     fausserait le kilométrage (source unique), les trajets, les alertes de vitesse et
--     les rapports, qui sont ceux du VÉHICULE. Conservation : à décider avec Slim avant la
--     mise en production (90 jours proposés) ; aucune purge automatique n'est créée ici.
--
-- Casse : tours et tour_waypoints sont en PascalCase entre guillemets (convention EF de
-- ces deux tables), driver_app_positions en snake_case comme les tables écrites à la main.
--
-- AUCUNE DONNÉE MODIFIÉE. Sur TN toutes les tournées ont "DriverId" NULL.
--
-- ORDRE DE DÉPLOIEMENT — AVANT le pod API qui mappe ces colonnes (sinon 42703 sur toute
-- lecture des tournées). Après la 050. Sur TN uniquement (DZ hors périmètre).

-- tours -------------------------------------------------------------------------
ALTER TABLE tours ADD COLUMN IF NOT EXISTS "SentAt" timestamp without time zone;
ALTER TABLE tours ADD COLUMN IF NOT EXISTS "SentByUserId" integer;
ALTER TABLE tours ADD COLUMN IF NOT EXISTS "OpenedAt" timestamp without time zone;
ALTER TABLE tours ADD COLUMN IF NOT EXISTS "TrackingSource" varchar(8);
ALTER TABLE tours ADD COLUMN IF NOT EXISTS "TrackingSourceSince" timestamp without time zone;

COMMENT ON COLUMN tours."SentAt" IS 'Dernier envoi au chauffeur (push) ; NULL = jamais envoyée ou chauffeur changé depuis';
COMMENT ON COLUMN tours."TrackingSource" IS 'Source de suivi courante : device | phone | none (NULL = tournée non suivie)';

-- tour_waypoints ----------------------------------------------------------------
ALTER TABLE tour_waypoints ADD COLUMN IF NOT EXISTS "DriverArrivedAt" timestamp without time zone;
ALTER TABLE tour_waypoints ADD COLUMN IF NOT EXISTS "DriverDepartedAt" timestamp without time zone;
ALTER TABLE tour_waypoints ADD COLUMN IF NOT EXISTS "ActualDepartureTime" timestamp without time zone;
ALTER TABLE tour_waypoints ADD COLUMN IF NOT EXISTS "ArrivalSource" varchar(16);
ALTER TABLE tour_waypoints ADD COLUMN IF NOT EXISTS "DriverDeclarationDistanceM" integer;

COMMENT ON COLUMN tour_waypoints."ArrivalSource" IS 'Qui a validé l''étape : device | phone | geofence | driver | manager';

-- driver_app_positions ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS driver_app_positions (
    id            bigserial PRIMARY KEY,
    company_id    integer NOT NULL,
    user_id       integer NOT NULL,
    driver_id     integer NOT NULL,
    tour_id       integer NOT NULL REFERENCES tours("Id") ON DELETE CASCADE,
    recorded_at   timestamp with time zone NOT NULL,   -- horloge du téléphone, corrigée du décalage
    received_at   timestamp with time zone NOT NULL DEFAULT now(),
    latitude      double precision NOT NULL,
    longitude     double precision NOT NULL,
    accuracy_m    real,
    speed_kph     real,
    heading       real,
    is_mocked     boolean NOT NULL DEFAULT false,
    battery_level smallint
);

-- Lecture par le moniteur : la tranche « depuis le dernier id vu » d'une tournée.
CREATE INDEX IF NOT EXISTS ix_driver_app_positions_tour_id
    ON driver_app_positions (tour_id, id);
-- Dernier point d'un chauffeur (source de suivi, écran).
CREATE INDEX IF NOT EXISTS ix_driver_app_positions_user_recorded
    ON driver_app_positions (user_id, recorded_at DESC);

COMMENT ON TABLE driver_app_positions IS
    'Positions du téléphone du chauffeur pendant une tournée en cours (jamais mélangées à gps_positions) ; durée de conservation à décider (90 j proposés)';
