-- Create admin@test.com user with dummy data
-- Password: Admin (bcrypt hash)

-- Step 1: Create company for dummy data
INSERT INTO "Companies" ("Name", "Type", "SubscriptionId", "CreatedAt", "UpdatedAt")
VALUES ('Demo Transport', 'transport', 1, NOW(), NOW())
ON CONFLICT DO NOTHING;

-- Get the company ID
DO $$
DECLARE
    v_company_id INTEGER;
    v_user_id INTEGER;
BEGIN
    -- Get or create company
    SELECT "Id" INTO v_company_id FROM "Companies" WHERE "Name" = 'Demo Transport';
    IF v_company_id IS NULL THEN
        INSERT INTO "Companies" ("Name", "Type", "SubscriptionId", "CreatedAt", "UpdatedAt")
        VALUES ('Demo Transport', 'transport', 1, NOW(), NOW())
        RETURNING "Id" INTO v_company_id;
    END IF;

    -- Create user admin@test.com
    INSERT INTO "Users" ("Name", "Email", "PasswordHash", "Phone", "CompanyId", "Status", "Roles", "Permissions", "CreatedAt", "UpdatedAt")
    VALUES (
        'Admin Demo',
        'admin@test.com',
        '$2a$11$K5.Xr0FVhj7yQkqg9XKqOuLm6E.MwvZqR9h.6mR3xQ3vL2Y0Y6pXa',
        '+212600000000',
        v_company_id,
        'active',
        ARRAY['admin'],
        ARRAY['all'],
        NOW(),
        NOW()
    )
    ON CONFLICT ("Email") DO UPDATE SET
        "PasswordHash" = '$2a$11$K5.Xr0FVhj7yQkqg9XKqOuLm6E.MwvZqR9h.6mR3xQ3vL2Y0Y6pXa',
        "CompanyId" = v_company_id,
        "Status" = 'active'
    RETURNING "Id" INTO v_user_id;

    -- Create dummy vehicles for this company
    INSERT INTO vehicles (name, plate_number, brand, model, color, type, year, status, has_gps, mileage, company_id, created_at, updated_at) VALUES
    ('Camion Alpha', 'DEMO-001', 'Mercedes', 'Actros', 'Blanc', 'camion', 2023, 'available', true, 45000, v_company_id, NOW(), NOW()),
    ('Fourgon Beta', 'DEMO-002', 'Renault', 'Master', 'Bleu', 'utilitaire', 2022, 'in_use', true, 78000, v_company_id, NOW(), NOW()),
    ('Citadine Gamma', 'DEMO-003', 'Peugeot', '208', 'Rouge', 'citadine', 2024, 'available', false, 12000, v_company_id, NOW(), NOW()),
    ('SUV Delta', 'DEMO-004', 'Toyota', 'Land Cruiser', 'Noir', 'suv', 2023, 'maintenance', true, 34000, v_company_id, NOW(), NOW()),
    ('Utilitaire Epsilon', 'DEMO-005', 'Volkswagen', 'Transporter', 'Gris', 'utilitaire', 2021, 'available', true, 95000, v_company_id, NOW(), NOW())
    ON CONFLICT DO NOTHING;

    -- Create dummy employees
    INSERT INTO "Employees" ("Name", "Email", "Phone", "Role", "Status", "CompanyId", "LicenseNumber", "CreatedAt", "UpdatedAt") VALUES
    ('Mohammed Alami', 'malami@demo.com', '+212661234567', 'driver', 'active', v_company_id, 'DL-12345', NOW(), NOW()),
    ('Fatima Benani', 'fbenani@demo.com', '+212662345678', 'driver', 'active', v_company_id, 'DL-23456', NOW(), NOW()),
    ('Ahmed Tazi', 'atazi@demo.com', '+212663456789', 'supervisor', 'active', v_company_id, NULL, NOW(), NOW()),
    ('Sara Idrissi', 'sidrissi@demo.com', '+212664567890', 'accountant', 'active', v_company_id, NULL, NOW(), NOW())
    ON CONFLICT DO NOTHING;

    RAISE NOTICE 'Created admin@test.com with company_id: %', v_company_id;
END $$;

-- Verify
SELECT 'Users:' as info;
SELECT "Id", "Name", "Email", "CompanyId" FROM "Users" WHERE "Email" = 'admin@test.com';

SELECT 'Vehicles for Demo Transport:' as info;
SELECT id, name, plate_number, status, company_id FROM vehicles WHERE company_id = (SELECT "Id" FROM "Companies" WHERE "Name" = 'Demo Transport');

SELECT 'Employees for Demo Transport:' as info;
SELECT "Id", "Name", "Role" FROM "Employees" WHERE "CompanyId" = (SELECT "Id" FROM "Companies" WHERE "Name" = 'Demo Transport');
