-- 033 : tranche de véhicules déclarée à l'inscription
--
-- Demandée par le cahier des charges du site public France (20/08/2026) :
-- le formulaire d'essai gratuit recueille une estimation de la taille du parc
-- (1-5, 6-20, 21-50, 51-100, 100+). C'est une donnée commerciale — elle sert à
-- qualifier le prospect, pas à limiter l'usage.
--
-- Stockée comme le CODE de la tranche et non comme un nombre : l'utilisateur
-- déclare un ordre de grandeur, pas un décompte. Convertir en entier donnerait
-- une fausse précision, et le nombre réel de véhicules se lit de toute façon
-- dans la table vehicles.
--
-- Nullable : toutes les sociétés existantes sont antérieures à ce champ, et
-- l'API doit rester compatible avec les clients qui ne l'envoient pas.

ALTER TABLE societes
    ADD COLUMN IF NOT EXISTS fleet_size_range VARCHAR(20) NULL;

COMMENT ON COLUMN societes.fleet_size_range IS
    'Tranche de vehicules declaree a l''inscription : 1-5, 6-20, 21-50, 51-100, 100+. Donnee commerciale, sans effet fonctionnel.';
