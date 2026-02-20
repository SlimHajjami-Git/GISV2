-- Add geofence-based tracking and deadline logic to tour_waypoints
-- 2026-02-20

ALTER TABLE tour_waypoints ADD COLUMN IF NOT EXISTS geofence_id INTEGER REFERENCES geofences(id) ON DELETE SET NULL;
ALTER TABLE tour_waypoints ADD COLUMN IF NOT EXISTS estimated_leg_minutes INTEGER NOT NULL DEFAULT 0;
ALTER TABLE tour_waypoints ADD COLUMN IF NOT EXISTS deadline_margin_minutes INTEGER NOT NULL DEFAULT 60;
ALTER TABLE tour_waypoints ADD COLUMN IF NOT EXISTS waypoint_status VARCHAR(20) NOT NULL DEFAULT 'pending';

-- Index for geofence lookups
CREATE INDEX IF NOT EXISTS idx_tour_waypoints_geofence_id ON tour_waypoints(geofence_id) WHERE geofence_id IS NOT NULL;

-- Index for active waypoint monitoring
CREATE INDEX IF NOT EXISTS idx_tour_waypoints_status ON tour_waypoints(waypoint_status) WHERE waypoint_status IN ('pending', 'temps_depasse');
