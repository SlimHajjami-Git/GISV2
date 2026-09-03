-- 041 — Cycle SEMESTRIEL (6 mois) achetable en ligne — recette client 03/09/2026.
--
-- CONSTAT — L'écran Abonnement d'un compte GPA en euros propose deux formules,
-- « Abonnement annuel » et « Abonnement semestriel ». La seconde n'était qu'une
-- carte de vitrine avec un lien « Nous contacter » : rien, du plan jusqu'à la
-- commande, ne connaissait le semestre. Le backend ne sait facturer que
-- monthly / quarterly / yearly — un cycle de 6 mois n'existait nulle part.
--
-- POURQUOI DEUX COLONNES ET PAS UN CALCUL — le semestre n'est pas « la moitié de
-- l'annuel » : c'est un tarif commercial à part (4 €/véhicule/mois × 6 = 24,00,
-- là où l'annuel descend à 3 €/mois soit 36,00 l'an). Le déduire d'un autre
-- cycle enfermerait le tarif dans du code au lieu de le laisser en base, où le
-- sys_admin peut le corriger. La durée suit la même logique que les cycles
-- existants (monthly/quarterly/yearly_duration_days) : 180 jours par défaut.
--
-- UN CYCLE À 0 N'EST PAS VENDABLE — le défaut 0 est délibéré : les autres plans
-- (offres avec GPS) n'ont pas de tarif semestriel, et la commande refuse un
-- montant nul (« Ce cycle de facturation n'est pas disponible pour cette
-- offre. »). Seul plan-basique reçoit donc un prix ici.
--
-- ORDRE DE DÉPLOIEMENT — CE SQL SE JOUE AVANT LE NOUVEAU POD API. Les deux
-- colonnes sont mappées par EF sur l'entité SubscriptionType : un pod démarré
-- avant la migration renverrait « 42703 column does not exist » sur toute
-- lecture de subscription_types, et le login casserait pour tout le monde.
--
-- Aucun recalage de données existantes : societes.billing_cycle et
-- subscription_orders.billing_cycle sont des chaînes libres, aucune ligne ne
-- portait 'semiannual' avant cette migration.

ALTER TABLE subscription_types
    ADD COLUMN IF NOT EXISTS semiannual_price numeric(10,2) NOT NULL DEFAULT 0;

ALTER TABLE subscription_types
    ADD COLUMN IF NOT EXISTS semiannual_duration_days integer NOT NULL DEFAULT 180;

-- Offre GPA : 4 €/véhicule/mois × 6 mois = 24,00 par véhicule. Le montant réel
-- de la commande reste prix × parc réel (price_per_vehicle, migration 039).
-- NB : tarif calibré EUR, comme les autres prix de ce plan. Un déploiement qui
-- vendrait cette offre en monnaie locale doit adapter le montant AVANT de jouer
-- cette migration.
UPDATE subscription_types
SET semiannual_price = 24.00,
    updated_at       = now()
WHERE code = 'plan-basique';
