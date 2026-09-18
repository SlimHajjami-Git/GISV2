-- 047 — Tension batterie véhicule : le vrai champ de la trame NEMS — 17/09/2026.
--
-- CONSTAT — La tension affichée venait de l'octet 32-34 de la trame (« Power »,
-- facteur 0,3). Sur 288 boîtiers NEMS de TN, 281 renvoyaient la MÊME valeur moteur
-- tournant et moteur éteint : cet octet ne mesure rien. L'application a affiché
-- « 12,9 V / 100 % » sur un véhicule incapable de démarrer (259 TU 4987, 14/08/2026),
-- d'où le masquage posé ce jour-là — qui cache aujourd'hui 274 véhicules sur 288.
--
-- CE QUE DIT LE FOURNISSEUR (17/09/2026) — la tension batterie du véhicule est
-- l'octet 34-36 (« Batterie » dans son code Java de référence), facteur 0,156 V par
-- unité, soit 40 V de pleine échelle sur 8 bits. Le champ « Power » est à ignorer.
--
-- VÉRIFIÉ SUR LES DONNÉES — sur les 7 boîtiers qui renseignent cet octet, × 0,156
-- donne 12,48 à 13,73 V : une batterie 12 V. Les 281 autres y recopient l'octet de
-- cap (valeurs 0-44 → 0-6,9 V, hors de toute plage 12 V) ou y mettent 0. Le champ
-- n'est renseigné que sous les firmwares R00C32a et R00C17 ; sous R00C30d il ne
-- contient que le doublon du cap. C'est la bande de plausibilité (10,5-14,4 V au
-- repos), et non un test d'alternateur, qui sépare proprement les deux familles.
--
-- POURQUOI UNE COLONNE ET NON metadata — gps_positions fait 27 Go sur TN et la
-- tension est lue par l'audit quotidien (médianes PostgreSQL sur 7 jours, toute la
-- flotte) et par le chemin chaud /vehicles/with-positions. Un accès jsonb sur ce
-- volume coûterait bien plus qu'une colonne SMALLINT de 2 octets.
--
-- NULL = PAS DE MESURE — l'ingest n'écrit la valeur que si elle est > 0 ; un boîtier
-- qui ne renseigne pas le champ laisse NULL, ce qui se distingue d'un vrai 0 V.
--
-- COÛT — PostgreSQL 11+ ajoute une colonne nullable sans valeur par défaut de façon
-- instantanée : métadonnées seules, aucune réécriture des 27 Go, verrou ACCESS
-- EXCLUSIVE de quelques millisecondes. La table n'est pas partitionnée (vérifié sur
-- TN et DZ : 0 enfant dans pg_inherits), donc rien à propager.
--
-- ORDRE DE DÉPLOIEMENT — ce SQL AVANT le nouveau pod gps-ingest, sur DZ puis TN.
-- L'inverse ferait échouer chaque INSERT en 42703 : plus aucune position enregistrée.
-- Ce SQL passe AUSSI avant le nouveau pod gis-api : l'API mappe battery_raw
-- (GpsPositionConfiguration) et la lit en SQL brut sur /vehicles/with-positions.
-- Sans la colonne, toute lecture de positions et le monitoring tombent en 42703.
-- Ingest non redéployé : la colonne reste simplement NULL partout.
--
-- POSITION ANCIENNE INCHANGÉE — power_voltage continue d'être écrite et conservée :
-- elle reste la seule source des boîtiers Teltonika (facteur 0,1, 11 appareils, qui
-- eux mesurent correctement) et l'historique des 27 Go garde sa valeur d'audit.

ALTER TABLE gps_positions
    ADD COLUMN IF NOT EXISTS battery_raw SMALLINT NULL;

COMMENT ON COLUMN gps_positions.battery_raw IS
    'Octet 34-36 de la trame NEMS (champ « Batterie », doc constructeur du 17/09/2026) : '
    'tension batterie du véhicule, × 0,156 V par unité (40 V pleine échelle sur 8 bits). '
    'NULL = le boîtier ne renseigne pas ce champ (firmware R00C30d : il y recopie l''octet '
    'de cap). Ne pas confondre avec power_voltage (octet 32-34, « Power »), qui ne mesure '
    'rien sur la quasi-totalité des NEMS.';
