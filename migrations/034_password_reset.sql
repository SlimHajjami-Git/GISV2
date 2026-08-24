-- ============================================================================
-- 034 — Réinitialisation de mot de passe (« mot de passe oublié »)
--
-- Jusqu'ici le lien « Mot de passe oublié ? » de l'écran de connexion était un
-- href="#" : aucun point d'entrée n'existait côté API. Un utilisateur qui
-- perdait son mot de passe devait passer par un administrateur.
--
-- Le jeton suit exactement le patron déjà en place pour la confirmation
-- d'adresse (email_verification_token / _expires_at) : 32 octets aléatoires
-- encodés en base64url, à usage unique, avec une échéance courte.
--
-- Pourquoi deux colonnes sur `users` plutôt qu'une table dédiée : un compte n'a
-- qu'une demande valide à la fois — en émettre une nouvelle doit invalider la
-- précédente, ce qu'une simple écriture obtient gratuitement. Une table
-- séparée aurait laissé cohabiter plusieurs jetons vivants pour le même
-- compte, ce qui est précisément ce qu'on ne veut pas.
--
-- Idempotent : réexécutable sans dommage.
-- ============================================================================

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS password_reset_token      varchar(128),
    ADD COLUMN IF NOT EXISTS password_reset_expires_at timestamptz;

-- La recherche se fait PAR JETON lors de la réinitialisation. Sans index, elle
-- balaierait toute la table des utilisateurs à chaque tentative — y compris
-- lors d'un martèlement de jetons au hasard.
--
-- Index PARTIEL : seules les lignes portant une demande en cours sont
-- indexées, soit une poignée à tout instant sur des dizaines de milliers de
-- comptes. UNIQUE : deux comptes ne peuvent pas partager un jeton vivant.
CREATE UNIQUE INDEX IF NOT EXISTS ix_users_password_reset_token
    ON users (password_reset_token)
    WHERE password_reset_token IS NOT NULL;

COMMENT ON COLUMN users.password_reset_token IS
    'Jeton de réinitialisation à usage unique (base64url, 32 octets). Effacé dès consommation.';
COMMENT ON COLUMN users.password_reset_expires_at IS
    'Échéance du jeton ci-dessus. Passée cette date le jeton est refusé même s''il est encore stocké.';
