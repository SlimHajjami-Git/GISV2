-- Migration: Add AutoStopOnEntry to geofences
-- Date: 2026-02-23
-- Description: Adds auto_stop_on_entry column to geofences table
--              When true, the vehicle engine is automatically cut when entering this zone

ALTER TABLE geofences ADD COLUMN IF NOT EXISTS auto_stop_on_entry boolean NOT NULL DEFAULT false;

-- Record migration in EF history
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260223150000_AddAutoStopOnEntryToGeofence', '9.0.1')
ON CONFLICT DO NOTHING;
