-- Add new admin user
-- Password hash for "Admin" using bcrypt
INSERT INTO "Users" ("Name", "Email", "PasswordHash", "Phone", "CompanyId", "Status", "Roles", "Permissions", "CreatedAt", "UpdatedAt")
VALUES (
    'Selim Hajjami',
    'hajjami.selim@gmail.com',
    '$2a$11$rK7gQU.kcU0bR6YQzC.k4.XVQfX7FbZP4fE0dK9U8LmK1R3xJ1cOe',
    NULL,
    2,
    'active',
    ARRAY['admin'],
    ARRAY['all'],
    NOW(),
    NOW()
)
ON CONFLICT ("Email") DO UPDATE SET
    "PasswordHash" = '$2a$11$rK7gQU.kcU0bR6YQzC.k4.XVQfX7FbZP4fE0dK9U8LmK1R3xJ1cOe',
    "Status" = 'active',
    "UpdatedAt" = NOW();
