-- 037 — Offre « gestion de parc sans GPS » (plan-basique) : masquer les rapports
-- qui dépendent du boîtier GPS (recette client du 25/08/2026).
--
-- Le drapeau report_fuel gouverne les rapports carburant CALCULÉS SUR LE GPS :
--   « Consommation carburant » (id 2, capteur/positions), « Estimation coûts
--   carburant » (id 13, distance GPS) et « Carburant réel vs GPS » (id 17,
--   anti-fraude capteur), ainsi que l'analyse par segments. Aucun n'a de sens
--   sans boîtier -> report_fuel = false.
-- Le rapport « Consommation carburant MENSUEL » (id 16) et « Coûts mensuel »
--   (id 15) lisent les PLEINS SAISIS et restent accessibles : ils dépendent de
--   report_monthly_costs (laissé à true), pas de report_fuel.
--
-- report_mileage_period gouverne « Kilométrage par période » (id 10, catégorie
--   GPS) : pas un rapport GPA -> false.
--
-- On NE touche PAS report_mileage (« Rapport kilométrique » id 5) : son sort
-- (masquer vs alimenter par les relevés au plein) est traité à part.
-- Les rapports conservés pour la GPA : maintenance (entretiens), costs
-- (réparations/dépenses), monthly (mensuel flotte), monthly_costs.

UPDATE subscription_types
SET report_fuel = false,
    report_mileage_period = false
WHERE code = 'plan-basique';
