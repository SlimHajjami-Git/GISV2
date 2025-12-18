-- Copy password hash from hajjami.selim@gmail.com to admin@test.com
UPDATE "Users" 
SET "PasswordHash" = (SELECT "PasswordHash" FROM "Users" WHERE "Email" = 'hajjami.selim@gmail.com')
WHERE "Email" = 'admin@test.com';

-- Verify
SELECT "Email", substring("PasswordHash", 1, 20) as hash_prefix FROM "Users" WHERE "Email" IN ('admin@test.com', 'hajjami.selim@gmail.com');
