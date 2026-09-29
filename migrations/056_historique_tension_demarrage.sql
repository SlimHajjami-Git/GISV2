-- 056 — Historique des tensions au démarrage : le témoin batterie attend une
-- CONFIRMATION (décision de Slim du 29/09/2026 : « si un chauffeur laisse la radio
-- allumée notre code peut donner des faux positifs non ? » puis « 20 derniers »).
--
-- CONSTAT — La migration 054 retenait UNE tension par démarrage et allumait le témoin
-- dès qu'elle passait sous 11,5 V. Mesuré sur la production TN le 29/09/2026, sur
-- 237 boîtiers et 4 jours (~19 démarrages par véhicule) : sur 34 témoins allumés,
-- 9 venaient d'un creux ISOLÉ entouré de démarrages sains. Exemples relevés :
--   251 TU 8789 : 10,9 | 12,3 | 12,5 | 12,5 | 12,3   (du plus récent au plus ancien)
--   257 TU 5450 : 11,1 | 12,7 | 12,3 | 11,9 | 12,3
-- Radio oubliée, phares, trajet trop court pour recharger : la batterie est saine.
--
-- ET L'INVERSE EST VRAI AUSSI — juger le dernier démarrage seul RATE des batteries
-- faibles dont le dernier démarrage était bon, typiquement après un long trajet :
--   235 TU 5540 : 13,1 | 11,3 | 11,3 | 11,3 | 11,3
--   187 TU 8526 : 12,0 | 11,3 | 11,4 | 11,4 | 11,3
-- La confirmation en rattrape 5 que la règle précédente laissait passer.
--
-- LA RÈGLE RETENUE — médiane des 20 derniers démarrages, ou de tous s'il y en a moins,
-- avec un minimum de 3. Médiane et non moyenne : sur 10,9 | 12,3 | 12,5 la moyenne vaut
-- 11,9 V et allumerait encore le témoin, la médiane vaut 12,3 V et l'éteint.
-- Pourquoi 20 « au plus » et pas 20 tout court : seuls 91 boîtiers sur 237 ont
-- 20 démarrages en 4 jours ; l'exiger priverait 146 véhicules de tout témoin, batteries
-- mourantes comprises, pour un écart de 3 véhicules sur le résultat.
-- Résultat mesuré : 20 témoins sur 224 boîtiers jugeables, 13 sans témoin faute de
-- 3 démarrages. 20 démarrages couvrent ~56 h : le témoin réagit en 2 à 3 jours, ce qui
-- convient à « prévoir un remplacement » et n'a jamais prétendu prédire la panne du jour.
--
-- POURQUOI UNE TABLE ET PAS UN TABLEAU SUR gps_devices — un smallint[] tronqué à 20
-- suffirait au calcul, mais perdrait les horodatages. Or répondre à « cette batterie
-- se dégrade-t-elle ? » a demandé aujourd'hui de reconstruire ces relevés en balayant
-- gps_positions (27 Go) pendant plusieurs minutes. Avec cette table, la même question
-- et toute future courbe « tension au démarrage » sont un SELECT. Volume : ~5 démarrages
-- par véhicule et par jour, soit ~1 200 lignes/jour pour le parc TN, ~440 000 par an —
-- négligeable à côté de gps_positions. AUCUNE purge automatique n'est posée ici : si une
-- rétention devient utile, elle se proposera à part, avec ses chiffres.
--
-- battery_start_median_raw — la médiane calculée, en valeur BRUTE de l'octet 34-36
-- (× 40/256 = 0,156 V par unité), écrite par BatteryStartReadingService à chaque nouveau
-- démarrage. NULL = moins de 3 démarrages connus → aucun témoin, aucune notification.
-- C'est elle, et non battery_start_raw, qui pilote le témoin et l'alerte.
-- battery_start_raw (migration 054) reste la DERNIÈRE mesure, celle qui s'affiche.
--
-- ORDRE DE DÉPLOIEMENT — ce SQL AVANT le nouveau pod gis-api : l'API mappe la table et
-- la colonne, et les lit sur /vehicles/with-positions. Sans elles, 42703 sur tout le
-- monitoring. À jouer après 054, sur DZ puis TN. Rien à faire côté ingest Rust.
--
-- RETOUR ARRIÈRE — table et colonne restent, simplement plus écrites ni lues ; le témoin
-- repasserait sur battery_start_raw. Aucune donnée d'origine n'est touchée.

CREATE TABLE IF NOT EXISTS battery_start_readings (
    id           BIGSERIAL PRIMARY KEY,
    device_id    INTEGER     NOT NULL REFERENCES gps_devices(id) ON DELETE CASCADE,
    start_at     TIMESTAMPTZ NOT NULL,
    battery_raw  SMALLINT    NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Idempotence du service : chaque démarrage est vu par plusieurs cycles (détection sur
-- 20 min, cycle de 5 min). L'insertion se fait en ON CONFLICT DO NOTHING sur cette clé.
CREATE UNIQUE INDEX IF NOT EXISTS ux_battery_start_readings_device_start
    ON battery_start_readings (device_id, start_at);

-- « Les 20 derniers démarrages de ce boîtier » : un seul parcours d'index, à l'envers.
CREATE INDEX IF NOT EXISTS ix_battery_start_readings_device_recent
    ON battery_start_readings (device_id, start_at DESC) INCLUDE (battery_raw);

COMMENT ON TABLE battery_start_readings IS
    'Une ligne par démarrage de véhicule : tension batterie relevée sur l''octet 34-36 '
    'des trames NEMS, médiane des 3 premières trames des 2 minutes suivant le démarrage. '
    'Écrite par BatteryStartReadingService. Sert à confirmer le témoin « anomalie '
    'batterie » sur la médiane des 20 derniers démarrages plutôt que sur un seul.';

ALTER TABLE gps_devices
    ADD COLUMN IF NOT EXISTS battery_start_median_raw SMALLINT NULL;

COMMENT ON COLUMN gps_devices.battery_start_median_raw IS
    'Médiane des 20 derniers démarrages de ce boîtier (ou de tous s''il y en a moins), '
    'en valeur brute de l''octet 34-36, × 40/256 V par unité. NULL = moins de 3 '
    'démarrages connus, donc aucun témoin ni notification. Pilote le témoin « anomalie '
    'batterie » et l''alerte de VoltageHealthMonitoringService ; battery_start_raw, elle, '
    'reste la DERNIÈRE mesure, celle qui s''affiche au monitoring.';
