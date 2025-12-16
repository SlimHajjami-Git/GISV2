-- Véhicules de test
INSERT INTO vehicles (name, plate_number, brand, model, color, driver_name, driver_phone) VALUES
    ('Camion Livraison Nord', 'A-12345-MA', 'Mercedes', 'Sprinter', 'Blanc', 'Ahmed Benali', '+212 6 12 34 56 78'),
    ('Fourgon Centre', 'B-67890-MA', 'Renault', 'Master', 'Bleu', 'Karim Tazi', '+212 6 23 45 67 89'),
    ('Utilitaire Sud', 'C-11223-MA', 'Peugeot', 'Expert', 'Gris', 'Youssef Alami', '+212 6 34 56 78 90'),
    ('Camionnette Casablanca', 'D-44556-MA', 'Citroën', 'Jumper', 'Rouge', 'Omar Fassi', '+212 6 45 67 89 01'),
    ('Véhicule Commercial', 'E-77889-MA', 'Volkswagen', 'Crafter', 'Noir', 'Hassan Idrissi', '+212 6 56 78 90 12')
ON CONFLICT DO NOTHING;

-- Devices GPS liés aux véhicules
INSERT INTO devices (device_uid, label, protocol_type, vehicle_id) VALUES
    ('861001002935274', 'GPS-001', 'gps_type_1', 1),
    ('861001002935275', 'GPS-002', 'gps_type_1', 2),
    ('861001002935276', 'GPS-003', 'gps_type_1', 3),
    ('861001002935277', 'GPS-004', 'gps_type_1', 4),
    ('861001002935278', 'GPS-005', 'gps_type_1', 5)
ON CONFLICT (device_uid) DO UPDATE SET 
    vehicle_id = EXCLUDED.vehicle_id,
    label = EXCLUDED.label;

-- Positions de test pour Casablanca (véhicule 1)
INSERT INTO positions (device_id, recorded_at, lat, lng, speed_kph, course_deg, ignition_on, fuel_raw, power_voltage) VALUES
    ((SELECT id FROM devices WHERE device_uid = '861001002935274'), NOW() - INTERVAL '2 hours', 33.5731, -7.5898, 45, 90, true, 75, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935274'), NOW() - INTERVAL '1 hour 45 minutes', 33.5789, -7.5756, 52, 45, true, 74, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935274'), NOW() - INTERVAL '1 hour 30 minutes', 33.5892, -7.5634, 38, 30, true, 73, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935274'), NOW() - INTERVAL '1 hour 15 minutes', 33.5950, -7.5523, 60, 15, true, 72, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935274'), NOW() - INTERVAL '1 hour', 33.6012, -7.5412, 25, 0, true, 71, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935274'), NOW() - INTERVAL '30 minutes', 33.6089, -7.5334, 0, 0, false, 70, 12);

-- Positions de test pour Rabat (véhicule 2)
INSERT INTO positions (device_id, recorded_at, lat, lng, speed_kph, course_deg, ignition_on, fuel_raw, power_voltage) VALUES
    ((SELECT id FROM devices WHERE device_uid = '861001002935275'), NOW() - INTERVAL '3 hours', 34.0209, -6.8416, 55, 180, true, 80, 13),
    ((SELECT id FROM devices WHERE device_uid = '861001002935275'), NOW() - INTERVAL '2 hours 30 minutes', 34.0150, -6.8350, 48, 200, true, 79, 13),
    ((SELECT id FROM devices WHERE device_uid = '861001002935275'), NOW() - INTERVAL '2 hours', 34.0089, -6.8289, 62, 220, true, 78, 13),
    ((SELECT id FROM devices WHERE device_uid = '861001002935275'), NOW() - INTERVAL '1 hour 30 minutes', 34.0025, -6.8201, 40, 190, true, 77, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935275'), NOW() - INTERVAL '1 hour', 33.9956, -6.8145, 35, 175, true, 76, 12);

-- Positions de test pour Marrakech (véhicule 3)
INSERT INTO positions (device_id, recorded_at, lat, lng, speed_kph, course_deg, ignition_on, fuel_raw, power_voltage) VALUES
    ((SELECT id FROM devices WHERE device_uid = '861001002935276'), NOW() - INTERVAL '4 hours', 31.6295, -7.9811, 30, 90, true, 65, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935276'), NOW() - INTERVAL '3 hours', 31.6350, -7.9750, 45, 60, true, 63, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935276'), NOW() - INTERVAL '2 hours', 31.6420, -7.9680, 50, 45, true, 61, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935276'), NOW() - INTERVAL '1 hour', 31.6489, -7.9612, 0, 0, false, 60, 12);

-- Positions de test pour Tanger (véhicule 4)
INSERT INTO positions (device_id, recorded_at, lat, lng, speed_kph, course_deg, ignition_on, fuel_raw, power_voltage) VALUES
    ((SELECT id FROM devices WHERE device_uid = '861001002935277'), NOW() - INTERVAL '5 hours', 35.7595, -5.8340, 40, 270, true, 90, 13),
    ((SELECT id FROM devices WHERE device_uid = '861001002935277'), NOW() - INTERVAL '4 hours', 35.7650, -5.8420, 55, 280, true, 88, 13),
    ((SELECT id FROM devices WHERE device_uid = '861001002935277'), NOW() - INTERVAL '3 hours', 35.7712, -5.8510, 48, 290, true, 86, 13),
    ((SELECT id FROM devices WHERE device_uid = '861001002935277'), NOW() - INTERVAL '2 hours', 35.7780, -5.8589, 35, 300, true, 84, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935277'), NOW() - INTERVAL '1 hour', 35.7845, -5.8670, 60, 285, true, 82, 12);

-- Positions de test pour Fès (véhicule 5)
INSERT INTO positions (device_id, recorded_at, lat, lng, speed_kph, course_deg, ignition_on, fuel_raw, power_voltage) VALUES
    ((SELECT id FROM devices WHERE device_uid = '861001002935278'), NOW() - INTERVAL '6 hours', 34.0181, -5.0078, 25, 135, true, 70, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935278'), NOW() - INTERVAL '5 hours', 34.0230, -5.0120, 42, 120, true, 68, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935278'), NOW() - INTERVAL '4 hours', 34.0289, -5.0178, 38, 100, true, 66, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935278'), NOW() - INTERVAL '3 hours', 34.0345, -5.0234, 50, 90, true, 64, 12),
    ((SELECT id FROM devices WHERE device_uid = '861001002935278'), NOW() - INTERVAL '2 hours', 34.0401, -5.0289, 0, 0, false, 62, 12);
