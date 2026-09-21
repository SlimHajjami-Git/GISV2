-- 050 — Comptes chauffeurs pour l'application mobile (tournées envoyées au chauffeur,
-- décision de Slim du 21/09/2026 : « le chauffeur est un utilisateur »).
--
-- CONSTAT — Un chauffeur est aujourd'hui une simple fiche `drivers` (nom, permis,
-- contact), sans identifiant de connexion : `drivers.user_id` a été retiré en avril
-- quand les fiches ont été séparées des comptes. Pour recevoir une tournée sur son
-- téléphone, le chauffeur doit pouvoir se connecter à l'application mobile — et à
-- RIEN d'autre : ni le site, ni le suivi de la flotte, ni les fiches des autres.
--
-- POURQUOI CES COLONNES —
--   • `users.account_type` : le type de compte est la SEULE clé du cloisonnement côté
--     serveur (PermissionMiddleware refuse tout hors /api/driver-app à un compte
--     « driver », la connexion web le refuse, le hub SignalR le rejette). Une colonne
--     dédiée, contrainte à deux valeurs, plutôt que `employee_role` (texte libre, NULL
--     sur les 38 comptes de TN) ou qu'un rôle : les rôles sont par société et servent
--     de repli dans plusieurs créations de compte, un rôle « chauffeur » finirait
--     attribué à un salarié.
--   • `drivers.user_id` : lie la fiche (véhicule affecté, permis, tournées via
--     tours."DriverId") au compte qui se connecte. ON DELETE SET NULL : supprimer le
--     compte laisse la fiche et son historique ; unique partiel : un compte, une fiche.
--   • `tours."DriverId"` n'avait AUCUNE clé étrangère : ajoutée, ON DELETE SET NULL,
--     même règle. Sur TN toutes les tournées ont "DriverId" NULL : aucune ligne à
--     vérifier.
--
-- AUCUNE DONNÉE MODIFIÉE — tous les comptes existants sont `staff` par défaut.
--
-- ORDRE DE DÉPLOIEMENT — ce SQL se joue AVANT le pod API qui mappe User.AccountType
-- et Driver.UserId (sinon 42703 sur toute lecture d'un utilisateur : login cassé
-- pour tout le monde). Sur TN uniquement (DZ hors périmètre, décision du 20/09).

-- users.account_type ----------------------------------------------------------
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS account_type varchar(16) NOT NULL DEFAULT 'staff';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'users_account_type_check' AND conrelid = 'users'::regclass
    ) THEN
        ALTER TABLE users
            ADD CONSTRAINT users_account_type_check
            CHECK (account_type IN ('staff', 'driver'));
    END IF;
END $$;

-- Les chauffeurs sont rares : index partiel, seulement eux.
CREATE INDEX IF NOT EXISTS idx_users_account_type_driver
    ON users (company_id)
    WHERE account_type = 'driver';

COMMENT ON COLUMN users.account_type IS
    'staff = compte ordinaire (web + mobile) ; driver = chauffeur, application mobile seulement (tournées)';

-- drivers.user_id --------------------------------------------------------------
ALTER TABLE drivers
    ADD COLUMN IF NOT EXISTS user_id integer;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'drivers_user_id_fkey' AND conrelid = 'drivers'::regclass
    ) THEN
        ALTER TABLE drivers
            ADD CONSTRAINT drivers_user_id_fkey
            FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_drivers_user_id
    ON drivers (user_id)
    WHERE user_id IS NOT NULL;

COMMENT ON COLUMN drivers.user_id IS
    'Compte de connexion du chauffeur (users.account_type = driver), NULL = fiche sans accès à l''application';

-- tours."DriverId" : clé étrangère manquante ------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'tours_driver_id_fkey' AND conrelid = 'tours'::regclass
    ) THEN
        ALTER TABLE tours
            ADD CONSTRAINT tours_driver_id_fkey
            FOREIGN KEY ("DriverId") REFERENCES drivers(id) ON DELETE SET NULL;
    END IF;
END $$;
