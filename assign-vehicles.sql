-- Assign vehicles to Calipso company (user hajjami.selim@gmail.com)
UPDATE vehicles SET company_id = (
    SELECT "CompanyId" FROM "Users" WHERE "Email" = 'hajjami.selim@gmail.com'
) WHERE company_id = 2;

-- Verify
SELECT id, name, plate_number, company_id FROM vehicles;
SELECT "Id", "Name" FROM "Companies";
