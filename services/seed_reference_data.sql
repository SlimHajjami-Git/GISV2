-- Seed Reference Data for GIS V2

-- ===========================================
-- MARQUES DE VÉHICULES (brands)
-- ===========================================
INSERT INTO brands ("Name", "LogoUrl", "IsActive", "CreatedAt") VALUES
('Toyota', '/logos/toyota.png', true, NOW()),
('Renault', '/logos/renault.png', true, NOW()),
('Peugeot', '/logos/peugeot.png', true, NOW()),
('Citroën', '/logos/citroen.png', true, NOW()),
('Volkswagen', '/logos/vw.png', true, NOW()),
('Mercedes-Benz', '/logos/mercedes.png', true, NOW()),
('BMW', '/logos/bmw.png', true, NOW()),
('Audi', '/logos/audi.png', true, NOW()),
('Ford', '/logos/ford.png', true, NOW()),
('Fiat', '/logos/fiat.png', true, NOW()),
('Opel', '/logos/opel.png', true, NOW()),
('Hyundai', '/logos/hyundai.png', true, NOW()),
('Kia', '/logos/kia.png', true, NOW()),
('Nissan', '/logos/nissan.png', true, NOW()),
('Honda', '/logos/honda.png', true, NOW()),
('Suzuki', '/logos/suzuki.png', true, NOW()),
('Dacia', '/logos/dacia.png', true, NOW()),
('Iveco', '/logos/iveco.png', true, NOW()),
('MAN', '/logos/man.png', true, NOW()),
('Scania', '/logos/scania.png', true, NOW()),
('Volvo', '/logos/volvo.png', true, NOW()),
('Isuzu', '/logos/isuzu.png', true, NOW()),
('Mitsubishi', '/logos/mitsubishi.png', true, NOW()),
('Mazda', '/logos/mazda.png', true, NOW())
ON CONFLICT DO NOTHING;

