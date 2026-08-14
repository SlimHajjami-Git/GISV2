-- 031: Fiabilité du capteur de tension, par boîtier
--
-- Contexte (14/08/2026, panne batterie 259 TU 4987 non détectée) : l'octet de
-- tension remonté par la plupart des boîtiers ne mesure rien. Test de
-- l'alternateur sur 7 jours de flotte TN : 213 véhicules sur 243 rapportent
-- EXACTEMENT la même valeur moteur tournant (>20 km/h) et moteur éteint, alors
-- qu'un alternateur fait physiquement monter la tension de ~12,6 V à ~14,2 V.
-- L'application affichait pourtant « 12,9 V » et « 100 % » sur un véhicule
-- incapable de démarrer — une affirmation fausse, pire qu'un silence.
--
-- Ces deux colonnes portent le verdict, recalculé chaque jour par
-- VoltageSensorAuditService. Le chemin de lecture n'affiche la tension QUE
-- lorsque reliable = true ; NULL (jamais audité, ou pas assez de données)
-- vaut « on n'affiche rien », le défaut prudent.
--
-- Sans transaction explicite : deux ADD COLUMN idempotents, sans réécriture
-- de table (colonnes nullables sans défaut).

ALTER TABLE gps_devices
    ADD COLUMN IF NOT EXISTS voltage_sensor_reliable   BOOLEAN NULL,
    ADD COLUMN IF NOT EXISTS voltage_sensor_checked_at TIMESTAMP WITH TIME ZONE NULL;

COMMENT ON COLUMN gps_devices.voltage_sensor_reliable IS
    'true = le capteur reagit a l''alternateur (ecart >= 3 unites entre roulage et repos) ET la tension au repos est plausible pour du 12 V. NULL/false = valeur non affichable.';
COMMENT ON COLUMN gps_devices.voltage_sensor_checked_at IS
    'Derniere execution de VoltageSensorAuditService pour ce boitier.';
