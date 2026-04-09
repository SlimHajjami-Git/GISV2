-- Add tours module permission columns
-- User-level: can_tours (default false for existing users)
ALTER TABLE users ADD COLUMN IF NOT EXISTS can_tours boolean NOT NULL DEFAULT false;

-- Subscription-level: module_tours (default true since tours was previously unprotected)
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS module_tours boolean NOT NULL DEFAULT true;

-- Separate fuel from costs: add can_fuel (user-level) and module_fuel (subscription-level)
ALTER TABLE users ADD COLUMN IF NOT EXISTS can_fuel boolean NOT NULL DEFAULT false;
ALTER TABLE subscription_types ADD COLUMN IF NOT EXISTS module_fuel boolean NOT NULL DEFAULT true;

-- Copy can_costs → can_fuel for existing users who already had costs access
UPDATE users SET can_fuel = can_costs WHERE can_costs = true;

-- Add playback user-level permission
ALTER TABLE users ADD COLUMN IF NOT EXISTS can_playback boolean NOT NULL DEFAULT false;

-- Give playback to existing users who have monitoring access
UPDATE users SET can_playback = can_monitoring WHERE can_monitoring = true;
