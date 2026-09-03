-- 040 — Offre « gestion de parc sans GPS » (plan-basique) : masquer le rapport
-- kilométrique et retirer la mention « sans GPS » du libellé de l'offre.
-- Demande client de la recette du 03/09/2026.
--
-- CONSTAT 1 — Rapport kilométrique. La migration 037 avait délibérément laissé
-- report_mileage à true (son commentaire le dit) parce que, depuis le 31/08, le
-- handler dérive le kilométrage des relevés au compteur saisis à chaque plein
-- (fuel_entries.odometer_km) pour les véhicules sans boîtier : le rapport
-- FONCTIONNE donc en GPA. Le client préfère malgré tout ne pas l'exposer.
-- Le drapeau ferme les deux portes d'un coup : l'entrée du menu (PermissionService
-- .hasReportAccess) et l'endpoint /api/reports/mileage (PermissionMiddleware).
-- Réversible à tout moment : repasser la colonne à true.
--
-- CONSTAT 2 — Libellé. La description est affichée telle quelle sous le nom de
-- l'offre sur l'écran Abonnement. « sans GPS » décrit l'offre par ce qu'elle
-- N'A PAS, face à un client qui la paie pour ce qu'elle fait.
--
-- Aucune colonne créée, aucun pod à redéployer. Les drapeaux d'abonnement sont
-- transportés dans le jeton : un utilisateur déjà connecté doit se reconnecter
-- pour voir l'effet.

UPDATE subscription_types
SET report_mileage = false,
    description    = 'Gestion de parc — véhicules, entretien, carburant et dépenses',
    updated_at     = now()
WHERE code = 'plan-basique';
