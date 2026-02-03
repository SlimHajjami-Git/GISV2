SELECT u.id, u.email, r."Name", r."IsSystemAdmin", r."IsCompanyAdmin" 
FROM "Users" u 
JOIN "Roles" r ON u."RoleId" = r."Id" 
WHERE u.email = 'admin@belive.ma';
