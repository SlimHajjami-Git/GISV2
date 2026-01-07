-- Seed script for default company "Belive" for testing
-- Run this after migrations to have a default tenant

-- Insert default subscription if not exists
INSERT INTO subscriptions ("Id", "Name", "Type", "Price", "Features", "GpsTracking", "GpsInstallation", "MaxVehicles", "MaxUsers", "MaxGpsDevices", "MaxGeofences", "BillingCycle", "IsActive", "CreatedAt")
VALUES (1, 'Plan Pro', 'parc_gps', 999.00, ARRAY['gps_tracking', 'geofencing', 'reports', 'alerts', 'maintenance'], true, true, 100, 20, 100, 50, 'monthly', true, NOW())
ON CONFLICT ("Id") DO NOTHING;

-- Insert default company "Belive"
INSERT INTO companies ("Id", "Name", "Type", "Address", "City", "Country", "Phone", "Email", "SubscriptionId", "IsActive", "SubscriptionExpiresAt", "CreatedAt", "UpdatedAt")
VALUES (1, 'Belive', 'transport', '123 Avenue Mohammed V', 'Casablanca', 'MA', '+212 522 123456', 'contact@belive.ma', 1, true, NOW() + INTERVAL '1 year', NOW(), NOW())
ON CONFLICT ("Id") DO NOTHING;

-- Insert default admin user for Belive (password: Admin123!)
INSERT INTO users ("Id", "Name", "Email", "Phone", "PasswordHash", "Roles", "Permissions", "Status", "CompanyId", "CreatedAt", "UpdatedAt")
VALUES (1, 'Admin Belive', 'admin@belive.ma', '+212 600 000000', '$2a$11$rBNpVqLDkMXru4JTLZ8xluCOrGvuQatcXoGq1dPXeQIjLvDSzwHWe', ARRAY['admin'], ARRAY['all'], 'active', 1, NOW(), NOW())
ON CONFLICT ("Id") DO NOTHING;

-- Reset sequences to avoid conflicts
SELECT setval('subscriptions_"Id"_seq', (SELECT COALESCE(MAX("Id"), 0) + 1 FROM subscriptions), false);
SELECT setval('companies_"Id"_seq', (SELECT COALESCE(MAX("Id"), 0) + 1 FROM companies), false);
SELECT setval('users_"Id"_seq', (SELECT COALESCE(MAX("Id"), 0) + 1 FROM users), false);

-- Output confirmation
SELECT 'Belive company created successfully' as status;
SELECT c."Id", c."Name", c."Email", s."Name" as subscription 
FROM companies c 
JOIN subscriptions s ON c."SubscriptionId" = s."Id" 
WHERE c."Name" = 'Belive';
