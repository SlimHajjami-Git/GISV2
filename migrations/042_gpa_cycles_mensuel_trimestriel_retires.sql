-- 042 — Offre GPA (plan-basique) : les cycles MENSUEL et TRIMESTRIEL ne sont
-- pas proposés. Demande client de la recette du 04/09/2026.
--
-- CONSTAT — Le panneau « Commander » listait quatre cycles (Mensuel 16 EUR,
-- Trimestriel 48 EUR, Semestriel 96 EUR, Annuel 144 EUR pour 4 véhicules)
-- alors que l'offre n'en affiche que deux : annuel (3 EUR/véhicule/mois) et
-- semestriel (4 EUR/véhicule/mois). Les prix mensuel (4) et trimestriel (12)
-- avaient été posés par la migration 039 pour compléter la grille par
-- véhicule ; ils n'ont jamais fait partie de l'offre commerciale.
--
-- POURQUOI EN BASE ET NON À L'ÉCRAN — un cycle à prix 0 est REFUSÉ par le
-- serveur à la commande (« Ce cycle de facturation n'est pas disponible pour
-- cette offre ») et masqué automatiquement par l'écran (availableCycles ne
-- garde que les prix > 0). Une seule source de vérité, appliquée partout,
-- plutôt qu'un filtre d'affichage que l'API ne connaîtrait pas.
--
-- Aucune société n'est facturée en mensuel ni en trimestriel (vérifié :
-- 17/17 en annuel). Réversible : remettre un prix > 0.

UPDATE subscription_types
SET monthly_price   = 0,
    quarterly_price = 0,
    updated_at      = now()
WHERE code = 'plan-basique';
