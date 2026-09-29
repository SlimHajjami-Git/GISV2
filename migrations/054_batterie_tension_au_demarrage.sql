-- 054 — Batterie NEMS : la tension retenue est celle du DÉMARRAGE, gardée jusqu'au suivant
-- (décision de Slim du 29/09/2026 : « afficher la valeur de la batterie quand le véhicule
-- démarre et garder cette valeur à l'écran jusqu'au prochain démarrage »).
--
-- CONSTAT — Depuis le 25/09 le monitoring affichait, pour un NEMS, le MINIMUM DU JOUR de
-- l'octet « Batterie » (34-36). Une statistique d'extrême sans garde-fou : mesuré sur la
-- production TN le 28/09, 153 boîtiers sur 237 seraient passés sous le seuil, dont 54 à
-- cause de dix trames ou moins dans la journée. Cas net : 229 TU 9662, 211 trames, UNE
-- seule basse, affichait 10,0 V pour une médiane de 12,81 V. Et l'octet mélange deux
-- grandeurs : moteur tournant il porte l'alternateur (13,5 à 14,4 V), pas la batterie.
--
-- CE QU'ON RETIENT — une seule mesure par démarrage. Le démarrage est la première trame
-- moteur allumé après au moins 10 minutes sans aucune trame moteur allumé ; la valeur est
-- la médiane des 3 premières trames exploitables des 2 minutes qui suivent. Elle reste
-- affichée jusqu'au démarrage suivant, c'est donc un ÉTAT du boîtier, pas un agrégat à
-- recalculer : d'où ces deux colonnes plutôt qu'un calcul dans /vehicles/with-positions.
--
-- CONSÉQUENCE DE PERFORMANCE, VOULUE — /vehicles/with-positions est pollé toutes les ~30 s
-- par page et par utilisateur. Calculer la batterie dans sa requête de statistiques coûtait
-- entre 120 et 270 ms de plus par appel sur HERTZ (307 véhicules), mesuré le 28/09 :
-- 674 ms sans, 788 à 944 ms avec. En lisant ces colonnes le chemin chaud ne paie plus rien.
--
-- POURQUOI DES COLONNES ET PAS metadata — gps_devices fait quelques centaines de lignes,
-- mais ces deux valeurs sont lues à CHAQUE appel du chemin chaud et écrites par un service
-- de fond toutes les 5 minutes. Deux colonnes typées valent mieux qu'un accès jsonb.
--
-- battery_start_raw — valeur BRUTE de l'octet 34-36 (× 40/256 = 0,156 V par unité), déjà
-- triée sur la bande 68-92 par le service qui l'écrit : hors de cette bande l'octet n'est
-- pas une tension (les firmwares R00C30d y recopient l'octet de cap, 0 à 44). NULL = aucun
-- démarrage exploitable connu, ou octet jugé figé (voir ci-dessous) → l'écran affiche N/A.
--
-- battery_start_at — instant du démarrage d'où vient la valeur, en UTC. Sert à dater la
-- mesure à l'écran (« au démarrage du 29/09 à 07:12 ») : sur TN, 14 boîtiers sur 226
-- n'avaient pas redémarré depuis plus de 24 h, la fraîcheur doit être visible.
--
-- GARDE « L'OCTET BOUGE-T-IL » — le service n'écrit rien si, sur les dernières 24 h, l'octet
-- est strictement constant alors que le véhicule a roulé ET s'est arrêté (au moins 20 trames
-- dans chaque état) : c'est la signature de l'incident du 14/08/2026, où l'application
-- affichait « 12,9 V / 100 % » sur un véhicule incapable de démarrer. Mesuré sur 891
-- journées-boîtier du 24 au 29/09 : se déclenche sur UNE seule (244 TU 1249 le 27/09,
-- 1 124 trames toutes à 49 brut, soit 7,66 V figé, en roulant jusqu'à 88 km/h).
--
-- COÛT — PostgreSQL 11+ ajoute une colonne nullable sans valeur par défaut de façon
-- instantanée : métadonnées seules, verrou ACCESS EXCLUSIVE de quelques millisecondes sur
-- une table de quelques centaines de lignes.
--
-- ORDRE DE DÉPLOIEMENT — ce SQL AVANT le nouveau pod gis-api. L'API mappe les deux colonnes
-- (GpsDeviceConfiguration) et les lit sur /vehicles/with-positions ; sans elles, le
-- monitoring ET toute lecture de boîtier tombent en 42703. Rien à faire côté ingest Rust :
-- il n'écrit pas ces colonnes. SQL joué avant le pod, sur DZ puis TN.
--
-- RETOUR ARRIÈRE — les colonnes restent, simplement plus écrites ni lues. Aucune donnée
-- d'origine n'est touchée : gps_positions.battery_raw reste la source de vérité.

ALTER TABLE gps_devices
    ADD COLUMN IF NOT EXISTS battery_start_raw SMALLINT NULL;

ALTER TABLE gps_devices
    ADD COLUMN IF NOT EXISTS battery_start_at TIMESTAMPTZ NULL;

COMMENT ON COLUMN gps_devices.battery_start_raw IS
    'Tension batterie retenue au dernier démarrage du véhicule : valeur brute de l''octet '
    '34-36 de la trame NEMS, × 40/256 V par unité, médiane des 3 premières trames des '
    '2 minutes suivant le démarrage, triée sur la bande 68-92. NULL = aucun démarrage '
    'exploitable connu, ou octet figé sur les dernières 24 h (capteur qui ne mesure rien). '
    'Écrite par BatteryStartReadingService, lue par /vehicles/with-positions.';

COMMENT ON COLUMN gps_devices.battery_start_at IS
    'Instant (UTC) du démarrage d''où provient battery_start_raw. Permet de dater la mesure '
    'à l''écran : la valeur reste affichée jusqu''au démarrage suivant, donc parfois '
    'plusieurs jours.';
