-- 055 — L'alerte « véhicule qui ne démarre pas » est retirée : la colonne reste,
-- on la documente (décision de Slim du 29/09/2026 : « maintenant que le code de la
-- batterie est bien fait on peut enlever l'autre alerte »).
--
-- CONSTAT — StartFailureDetectionService lisait le DÉMARREUR (tentatives de contact
-- répétées, immobilité du jour, a roulé la veille) faute de pouvoir juger la batterie
-- sur l'octet 32-34, muet. Deux raisons de l'arrêter :
--
--   1. Elle n'a jamais tenu son étalonnage. Calibrée en août 2026 sur 16 jours-véhicule
--      en 8 jours, soit « environ 2 alertes par jour pour 250 véhicules ». Relevé sur la
--      production TN le 29/09/2026 : 813 véhicules-jours alertés sur 37 jours, soit 22 par
--      jour, sur 119 véhicules distincts et un parc de ~300. Sur les 14 derniers jours,
--      entre 18 et 33 véhicules alertés CHAQUE jour. Une flotte où un quart des véhicules
--      « ne démarre pas » tous les jours n'est pas une flotte en panne : c'est du bruit.
--      2 415 notifications au total, dont 2 058 sur les 30 derniers jours.
--
--   2. Elle dit maintenant la même chose que l'alerte batterie, en moins fiable. Depuis
--      le 29/09, la tension est relevée au DÉMARRAGE du véhicule (migration 054) et
--      VoltageHealthMonitoringService notifie sous 11,5 V. Deux alertes concurrentes sur
--      la même panne se contredisent et l'exploitant ne sait plus laquelle croire.
--
-- CE QUE CE FICHIER FAIT — un COMMENT, rien d'autre. Aucune donnée n'est touchée :
--   * la colonne gps_devices.last_start_failure_alert_at est CONSERVÉE avec ses
--     120 boîtiers horodatés (dernier le 29/09/2026) — c'est de l'historique client ;
--   * les 2 415 notifications de type 'start_failure' déjà en base sont CONSERVÉES.
--     Elles restent lisibles dans la cloche (le front les laisse passer par son cas par
--     défaut) et 2 401 sur 2 415 sont déjà lues, donc elles ne gonflent pas le compteur
--     de non-lues. Si Slim veut les purger, cela se propose à part, avec ses chiffres.
--
-- L'API ne mappe plus cette colonne (GpsDeviceConfiguration) et plus aucun code ne
-- l'écrit ni ne la lit.
--
-- ORDRE DE DÉPLOIEMENT — AUCUNE contrainte : ce fichier ne change pas le schéma, il ne
-- fait qu'écrire un commentaire. Il peut être joué avant ou après le pod, sur DZ puis TN.
-- Retirer un mapping EF ne casse rien : la colonne existe toujours, elle n'est plus lue.

COMMENT ON COLUMN gps_devices.last_start_failure_alert_at IS
    'HISTORIQUE — plus écrite depuis le 29/09/2026. Servait de temporisation 24 h à '
    'l''alerte « véhicule qui ne démarre pas » (StartFailureDetectionService), retirée '
    'ce jour-là : étalonnage jamais tenu (22 véhicules alertés par jour sur un parc de '
    '~300, pour ~2 attendus) et doublon de l''alerte batterie, qui se juge désormais sur '
    'la tension relevée au démarrage (gps_devices.battery_start_raw, migration 054). '
    'Conservée pour l''historique ; ne pas la réutiliser pour autre chose.';
