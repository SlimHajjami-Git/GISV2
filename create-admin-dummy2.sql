-- Create admin@test.com user with dummy data
-- Use existing company (Test Company, ID=1) for admin@test.com

-- Update admin@test.com to use company 1 and set correct password
UPDATE "Users" SET 
    "PasswordHash" = '$2a$11$K5.Xr0FVhj7yQkqg9XKqOuLm6E.MwvZqR9h.6mR3xQ3vL2Y0Y6pXa',
    "Status" = 'active',
    "UpdatedAt" = NOW()
WHERE "Email" = 'admin@test.com';

-- If user doesn't exist, create it
INSERT INTO "Users" ("Name", "Email", "PasswordHash", "Phone", "CompanyId", "Status", "Roles", "Permissions", "CreatedAt", "UpdatedAt")
SELECT 'Admin Demo', 'admin@test.com', '$2a$11$K5.Xr0FVhj7yQkqg9XKqOuLm6E.MwvZqR9h.6mR3xQ3vL2Y0Y6pXa', '+212600000000', 1, 'active', ARRAY['admin'], ARRAY['all'], NOW(), NOW()
WHERE NOT EXISTS (SELECT 1 FROM "Users" WHERE "Email" = 'admin@test.com');

-- Create dummy vehicles for company 1 (Test Company)
INSERT INTO vehicles (name, plate_number, brand, model, color, type, year, status, has_gps, mileage, company_id, driver_name, driver_phone, created_at, updated_at) VALUES
('Camion Alpha', 'DEMO-001', 'Mercedes', 'Actros', 'Blanc', 'camion', 2023, 'available', true, 45000, 1, 'Mohammed Alami', '+212661234567', NOW(), NOW()),
('Fourgon Beta', 'DEMO-002', 'Renault', 'Master', 'Bleu', 'utilitaire', 2022, 'in_use', true, 78000, 1, 'Fatima Benani', '+212662345678', NOW(), NOW()),
('Citadine Gamma', 'DEMO-003', 'Peugeot', '208', 'Rouge', 'citadine', 2024, 'available', false, 12000, 1, NULL, NULL, NOW(), NOW()),
('SUV Delta', 'DEMO-004', 'Toyota', 'Land Cruiser', 'Noir', 'suv', 2023, 'maintenance', true, 34000, 1, 'Ahmed Tazi', '+212663456789', NOW(), NOW()),
('Utilitaire Epsilon', 'DEMO-005', 'Volkswagen', 'Transporter', 'Gris', 'utilitaire', 2021, 'available', true, 95000, 1, NULL, NULL, NOW(), NOW())
ON CONFLICT DO NOTHING;

-- Create dummy employees for company 1
INSERT INTO "Employees" ("Name", "Email", "Phone", "Role", "Status", "CompanyId", "LicenseNumber", "CreatedAt", "UpdatedAt") VALUES
('Mohammed Alami', 'malami@demo.com', '+212661234567', 'driver', 'active', 1, 'DL-12345', NOW(), NOW()),
('Fatima Benani', 'fbenani@demo.com', '+212662345678', 'driver', 'active', 1, 'DL-23456', NOW(), NOW()),
('Ahmed Tazi', 'atazi@demo.com', '+212663456789', 'supervisor', 'active', 1, NULL, NOW(), NOW()),
('Sara Idrissi', 'sidrissi@demo.com', '+212664567890', 'accountant', 'active', 1, NULL, NOW(), NOW())
ON CONFLICT DO NOTHING;

-- Verify
SELECT 'User admin@test.com:' as info;
SELECT "Id", "Name", "Email", "CompanyId" FROM "Users" WHERE "Email" = 'admin@test.com';

SELECT 'Vehicles for company 1:' as info;
SELECT id, name, plate_number, status, company_id FROM vehicles WHERE company_id = 1;

SELECT 'Employees for company 1:' as info;
SELECT "Id", "Name", "Role" FROM "Employees" WHERE "CompanyId" = 1;
