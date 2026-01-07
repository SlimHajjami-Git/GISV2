CREATE TABLE IF NOT EXISTS poi_visits (
    id SERIAL PRIMARY KEY,
    poi_id INTEGER NOT NULL,
    vehicle_id INTEGER NOT NULL,
    device_id INTEGER,
    arrival_at TIMESTAMP NOT NULL,
    departure_at TIMESTAMP,
    duration_minutes INTEGER,
    arrival_lat DOUBLE PRECISION NOT NULL,
    arrival_lng DOUBLE PRECISION NOT NULL,
    departure_lat DOUBLE PRECISION,
    departure_lng DOUBLE PRECISION,
    notes VARCHAR(500),
    is_notified BOOLEAN DEFAULT FALSE,
    company_id INTEGER NOT NULL
);

ALTER TABLE poi_visits ADD CONSTRAINT fk_poi_visits_poi FOREIGN KEY (poi_id) REFERENCES points_of_interest("Id") ON DELETE CASCADE;
ALTER TABLE poi_visits ADD CONSTRAINT fk_poi_visits_vehicle FOREIGN KEY (vehicle_id) REFERENCES vehicles("Id") ON DELETE CASCADE;

CREATE INDEX IF NOT EXISTS idx_poi_visits_poi_arrival ON poi_visits(poi_id, arrival_at DESC);
CREATE INDEX IF NOT EXISTS idx_poi_visits_vehicle_arrival ON poi_visits(vehicle_id, arrival_at DESC);
CREATE INDEX IF NOT EXISTS idx_poi_visits_company ON poi_visits(company_id);