-- ===========================================
-- MODÈLES DE VÉHICULES (vehicle_models)
-- ===========================================
INSERT INTO vehicle_models ("Name", "BrandId", "VehicleType", "IsActive", "CreatedAt") VALUES
-- Toyota
('Hilux', (SELECT "Id" FROM brands WHERE "Name"='Toyota'), 'pickup', true, NOW()),
('Land Cruiser', (SELECT "Id" FROM brands WHERE "Name"='Toyota'), 'suv', true, NOW()),
('Corolla', (SELECT "Id" FROM brands WHERE "Name"='Toyota'), 'sedan', true, NOW()),
('Yaris', (SELECT "Id" FROM brands WHERE "Name"='Toyota'), 'hatchback', true, NOW()),
('Hiace', (SELECT "Id" FROM brands WHERE "Name"='Toyota'), 'van', true, NOW()),
-- Renault
('Clio', (SELECT "Id" FROM brands WHERE "Name"='Renault'), 'hatchback', true, NOW()),
('Megane', (SELECT "Id" FROM brands WHERE "Name"='Renault'), 'sedan', true, NOW()),
('Kangoo', (SELECT "Id" FROM brands WHERE "Name"='Renault'), 'van', true, NOW()),
('Master', (SELECT "Id" FROM brands WHERE "Name"='Renault'), 'van', true, NOW()),
('Trafic', (SELECT "Id" FROM brands WHERE "Name"='Renault'), 'van', true, NOW()),
-- Peugeot
('208', (SELECT "Id" FROM brands WHERE "Name"='Peugeot'), 'hatchback', true, NOW()),
('308', (SELECT "Id" FROM brands WHERE "Name"='Peugeot'), 'hatchback', true, NOW()),
('Partner', (SELECT "Id" FROM brands WHERE "Name"='Peugeot'), 'van', true, NOW()),
('Expert', (SELECT "Id" FROM brands WHERE "Name"='Peugeot'), 'van', true, NOW()),
('Boxer', (SELECT "Id" FROM brands WHERE "Name"='Peugeot'), 'van', true, NOW()),
-- Citroën
('C3', (SELECT "Id" FROM brands WHERE "Name"='Citroën'), 'hatchback', true, NOW()),
('C4', (SELECT "Id" FROM brands WHERE "Name"='Citroën'), 'hatchback', true, NOW()),
('Berlingo', (SELECT "Id" FROM brands WHERE "Name"='Citroën'), 'van', true, NOW()),
('Jumpy', (SELECT "Id" FROM brands WHERE "Name"='Citroën'), 'van', true, NOW()),
('Jumper', (SELECT "Id" FROM brands WHERE "Name"='Citroën'), 'van', true, NOW()),
-- Volkswagen
('Golf', (SELECT "Id" FROM brands WHERE "Name"='Volkswagen'), 'hatchback', true, NOW()),
('Passat', (SELECT "Id" FROM brands WHERE "Name"='Volkswagen'), 'sedan', true, NOW()),
('Caddy', (SELECT "Id" FROM brands WHERE "Name"='Volkswagen'), 'van', true, NOW()),
('Transporter', (SELECT "Id" FROM brands WHERE "Name"='Volkswagen'), 'van', true, NOW()),
('Crafter', (SELECT "Id" FROM brands WHERE "Name"='Volkswagen'), 'van', true, NOW()),
-- Mercedes
('Sprinter', (SELECT "Id" FROM brands WHERE "Name"='Mercedes-Benz'), 'van', true, NOW()),
('Vito', (SELECT "Id" FROM brands WHERE "Name"='Mercedes-Benz'), 'van', true, NOW()),
('Actros', (SELECT "Id" FROM brands WHERE "Name"='Mercedes-Benz'), 'truck', true, NOW()),
('Atego', (SELECT "Id" FROM brands WHERE "Name"='Mercedes-Benz'), 'truck', true, NOW()),
('Classe C', (SELECT "Id" FROM brands WHERE "Name"='Mercedes-Benz'), 'sedan', true, NOW()),
-- Ford
('Transit', (SELECT "Id" FROM brands WHERE "Name"='Ford'), 'van', true, NOW()),
('Ranger', (SELECT "Id" FROM brands WHERE "Name"='Ford'), 'pickup', true, NOW()),
('Focus', (SELECT "Id" FROM brands WHERE "Name"='Ford'), 'hatchback', true, NOW()),
('Fiesta', (SELECT "Id" FROM brands WHERE "Name"='Ford'), 'hatchback', true, NOW()),
-- Fiat
('Ducato', (SELECT "Id" FROM brands WHERE "Name"='Fiat'), 'van', true, NOW()),
('Doblo', (SELECT "Id" FROM brands WHERE "Name"='Fiat'), 'van', true, NOW()),
('Fiorino', (SELECT "Id" FROM brands WHERE "Name"='Fiat'), 'van', true, NOW()),
('500', (SELECT "Id" FROM brands WHERE "Name"='Fiat'), 'hatchback', true, NOW()),
-- Opel
('Corsa', (SELECT "Id" FROM brands WHERE "Name"='Opel'), 'hatchback', true, NOW()),
('Astra', (SELECT "Id" FROM brands WHERE "Name"='Opel'), 'hatchback', true, NOW()),
('Combo', (SELECT "Id" FROM brands WHERE "Name"='Opel'), 'van', true, NOW()),
('Vivaro', (SELECT "Id" FROM brands WHERE "Name"='Opel'), 'van', true, NOW()),
('Movano', (SELECT "Id" FROM brands WHERE "Name"='Opel'), 'van', true, NOW()),
-- Hyundai
('i10', (SELECT "Id" FROM brands WHERE "Name"='Hyundai'), 'hatchback', true, NOW()),
('i20', (SELECT "Id" FROM brands WHERE "Name"='Hyundai'), 'hatchback', true, NOW()),
('Tucson', (SELECT "Id" FROM brands WHERE "Name"='Hyundai'), 'suv', true, NOW()),
('H100', (SELECT "Id" FROM brands WHERE "Name"='Hyundai'), 'van', true, NOW()),
-- Dacia
('Logan', (SELECT "Id" FROM brands WHERE "Name"='Dacia'), 'sedan', true, NOW()),
('Sandero', (SELECT "Id" FROM brands WHERE "Name"='Dacia'), 'hatchback', true, NOW()),
('Duster', (SELECT "Id" FROM brands WHERE "Name"='Dacia'), 'suv', true, NOW()),
('Dokker', (SELECT "Id" FROM brands WHERE "Name"='Dacia'), 'van', true, NOW()),
-- Isuzu
('D-Max', (SELECT "Id" FROM brands WHERE "Name"='Isuzu'), 'pickup', true, NOW()),
('NPR', (SELECT "Id" FROM brands WHERE "Name"='Isuzu'), 'truck', true, NOW()),
('NQR', (SELECT "Id" FROM brands WHERE "Name"='Isuzu'), 'truck', true, NOW()),
-- Iveco
('Daily', (SELECT "Id" FROM brands WHERE "Name"='Iveco'), 'van', true, NOW()),
('Eurocargo', (SELECT "Id" FROM brands WHERE "Name"='Iveco'), 'truck', true, NOW()),
('Stralis', (SELECT "Id" FROM brands WHERE "Name"='Iveco'), 'truck', true, NOW())
ON CONFLICT DO NOTHING;

