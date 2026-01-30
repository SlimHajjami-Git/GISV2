-- Fleet Management Migration

CREATE TABLE IF NOT EXISTS departments (
    "Id" SERIAL PRIMARY KEY,
    "CompanyId" INTEGER NOT NULL,
    "Name" VARCHAR(100) NOT NULL,
    "Description" VARCHAR(500),
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS fuel_types (
    "Id" SERIAL PRIMARY KEY,
    "Code" VARCHAR(20) NOT NULL UNIQUE,
    "Name" VARCHAR(50) NOT NULL,
    "IsSystem" BOOLEAN NOT NULL DEFAULT TRUE
);

INSERT INTO fuel_types ("Code", "Name", "IsSystem") VALUES 
    ('essence', 'Essence', TRUE),
    ('diesel', 'Diesel', TRUE),
    ('hybride', 'Hybride', TRUE),
    ('electrique', 'Électrique', TRUE),
    ('gpl', 'GPL', TRUE)
ON CONFLICT ("Code") DO NOTHING;

CREATE TABLE IF NOT EXISTS fuel_pricing (
    "Id" SERIAL PRIMARY KEY,
    "CompanyId" INTEGER NOT NULL,
    "FuelTypeId" INTEGER NOT NULL,
    "PricePerLiter" DECIMAL(10,3) NOT NULL,
    "EffectiveFrom" TIMESTAMP WITH TIME ZONE NOT NULL,
    "EffectiveTo" TIMESTAMP WITH TIME ZONE,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS "DepartmentId" INTEGER;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS "SpeedLimit" INTEGER DEFAULT 120;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS "FuelType" VARCHAR(20) DEFAULT 'diesel';

CREATE TABLE IF NOT EXISTS speed_limit_alerts (
    "Id" SERIAL PRIMARY KEY,
    "VehicleId" INTEGER NOT NULL,
    "CompanyId" INTEGER NOT NULL,
    "SpeedLimit" INTEGER NOT NULL,
    "ActualSpeed" INTEGER NOT NULL,
    "Latitude" DOUBLE PRECISION NOT NULL,
    "Longitude" DOUBLE PRECISION NOT NULL,
    "Address" VARCHAR(500),
    "RecordedAt" TIMESTAMP WITH TIME ZONE NOT NULL,
    "IsAcknowledged" BOOLEAN NOT NULL DEFAULT FALSE,
    "AcknowledgedAt" TIMESTAMP WITH TIME ZONE,
    "AcknowledgedById" INTEGER,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS IX_departments_CompanyId ON departments("CompanyId");
CREATE INDEX IF NOT EXISTS IX_fuel_pricing_CompanyId ON fuel_pricing("CompanyId");
CREATE INDEX IF NOT EXISTS IX_fuel_pricing_FuelTypeId ON fuel_pricing("FuelTypeId");
CREATE INDEX IF NOT EXISTS IX_speed_limit_alerts_VehicleId ON speed_limit_alerts("VehicleId");
CREATE INDEX IF NOT EXISTS IX_speed_limit_alerts_CompanyId ON speed_limit_alerts("CompanyId");
