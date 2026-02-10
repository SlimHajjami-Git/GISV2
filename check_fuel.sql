SELECT * FROM fuel_types ORDER BY "Id";
SELECT fp.*, ft."Code", ft."Name" FROM fuel_pricing fp JOIN fuel_types ft ON fp."FuelTypeId" = ft."Id" ORDER BY fp."CompanyId", ft."Code";