-- ===========================================
-- TYPES DE CARBURANT (fuel_types)
-- ===========================================
INSERT INTO fuel_types ("Code", "Name", "IsSystem") VALUES
('diesel', 'Diesel', true),
('gasoline', 'Essence', true),
('unleaded', 'Essence Sans Plomb', true),
('lpg', 'GPL', true),
('cng', 'GNV', true),
('electric', 'Électrique', true),
('hybrid', 'Hybride', true),
('plugin_hybrid', 'Hybride Rechargeable', true)
ON CONFLICT DO NOTHING;

-- ===========================================
-- CATÉGORIES DE PIÈCES (part_categories)
-- ===========================================
INSERT INTO part_categories ("Name", "Description", "Icon", "IsActive", "CreatedAt") VALUES
('Moteur', 'Pièces du moteur et accessoires', 'engine', true, NOW()),
('Freinage', 'Plaquettes, disques, tambours, flexibles', 'brake', true, NOW()),
('Suspension', 'Amortisseurs, ressorts, rotules, silent blocs', 'suspension', true, NOW()),
('Direction', 'Crémaillère, biellettes, pompe direction', 'steering', true, NOW()),
('Transmission', 'Embrayage, boîte de vitesses, cardans', 'gearbox', true, NOW()),
('Filtration', 'Filtres air, huile, carburant, habitacle', 'filter', true, NOW()),
('Éclairage', 'Phares, feux, ampoules, clignotants', 'light', true, NOW()),
('Électricité', 'Batterie, alternateur, démarreur, bougies', 'battery', true, NOW()),
('Climatisation', 'Compresseur, condenseur, évaporateur', 'ac', true, NOW()),
('Carrosserie', 'Pare-chocs, ailes, capot, rétroviseurs', 'body', true, NOW()),
('Pneumatiques', 'Pneus, jantes, valves', 'tire', true, NOW()),
('Lubrifiants', 'Huiles moteur, transmission, liquides', 'oil', true, NOW()),
('Courroies', 'Distribution, accessoires, galets', 'belt', true, NOW()),
('Échappement', 'Pot, catalyseur, silencieux, collecteur', 'exhaust', true, NOW()),
('Refroidissement', 'Radiateur, pompe eau, thermostat, durites', 'cooling', true, NOW())
ON CONFLICT DO NOTHING;

-- ===========================================
-- Vérification
-- ===========================================
SELECT 'Brands: ' || COUNT(*) FROM brands;
SELECT 'Models: ' || COUNT(*) FROM vehicle_models;
SELECT 'Fuel Types: ' || COUNT(*) FROM fuel_types;
SELECT 'Part Categories: ' || COUNT(*) FROM part_categories;
