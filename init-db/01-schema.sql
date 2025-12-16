-- Table des véhicules
CREATE TABLE IF NOT EXISTS vehicles (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    plate_number VARCHAR(20),
    brand VARCHAR(50),
    model VARCHAR(50),
    color VARCHAR(30),
    driver_name VARCHAR(100),
    driver_phone VARCHAR(20),
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- Table des devices (trackers GPS)
CREATE TABLE IF NOT EXISTS devices (
    id SERIAL PRIMARY KEY,
    device_uid VARCHAR(50) UNIQUE NOT NULL,
    label VARCHAR(100),
    protocol_type VARCHAR(50),
    vehicle_id INTEGER REFERENCES vehicles(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- Table des positions
CREATE TABLE IF NOT EXISTS positions (
    id SERIAL PRIMARY KEY,
    device_id INTEGER REFERENCES devices(id) ON DELETE CASCADE,
    recorded_at TIMESTAMP,
    lat DOUBLE PRECISION,
    lng DOUBLE PRECISION,
    speed_kph DOUBLE PRECISION,
    course_deg DOUBLE PRECISION,
    altitude_m DOUBLE PRECISION,
    ignition_on BOOLEAN,
    fuel_raw SMALLINT,
    power_voltage SMALLINT,
    mems_x SMALLINT,
    mems_y SMALLINT,
    mems_z SMALLINT,
    rpm INTEGER,
    send_flag INTEGER,
    added_info BIGINT,
    signal_quality INTEGER,
    satellites INTEGER,
    is_valid BOOLEAN,
    is_real_time BOOLEAN,
    raw_payload TEXT,
    metadata JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Table des événements
CREATE TABLE IF NOT EXISTS device_events (
    id SERIAL PRIMARY KEY,
    device_id INTEGER REFERENCES devices(id) ON DELETE CASCADE,
    event_type VARCHAR(50) NOT NULL,
    event_data JSONB,
    recorded_at TIMESTAMP,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Index pour performances
CREATE INDEX IF NOT EXISTS idx_positions_device_id ON positions(device_id);
CREATE INDEX IF NOT EXISTS idx_positions_recorded_at ON positions(recorded_at DESC);
CREATE INDEX IF NOT EXISTS idx_devices_vehicle_id ON devices(vehicle_id);
