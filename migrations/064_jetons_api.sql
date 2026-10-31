-- 064 — Jetons d'API à usage unique (demande d'un client du 28/10/2026, précisée le 31/10/2026).
--
-- Constat : un client voulait lire les arrêts de ses véhicules d'une date à une autre depuis son
-- propre système. L'API existait (rapport d'arrêts), mais elle n'accepte que le jeton d'une
-- session, obtenu avec e-mail et mot de passe puis renouvelé chaque jour : le client devait
-- conserver un mot de passe dans son logiciel.
--
-- Modèle : depuis son profil, l'utilisateur RESSAISIT son e-mail et son mot de passe et reçoit un
-- jeton réservé à l'API. Le jeton n'ouvre qu'UN appel de lecture des arrêts : il est consommé dès
-- que cet appel est accepté, et expire de toute façon au bout de quelques minutes. Il emprunte les
-- droits et le périmètre de véhicules de l'utilisateur qui l'a généré. Seule l'empreinte SHA-256
-- est stockée : le jeton complet n'est montré qu'une fois, à la génération.
--
-- Ordre : à jouer AVANT le pod API qui mappe la table. Rejouable sans effet.

CREATE TABLE IF NOT EXISTS api_tokens (
    id           serial PRIMARY KEY,
    company_id   integer      NOT NULL,
    user_id      integer      NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    token_prefix varchar(16)  NOT NULL,
    token_hash   varchar(64)  NOT NULL,
    expires_at   timestamptz  NOT NULL,
    used_at      timestamptz  NULL,
    created_ip   varchar(64)  NULL,
    created_at   timestamptz  NOT NULL DEFAULT now(),
    updated_at   timestamptz  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_api_tokens_token_hash ON api_tokens (token_hash);
CREATE INDEX IF NOT EXISTS ix_api_tokens_user ON api_tokens (user_id, created_at DESC);
