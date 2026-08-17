-- 032 : temporisation des alertes "demarrage impossible"
--
-- Contexte (17/08/2026) : la surveillance batterie par la tension est une
-- impasse sur ce materiel — cinq formulations testees, cinq refutees. Le seul
-- signal qui a resiste est le DEMARREUR : des tentatives de contact repetees
-- suivies d'une immobilisation, sur un vehicule qui roulait la veille.
--
-- Cette colonne empeche de renotifier le meme boitier toutes les 15 minutes
-- tant que le vehicule reste en panne.

ALTER TABLE gps_devices
    ADD COLUMN IF NOT EXISTS last_start_failure_alert_at TIMESTAMP WITH TIME ZONE NULL;

COMMENT ON COLUMN gps_devices.last_start_failure_alert_at IS
    'Derniere alerte "demarrage impossible" envoyee pour ce boitier (temporisation 24 h).';
