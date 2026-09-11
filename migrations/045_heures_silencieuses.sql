-- 045 — Heures silencieuses persistées par utilisateur — recette client du 11/09/2026.
--
-- CONSTAT — L'interrupteur « Heures silencieuses » de Paramètres > Notifications
-- n'écrivait que dans le localStorage du navigateur (clé appSettings) : aucune
-- API ne le recevait et aucun code d'envoi ne le lisait. NotificationService
-- (SignalR + push FCM) envoyait donc à toute heure ; sur TN, 40 des 106
-- notifications de l'utilisateur 45 ont été créées entre 22:00 et 07:00. Les
-- champs QuietHours existants (NotificationSettings de l'entité orpheline
-- UserSettings, MaintenanceTemplate) sont des restes jamais branchés.
--
-- POURQUOI SUR users — le réglage est PERSONNEL (un gestionnaire peut vouloir le
-- silence la nuit, un autre non) et il est lu à chaque notification par une
-- lecture par clé primaire : trois colonnes sur la ligne déjà chargée au login
-- valent mieux qu'une table de préférences à joindre.
--
-- TYPES — interval comme geofences.active_start_time / active_end_time (TimeSpan?
-- côté EF). Heures exprimées dans le fuseau de la SOCIÉTÉ (Timezone du JSON
-- societes.settings, repli Africa/Tunis), jamais celui du navigateur. La plage peut
-- passer minuit (22:00 → 07:00). Défaut désactivé : aucun changement de
-- comportement pour qui ne l'active pas.
--
-- EFFET — pendant la plage, une notification non « critical » est quand même
-- créée et arrive dans la cloche, mais sans push FCM ni toast à l'écran
-- (payload SignalR Silent = true). Les e-mails ne sont pas concernés.
--
-- ORDRE DE DÉPLOIEMENT — CE SQL SE JOUE AVANT LE NOUVEAU POD API, sur DZ puis
-- TN. Les colonnes sont mappées par EF sur l'entité User, chargée au login : un
-- pod démarré avant la migration renverrait « 42703 column does not exist » et
-- la connexion casserait pour tout le monde.

-- users est lue à chaque connexion : si un verrou traîne, on échoue vite (et on
-- relance) plutôt que de faire attendre tous les logins derrière l'ALTER TABLE.
SET lock_timeout = '5s';

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS quiet_hours_enabled BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS quiet_hours_start   INTERVAL NULL,
    ADD COLUMN IF NOT EXISTS quiet_hours_end     INTERVAL NULL;

COMMENT ON COLUMN users.quiet_hours_enabled IS
    'Heures silencieuses actives : pendant la plage, pas de push FCM ni de toast pour les notifications non critical (elles restent dans la cloche). Réglé par PUT /api/users/me/quiet-hours.';

COMMENT ON COLUMN users.quiet_hours_start IS
    'Début de la plage silencieuse, heure locale de la société (peut être > quiet_hours_end : la plage passe alors minuit).';

COMMENT ON COLUMN users.quiet_hours_end IS
    'Fin de la plage silencieuse (exclue), heure locale de la société.';
