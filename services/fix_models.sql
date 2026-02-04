-- Nettoyer les doublons de marques
DELETE FROM brands WHERE "Id" > 24;

-- Insérer les modèles avec les bons IDs
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Hilux', "Id", 'pickup', true, NOW() FROM brands WHERE "Name"='Toyota' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Land Cruiser', "Id", 'suv', true, NOW() FROM brands WHERE "Name"='Toyota' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Corolla', "Id", 'sedan', true, NOW() FROM brands WHERE "Name"='Toyota' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Yaris', "Id", 'hatchback', true, NOW() FROM brands WHERE "Name"='Toyota' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Hiace', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Toyota' LIMIT 1;

INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Clio', "Id", 'hatchback', true, NOW() FROM brands WHERE "Name"='Renault' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Megane', "Id", 'sedan', true, NOW() FROM brands WHERE "Name"='Renault' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Kangoo', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Renault' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Master', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Renault' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Trafic', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Renault' LIMIT 1;

INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT '208', "Id", 'hatchback', true, NOW() FROM brands WHERE "Name"='Peugeot' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT '308', "Id", 'hatchback', true, NOW() FROM brands WHERE "Name"='Peugeot' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Partner', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Peugeot' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Expert', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Peugeot' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Boxer', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Peugeot' LIMIT 1;

INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'C3', "Id", 'hatchback', true, NOW() FROM brands WHERE "Name"='Citroën' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Berlingo', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Citroën' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Jumpy', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Citroën' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Jumper', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Citroën' LIMIT 1;

INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Golf', "Id", 'hatchback', true, NOW() FROM brands WHERE "Name"='Volkswagen' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Caddy', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Volkswagen' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Transporter', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Volkswagen' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Crafter', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Volkswagen' LIMIT 1;

INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Sprinter', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Mercedes-Benz' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Vito', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Mercedes-Benz' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Actros', "Id", 'truck', true, NOW() FROM brands WHERE "Name"='Mercedes-Benz' LIMIT 1;

INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Transit', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Ford' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Ranger', "Id", 'pickup', true, NOW() FROM brands WHERE "Name"='Ford' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Focus', "Id", 'hatchback', true, NOW() FROM brands WHERE "Name"='Ford' LIMIT 1;

INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Ducato', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Fiat' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Doblo', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Fiat' LIMIT 1;

INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Corsa', "Id", 'hatchback', true, NOW() FROM brands WHERE "Name"='Opel' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Astra', "Id", 'hatchback', true, NOW() FROM brands WHERE "Name"='Opel' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Combo', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Opel' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Vivaro', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Opel' LIMIT 1;

INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'i20', "Id", 'hatchback', true, NOW() FROM brands WHERE "Name"='Hyundai' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Tucson', "Id", 'suv', true, NOW() FROM brands WHERE "Name"='Hyundai' LIMIT 1;

INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Logan', "Id", 'sedan', true, NOW() FROM brands WHERE "Name"='Dacia' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Sandero', "Id", 'hatchback', true, NOW() FROM brands WHERE "Name"='Dacia' LIMIT 1;
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Duster', "Id", 'suv', true, NOW() FROM brands WHERE "Name"='Dacia' LIMIT 1;

INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'D-Max', "Id", 'pickup', true, NOW() FROM brands WHERE "Name"='Isuzu' LIMIT 1;

INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") 
SELECT 'Daily', "Id", 'van', true, NOW() FROM brands WHERE "Name"='Iveco' LIMIT 1;

-- Vérification
SELECT 'Brands: ' || COUNT(*) FROM brands;
SELECT 'Models: ' || COUNT(*) FROM vehicle_models;
SELECT 'Fuel Types: ' || COUNT(*) FROM fuel_types;
SELECT 'Part Categories: ' || COUNT(*) FROM part_categories;
