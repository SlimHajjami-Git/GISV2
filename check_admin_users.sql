SELECT id, name, email, user_type, roles, permissions 
FROM users 
WHERE user_type = 'system_admin' 
   OR 'super_admin' = ANY(roles) 
   OR 'admin' = ANY(roles) 
LIMIT 10;
