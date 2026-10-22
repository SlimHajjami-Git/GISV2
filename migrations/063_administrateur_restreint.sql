-- 063 — Rôle « Administrateur restreint » pour la console d'administration (demande de Karim du 22/10/2026).
--
-- Constat : la console /admin était en « tout ou rien ». Un seul rôle système (« Administrateur »)
-- existait, et toute route /api/admin était ouverte à tout administrateur système : impossible de
-- donner à un collaborateur (compte d'Adel) les seuls écrans Sociétés, Users, Véhicules et
-- Monitoring sans lui ouvrir aussi Abonnements, Boîtiers GPS, Trames, Feature Control, etc.
--
-- Modèle : un second rôle système, dans la même société que le rôle « Administrateur » existant
-- (celle des administrateurs de la plateforme). Ses permissions portent la clé « admin_screens » :
-- la liste des écrans autorisés. L'API (AdminScreenAccess, PermissionMiddleware) n'ouvre que les
-- fonctions de ces écrans, jamais les suppressions ni la réinitialisation des données d'une
-- société, ni « se connecter en tant que » ; la console masque le reste du menu. Un rôle système
-- SANS cette clé garde l'accès complet : les administrateurs actuels ne changent pas.
--
-- Le compte d'Adel N'EST PAS créé ici (pas de mot de passe dans un fichier) : Karim le crée dans
-- /admin → Users, société des administrateurs, rôle « Administrateur restreint ».
--
-- Ordre : à jouer AVANT le pod API qui sait lire « admin_screens ». Rejouable sans effet.

INSERT INTO roles (name, description, societe_id, is_company_admin, is_system_role, permissions, created_at, updated_at)
SELECT 'Administrateur restreint',
       'Console d''administration limitée : Sociétés, Users, Véhicules, Monitoring ; aucune suppression.',
       admin_role.societe_id,
       true,
       true,
       '{"admin_screens": ["clients", "users", "vehicles", "monitoring"]}'::jsonb,
       now(),
       now()
FROM (
    SELECT societe_id
    FROM roles
    WHERE is_system_role AND name = 'Administrateur'
    ORDER BY id
    LIMIT 1
) AS admin_role
WHERE NOT EXISTS (
    SELECT 1 FROM roles WHERE is_system_role AND name = 'Administrateur restreint'
);
