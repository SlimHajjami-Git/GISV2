-- Fix admin@belive.tn password with valid BCrypt hash for "Admin@2026"
UPDATE users 
SET password_hash = '$2a$11$WGhtfeVPezc1xdVfRHzMu.7fNZ5IKvuswo4FIeTMWcNwOluFu59Ia'
WHERE email = 'admin@belive.tn';

SELECT id, email, first_name, role_id, substring(password_hash, 1, 30) as hash_prefix 
FROM users 
WHERE email = 'admin@belive.tn';
