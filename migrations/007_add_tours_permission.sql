-- Add tours module permission columns
-- User-level: CanTours (default false for existing users)
ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "CanTours" boolean NOT NULL DEFAULT false;

-- Subscription-level: ModuleTours (default true since tours was previously unprotected)
ALTER TABLE "SubscriptionTypes" ADD COLUMN IF NOT EXISTS "ModuleTours" boolean NOT NULL DEFAULT true;

-- Separate fuel from costs: add CanFuel (user-level) and ModuleFuel (subscription-level)
ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "CanFuel" boolean NOT NULL DEFAULT false;
ALTER TABLE "SubscriptionTypes" ADD COLUMN IF NOT EXISTS "ModuleFuel" boolean NOT NULL DEFAULT true;

-- Copy CanCosts → CanFuel for existing users who already had costs access
UPDATE "Users" SET "CanFuel" = "CanCosts" WHERE "CanCosts" = true;

-- Add playback user-level permission
ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "CanPlayback" boolean NOT NULL DEFAULT false;

-- Give playback to existing users who have monitoring access
UPDATE "Users" SET "CanPlayback" = "CanMonitoring" WHERE "CanMonitoring" = true;
