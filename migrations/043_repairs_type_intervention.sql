-- 043 — Type d'intervention sur les réparations (rapport « Fréquence des
-- réparations », recette client du 04/09/2026).
--
-- Constat : la table repairs ne porte aucune catégorie d'intervention (colonnes
-- réelles vérifiées sur TN : reference, description, repair_date, coûts, statut,
-- facture, notes). Le nouveau rapport R4 propose une répartition « par type »
-- (électrique, mécanique, freinage, pneumatique, carrosserie, autres) : sans
-- colonne, l'API ne peut que la DÉDUIRE des mots de la description.
--
-- Pourquoi : la colonne nullable permet à l'exploitant de qualifier ses
-- prochaines réparations depuis l'écran (le type saisi prime toujours) ; les
-- lignes existantes restent NULL et continuent d'être classées par mots-clés,
-- l'écran signale alors « (déduit) ». AUCUNE mise à jour de données ici.
--
-- Valeurs attendues : electrique, mecanique, freinage, pneumatique, carrosserie,
-- autre. Pas de contrainte CHECK pour ne pas bloquer une saisie mobile ancienne.
--
-- Ordre de déploiement : jouer ce SQL sur DZ puis TN AVANT le pod API qui mappe
-- Repair.RepairType, sinon 42703 sur toute lecture des réparations.

ALTER TABLE repairs
    ADD COLUMN IF NOT EXISTS repair_type varchar(30);

COMMENT ON COLUMN repairs.repair_type IS
    'Type d''intervention : electrique | mecanique | freinage | pneumatique | carrosserie | autre (NULL = déduit de la description)';
