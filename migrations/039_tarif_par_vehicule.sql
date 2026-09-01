-- 039 — Tarification PAR VÉHICULE pour l'offre GPA (recette client 01/09/2026).
--
-- Constat : l'écran Abonnement affichait « Prochain règlement 299,00 EUR » —
-- le forfait annuel d'origine du plan, figé dans societes.next_payment_amount
-- à la création de la société. L'offre GPA se vend 3 €/véhicule/mois en
-- engagement annuel : pour 2 véhicules, 3 × 2 × 12 = 72 €, pas 299.
--
-- Modèle : subscription_types.price_per_vehicle = true signifie que les prix
-- des cycles (monthly/quarterly/yearly_price) s'entendent PAR VÉHICULE ; le
-- montant dû = prix du cycle × nombre de véhicules (au moins 1). L'API
-- recalcule ce montant à chaque lecture pour ces plans.

ALTER TABLE subscription_types
    ADD COLUMN IF NOT EXISTS price_per_vehicle boolean NOT NULL DEFAULT false;

-- Le plan GPA passe au modèle par véhicule, avec des prix par véhicule :
--   monthly 4 (sans engagement), quarterly 12 (3 × 4), yearly 36 (12 × 3).
-- Les anciens 29/79/299 étaient des forfaits d'un modèle abandonné (yearly
-- avait déjà été corrigé à 36 à la main sur TN le 25/08).
-- NB : tarifs calibrés EUR (offre Europe — les comptes GPA sont facturés en
-- EUR depuis le 28/08). Sur un déploiement qui vendrait ce plan en monnaie
-- locale (DZ…), adapter les montants AVANT de jouer cette migration.
UPDATE subscription_types
SET price_per_vehicle = true,
    monthly_price     = 4.00,
    quarterly_price   = 12.00,
    yearly_price      = 36.00,
    updated_at        = now()
WHERE code = 'plan-basique';

-- Recale le montant mémorisé des sociétés déjà rattachées à un plan par
-- véhicule (l'API recalcule à l'affichage, mais les écrans d'administration
-- lisent la colonne) : prix du cycle × parc réel, au moins 1 véhicule.
UPDATE societes s
SET next_payment_amount = (
        CASE lower(coalesce(s.billing_cycle, 'yearly'))
            WHEN 'monthly'   THEN st.monthly_price
            WHEN 'quarterly' THEN st.quarterly_price
            ELSE                  st.yearly_price
        END
    ) * GREATEST(1, (SELECT count(*) FROM vehicles v WHERE v.company_id = s.id)),
    updated_at = now()
FROM subscription_types st
WHERE st.id = s.subscription_type_id
  AND st.price_per_vehicle;
