-- 068 — Données CAN (FMS) d'un boîtier déclarées NON FIABLES (incident NEMS L du 09/11/2026).
--
-- Constat (lecture seule de TN, 13/11/2026) : le 09/11 entre 14h35 et 17h23, une trentaine de
-- NEMS L ont reçu les logiciels MF2-NTS-VW15-2.0.0.1R00C32b / VW26-2.0.0.1R00C32a. Depuis, 13
-- boîtiers envoient un compteur 3 à 100 fois trop grand qui n'avance que par pas de 256 km
-- (champs mal alignés : la R00C32b ajoute température et batterie) et un carburant figé à 4 ou
-- 20. Au 13/11 : 25 078 positions faussées, 4 916 faux « niveau bas » et 103 faux « pleins »
-- dans fuel_records, une fausse alerte de vol ; 8 boîtiers envoient ENCORE des valeurs fausses.
-- L'ingestion laissait passer le compteur faux (après 5 valeurs aberrantes mais cohérentes entre
-- elles, elle « fait confiance » au boîtier) et la synchronisation du kilométrage
-- (POST /vehicles/{id}/sync-mileage) le recopiait sur la fiche sans contrôle.
--
-- Modèle : un drapeau par boîtier. Levé, l'ingestion efface de la trame, AVANT tout traitement,
-- compteur, carburant, régime, consommation et température CAN (enregistrés vides : rien de
-- faux n'entre en base, ni position, ni événement carburant, ni trajet, ni temps réel) ; la
-- position GPS reste enregistrée et la fiche avance à la distance GPS (repli « CAN muet »
-- existant). La synchronisation du kilométrage refuse ces boîtiers. Le drapeau se baisse quand
-- le décodage du nouveau format est corrigé ou le logiciel remis en place.
--
-- AUCUNE donnée n'est modifiée ici : les boîtiers concernés sont marqués à part, sur décision
-- explicite, avec la liste et le motif. Ordre : à jouer AVANT le pod API et l'ingestion qui lisent
-- ces colonnes (sinon 42703 au chargement des boîtiers). Rejouable sans effet.

ALTER TABLE gps_devices ADD COLUMN IF NOT EXISTS fms_untrusted boolean NOT NULL DEFAULT false;
ALTER TABLE gps_devices ADD COLUMN IF NOT EXISTS fms_untrusted_reason varchar(256);
ALTER TABLE gps_devices ADD COLUMN IF NOT EXISTS fms_untrusted_at timestamptz;
