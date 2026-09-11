-- 046 — Une permission par rapport — recette client du 11/09/2026.
--
-- CONSTAT — L'éditeur de permissions (Utilisateurs > Rapports autorisés) montre
-- 13 cases pour 21 rapports. Les quatre rapports de coûts du 04/09/2026 (coût
-- d'exploitation réel, évolution des coûts, véhicules les plus coûteux, fréquence
-- des réparations) héritaient de can_report_costs ; « Consommation carburant
-- mensuel » héritait de can_report_monthly_costs (même requête que « Coûts mensuel
-- par véhicule ») ; « Estimation coûts carburant », « Carburant réel vs GPS » et le
-- Rapport IA Flotte n'avaient aucun droit par utilisateur. Le client veut retirer
-- ou accorder CHAQUE rapport séparément.
--
-- POURQUOI DES COLONNES — les 13 permissions existantes sont déjà des colonnes de
-- users, chargées au login dans le jeton et relues par PermissionMiddleware à chaque
-- requête : huit colonnes de plus suivent exactement le même chemin, sans jointure.
--
-- DÉFAUT true — comme les 13 autres. Un rapport reste soumis à l'abonnement de la
-- société (subscription_types.report_* / advanced_reports) ET au module Rapports de
-- l'utilisateur (can_reports) ; ces cases n'ajoutent que la possibilité de le retirer
-- à un utilisateur précis.
--
-- REPRISE À LA CRÉATION — les cinq rapports qui héritaient d'une case parente
-- reprennent SON état, une seule fois, pour ne rien ROUVRIR : un utilisateur privé de
-- « Réparations véhicules » ne doit pas retrouver les quatre rapports de coûts par
-- effet de bord (constaté sur TN : l'utilisateur 51 de la société 14, seul concerné ;
-- 0 sur DZ). Les trois autres rapports n'avaient pas de parent : true = inchangé.
--
-- Déploiement : jouer ce fichier AVANT le pod API (colonnes mappées sur l'entité
-- User, lue au login : absente = 42703 et personne ne se connecte). DZ puis TN.
-- Idempotent : rejoué, il n'ajoute rien et ne touche à aucune ligne.

SET lock_timeout = '5s';

DO $$
DECLARE
    creation BOOLEAN := NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'users' AND column_name = 'can_report_operating_cost');
    repris INTEGER := 0;
BEGIN
    ALTER TABLE users
        ADD COLUMN IF NOT EXISTS can_report_operating_cost   BOOLEAN NOT NULL DEFAULT true,
        ADD COLUMN IF NOT EXISTS can_report_cost_evolution   BOOLEAN NOT NULL DEFAULT true,
        ADD COLUMN IF NOT EXISTS can_report_cost_ranking     BOOLEAN NOT NULL DEFAULT true,
        ADD COLUMN IF NOT EXISTS can_report_repair_frequency BOOLEAN NOT NULL DEFAULT true,
        ADD COLUMN IF NOT EXISTS can_report_monthly_fuel     BOOLEAN NOT NULL DEFAULT true,
        ADD COLUMN IF NOT EXISTS can_report_ai_fleet         BOOLEAN NOT NULL DEFAULT true,
        ADD COLUMN IF NOT EXISTS can_report_fuel_estimation  BOOLEAN NOT NULL DEFAULT true,
        ADD COLUMN IF NOT EXISTS can_report_fuel_comparison  BOOLEAN NOT NULL DEFAULT true;

    IF creation THEN
        UPDATE users SET
            can_report_operating_cost   = can_report_costs,
            can_report_cost_evolution   = can_report_costs,
            can_report_cost_ranking     = can_report_costs,
            can_report_repair_frequency = can_report_costs,
            can_report_monthly_fuel     = can_report_monthly_costs
        WHERE NOT can_report_costs OR NOT can_report_monthly_costs;
        GET DIAGNOSTICS repris = ROW_COUNT;
        RAISE NOTICE '046 : colonnes créées, % utilisateur(s) ont repris l''état de leurs cases parentes', repris;
    ELSE
        RAISE NOTICE '046 : colonnes déjà présentes, rien à faire';
    END IF;
END $$;

COMMENT ON COLUMN users.can_report_operating_cost   IS 'Rapport « Coût d''exploitation réel » (/api/reports/costs/operating). Sous can_reports.';
COMMENT ON COLUMN users.can_report_cost_evolution   IS 'Rapport « Évolution des coûts » (/api/reports/costs/evolution). Sous can_reports.';
COMMENT ON COLUMN users.can_report_cost_ranking     IS 'Rapport « Véhicules les plus coûteux » (/api/reports/costs/ranking). Sous can_reports.';
COMMENT ON COLUMN users.can_report_repair_frequency IS 'Rapport « Fréquence des réparations » (/api/reports/costs/repair-frequency). Sous can_reports.';
COMMENT ON COLUMN users.can_report_monthly_fuel     IS 'Rapport « Consommation carburant mensuel » (/api/reports/monthly-fuel : même requête que monthly-costs, réponse limitée au carburant). Sous can_reports.';
COMMENT ON COLUMN users.can_report_ai_fleet         IS 'Rapport « IA Flotte » (/api/ai-chat/fleet-report). Sous can_reports et advanced_reports de l''abonnement.';
COMMENT ON COLUMN users.can_report_fuel_estimation  IS 'Rapport « Estimation coûts carburant » (/api/fuelexpenses/statistics). Sous can_reports et report_fuel de l''abonnement.';
COMMENT ON COLUMN users.can_report_fuel_comparison  IS 'Rapport « Carburant réel vs GPS » (/api/fuelexpenses/comparison et vehicle-audit). Sous can_reports et report_fuel de l''abonnement.';
