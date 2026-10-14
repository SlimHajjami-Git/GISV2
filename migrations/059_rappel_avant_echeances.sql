-- 059 — « Rappel avant (jours) » pour toutes les échéances : carte grise, autorisation de
--       transport et permis chauffeur (campagne de tests GPA du 06/10/2026, erreur n°4).
--
-- Constat : le champ « Rappel avant (jours) » ne servait à rien. La fenêtre de renouvellement
-- ne l'envoyait pas ; la fiche chauffeur l'envoyait (permitReminderDays) mais ni l'API ni la
-- table drivers ne l'avaient ; et les trois colonnes existantes de vehicles (assurance,
-- vignette, visite technique, migration 025) n'étaient lues par personne : alertes et écran
-- Échéances basculaient « bientôt » à 30 jours, en dur. Test de Karim le 14/10/2026 : assurance
-- renouvelée au 28/11 avec un rappel de 60 jours, restée « En règle » et rappel relu à 30.
--
-- Modèle : chaque échéance a son nombre de jours de rappel (30 par défaut, valeur d'avant). Il
-- décide quand l'échéance passe « Expire bientôt » (écran Échéances, compteurs, fiche véhicule)
-- et quand l'alerte part. Le seuil « urgent » (15 jours) ne change pas. Sur TN le 14/10/2026,
-- les 505 véhicules sont à 30 : aucun affichage ne change avant qu'un client modifie la valeur.
--
-- À jouer AVANT le pod API (qui lit et écrit ces colonnes : sinon 42703 sur vehicles et drivers).

ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS registration_reminder_days integer NOT NULL DEFAULT 30;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS transport_permit_reminder_days integer NOT NULL DEFAULT 30;
ALTER TABLE drivers ADD COLUMN IF NOT EXISTS permit_reminder_days integer NOT NULL DEFAULT 30;
