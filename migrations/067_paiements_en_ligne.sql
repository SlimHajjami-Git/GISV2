-- 067 — Paiement en ligne des commandes d'abonnement par ClicToPay (demande de Slim du 06/11/2026).
--
-- Constat : une commande d'abonnement (subscription_orders) ne se réglait que hors application
-- (virement, espèces), puis la plateforme la validait à la main. ClicToPay (Monétique Tunisie,
-- manuel d'intégration « Basic 1.0 ») permet de la régler par carte : register.do crée la
-- commande chez ClicToPay et renvoie la page de paiement, getOrderStatusExtended.do donne le
-- résultat réel. Le manuel interdit de valider sur le seul retour du navigateur.
--
-- Modèle : UNE LIGNE PAR TENTATIVE de paiement d'une commande (le client peut abandonner puis
-- recommencer). order_number est NOTRE numéro envoyé à ClicToPay (unique par marchand, 32
-- caractères au plus) ; provider_order_id est l'UUID que ClicToPay renvoie. Montant en unités
-- minimales (millimes pour le dinar, centimes pour l'euro), devise ISO à 3 lettres. Rien de la
-- carte n'est conservé hormis ce que ClicToPay renvoie déjà masqué (450921**1119) et le code
-- d'autorisation.
--
-- status : pending (page de paiement ouverte) | paid (orderStatus = 2 vérifié, montant et devise
-- conformes) | failed (refusé, annulé, remboursé) | error (register.do en échec) | expired
-- (jamais payé dans les 48 h) | anomaly (payé mais montant, devise ou numéro différent : à
-- vérifier par l'équipe, la commande N'EST PAS validée).
--
-- kind : subscription (règlement d'une commande d'abonnement) | admin_test (paiement de test lancé depuis
-- la console d'administration pour éprouver ClicToPay, sans commande ni abonnement touchés).
--
-- Ordre : à jouer AVANT le pod API qui mappe cette table. Rejouable sans effet.

CREATE TABLE IF NOT EXISTS online_payments (
    id                       serial PRIMARY KEY,
    company_id               integer      NOT NULL REFERENCES societes(id) ON DELETE CASCADE,
    -- NULL pour un paiement de test lancé depuis la console admin (kind = admin_test).
    subscription_order_id    integer      REFERENCES subscription_orders(id) ON DELETE CASCADE,
    kind                     varchar(20)  NOT NULL DEFAULT 'subscription',
    provider                 varchar(20)  NOT NULL DEFAULT 'clictopay',
    order_number             varchar(32)  NOT NULL,
    provider_order_id        varchar(64),
    amount_minor             bigint       NOT NULL,
    currency                 varchar(3)   NOT NULL,
    status                   varchar(20)  NOT NULL DEFAULT 'pending',
    provider_order_status    integer,
    action_code              integer,
    action_description       varchar(512),
    card_pan_masked          varchar(32),
    approval_code            varchar(16),
    note                     varchar(512),
    created_by_user_id       integer,
    paid_at                  timestamptz,
    last_checked_at          timestamptz,
    created_at               timestamptz  NOT NULL DEFAULT now(),
    updated_at               timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_online_payments_order_number ON online_payments (order_number);
CREATE INDEX IF NOT EXISTS ix_online_payments_order ON online_payments (subscription_order_id);
-- Le rapprochement en tâche de fond ne relit que les tentatives encore en attente.
CREATE INDEX IF NOT EXISTS ix_online_payments_pending ON online_payments (created_at) WHERE status = 'pending';
