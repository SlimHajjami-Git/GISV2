-- Fix fuel_types codes to match frontend values
UPDATE fuel_types SET "Code"='essence', "Name"='Essence' WHERE "Id"=2;
UPDATE fuel_types SET "Code"='sans_plomb', "Name"='Essence Sans Plomb' WHERE "Id"=3;
UPDATE fuel_types SET "Code"='gpl', "Name"='GPL' WHERE "Id"=4;
UPDATE fuel_types SET "Code"='gnv', "Name"='GNV' WHERE "Id"=5;
UPDATE fuel_types SET "Code"='electrique', "Name"='Électrique' WHERE "Id"=6;
UPDATE fuel_types SET "Code"='hybride', "Name"='Hybride' WHERE "Id"=7;
UPDATE fuel_types SET "Code"='hybride_rechargeable', "Name"='Hybride Rechargeable' WHERE "Id"=8;

-- Set FuelType and fuel_tank_capacity on existing vehicles
UPDATE vehicles SET "FuelType"='diesel', fuel_tank_capacity=80 WHERE id=3;
UPDATE vehicles SET "FuelType"='diesel', fuel_tank_capacity=60 WHERE id=4;

-- Verify
SELECT "Id", "Code", "Name" FROM fuel_types ORDER BY "Id";
SELECT id, name, "FuelType", fuel_tank_capacity FROM vehicles WHERE gps_device_id IS NOT NULL;
SELECT fp."Id", ft."Code", fp."PricePerLiter", fp."IsActive" FROM fuel_pricing fp JOIN fuel_types ft ON fp."FuelTypeId" = ft."Id" WHERE fp."CompanyId"=1;
