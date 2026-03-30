-- Device Events table: tracks GPS device lifecycle events (restart, disconnect, tamper)
-- Used to detect manual disconnection of GPS devices by drivers

CREATE TABLE IF NOT EXISTS device_events (
    id BIGSERIAL PRIMARY KEY,
    device_id INTEGER NOT NULL REFERENCES gps_devices(id),
    vehicle_id INTEGER,
    company_id INTEGER NOT NULL DEFAULT 1,
    event_type VARCHAR(50) NOT NULL,          -- 'restart', 'gsm_reset', 'disconnect', 'tamper_suspected'
    event_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    offline_duration_secs INTEGER,            -- seconds between last known position and this event
    last_known_lat DOUBLE PRECISION,
    last_known_lon DOUBLE PRECISION,
    last_known_address TEXT,
    was_moving BOOLEAN DEFAULT FALSE,         -- was vehicle moving before disconnect?
    details JSONB,                            -- additional context (raw frame, firmware version, etc.)
    acknowledged BOOLEAN NOT NULL DEFAULT FALSE,  -- has an operator acknowledged the alert?
    acknowledged_by INTEGER,                  -- user_id who acknowledged
    acknowledged_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_device_events_device_id ON device_events(device_id);
CREATE INDEX idx_device_events_company_id ON device_events(company_id);
CREATE INDEX idx_device_events_event_at ON device_events(event_at DESC);
CREATE INDEX idx_device_events_type ON device_events(event_type);
CREATE INDEX idx_device_events_unack ON device_events(company_id, acknowledged) WHERE NOT acknowledged;
