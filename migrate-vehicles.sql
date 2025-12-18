-- Migrate data from old vehicles table to new EF Core Vehicles table
INSERT INTO "Vehicles" ("Name", "Type", "Brand", "Model", "Plate", "Year", "Color", "Status", "HasGps", "Mileage", "CompanyId", "CreatedAt", "UpdatedAt")
SELECT 
    name, 
    'camion', 
    COALESCE(brand, 'Unknown'), 
    COALESCE(model, 'Unknown'), 
    plate_number, 
    2025, 
    color, 
    'available', 
    CASE WHEN EXISTS (SELECT 1 FROM devices d WHERE d.vehicle_id = v.id) THEN true ELSE false END,
    0, 
    1, 
    created_at, 
    updated_at 
FROM vehicles v
WHERE plate_number IS NOT NULL 
  AND plate_number NOT LIKE 'TEST%'
  AND NOT EXISTS (SELECT 1 FROM "Vehicles" vv WHERE vv."Plate" = v.plate_number);
