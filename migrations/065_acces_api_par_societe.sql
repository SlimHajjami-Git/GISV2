-- 065 — Accès API activé société par société (demande de Karim du 01/11/2026).
--
-- Constat : le jeton d'API à usage unique (migration 064, Profil > Accès API) se générait par
-- n'importe quel utilisateur actif de n'importe quelle société, alors que la règle de gestion de
-- Karim est : l'accès API est réservé à l'administrateur de la société, et seulement pour les
-- clients que Belive a activés (VITAL au départ). En production au 01/11 : 2 jetons générés, par
-- un administrateur de HERTZ, aucun par VITAL.
--
-- Modèle : interrupteur par société, faux par défaut, réglé par Belive dans la fiche société de
-- la console d'administration (PUT /api/admin/company/{id}/api-access). Sans lui, ni génération
-- ni section « Accès API » dans le profil, même pour l'administrateur ; et un jeton déjà généré
-- cesse de fonctionner (règle relue à chaque appel, ApiTokenAccess.AccountRefusal).
--
-- Ordre : à jouer AVANT le pod API qui lit cette colonne (sinon 42703 au chargement des sociétés,
-- connexion comprise). Rejouable sans effet. Après le déploiement, activer VITAL dans sa fiche.

ALTER TABLE societes ADD COLUMN IF NOT EXISTS api_access_enabled boolean NOT NULL DEFAULT false;
