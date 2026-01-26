-- ================================================================================
--                    SCRIPT DE MIGRATION PRODUCTION
--                    GIS V2 - 26 Janvier 2026
-- ================================================================================
-- 
-- IMPORTANT: Exécuter ces commandes sur la base de production PostgreSQL
-- Base de données: gis_v2
-- Utilisateur: postgres
--
-- ORDRE D'EXÉCUTION:
-- 1. Faire un backup de la base
-- 2. Exécuter ce script
-- 3. Redémarrer l'API
-- 4. Vérifier les logs
--
-- ================================================================================

-- ================================================================================
-- SECTION 1: MODIFICATIONS TABLE VEHICLES (Échéances documents)
-- ================================================================================
-- Source: SPECIFICATIONS_BACKEND_JANVIER_2026.txt (Section 11.2)
-- Déjà appliqué en dev via fix_vehicles.sql

ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS "InsuranceExpiry" timestamp with time zone;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS "TechnicalInspectionExpiry" timestamp with time zone;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS "TaxExpiry" timestamp with time zone;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS "RegistrationExpiry" timestamp with time zone;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS "TransportPermitExpiry" timestamp with time zone;

-- ================================================================================
-- SECTION 2: MODIFICATIONS TABLE VEHICLE_COSTS (Documents)
-- ================================================================================
-- Source: SPECIFICATIONS_BACKEND_JANVIER_2026.txt (Section 11.3)

ALTER TABLE vehicle_costs ADD COLUMN IF NOT EXISTS "ExpiryDate" timestamp with time zone;
ALTER TABLE vehicle_costs ADD COLUMN IF NOT EXISTS "DocumentNumber" VARCHAR(100);
ALTER TABLE vehicle_costs ADD COLUMN IF NOT EXISTS "DocumentUrl" VARCHAR(500);

-- ================================================================================
-- SECTION 3: MODIFICATIONS TABLE SUPPLIERS (Garages)
-- ================================================================================
-- Source: SPECIFICATIONS_BACKEND_JANVIER_2026.txt (Section 4.1)

ALTER TABLE suppliers ADD COLUMN IF NOT EXISTS "PostalCode" VARCHAR(20);

-- Modifier Rating de INT vers DECIMAL (si nécessaire)
-- Note: Vérifier le type actuel avant d'exécuter
-- ALTER TABLE suppliers ALTER COLUMN "Rating" TYPE DECIMAL(3,1) USING COALESCE("Rating", 0)::DECIMAL(3,1);

-- ================================================================================
-- SECTION 4: TABLE SUPPLIER_SERVICES (Nouvelle)
-- ================================================================================
-- Source: SPECIFICATIONS_BACKEND_JANVIER_2026.txt (Section 3.1)

CREATE TABLE IF NOT EXISTS supplier_services (
    "Id" SERIAL PRIMARY KEY,
    "SupplierId" INT NOT NULL,
    "ServiceCode" VARCHAR(50) NOT NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    CONSTRAINT "FK_SupplierServices_Suppliers" 
        FOREIGN KEY ("SupplierId") REFERENCES suppliers("Id") ON DELETE CASCADE,
    CONSTRAINT "UQ_SupplierServices_Supplier_Service" 
        UNIQUE ("SupplierId", "ServiceCode")
);

CREATE INDEX IF NOT EXISTS "IX_SupplierServices_SupplierId" ON supplier_services("SupplierId");

-- ================================================================================
-- SECTION 5: TABLE ACCIDENT_CLAIMS (Nouvelle)
-- ================================================================================
-- Source: SPECIFICATIONS_BACKEND_JANVIER_2026.txt (Section 12.2)

CREATE TABLE IF NOT EXISTS "AccidentClaims" (
    "Id" SERIAL PRIMARY KEY,
    "ClaimNumber" VARCHAR(50) NOT NULL UNIQUE,
    "CompanyId" INT NOT NULL,
    "VehicleId" INT NOT NULL,
    "DriverId" INT,
    "AccidentDate" DATE NOT NULL,
    "AccidentTime" TIME NOT NULL,
    "Location" VARCHAR(500) NOT NULL,
    "Latitude" DECIMAL(10,7),
    "Longitude" DECIMAL(10,7),
    "WeatherConditions" VARCHAR(50),
    "RoadConditions" VARCHAR(50),
    "Description" TEXT NOT NULL,
    "Severity" VARCHAR(20) NOT NULL,
    "EstimatedDamage" DECIMAL(10,2) NOT NULL,
    "ApprovedAmount" DECIMAL(10,2),
    "Status" VARCHAR(20) NOT NULL DEFAULT 'draft',
    "ThirdPartyInvolved" BOOLEAN DEFAULT FALSE,
    "PoliceReportNumber" VARCHAR(100),
    "MileageAtAccident" INT,
    "DamagedZones" VARCHAR(500),
    "Witnesses" TEXT,
    "AdditionalNotes" TEXT,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    "CreatedByUserId" INT,
    CONSTRAINT "FK_AccidentClaims_Vehicles" 
        FOREIGN KEY ("VehicleId") REFERENCES vehicles("id") ON DELETE CASCADE,
    CONSTRAINT "FK_AccidentClaims_Users" 
        FOREIGN KEY ("CreatedByUserId") REFERENCES users("id"),
    CONSTRAINT "FK_AccidentClaims_Drivers" 
        FOREIGN KEY ("DriverId") REFERENCES users("id")
);

CREATE INDEX IF NOT EXISTS "IX_AccidentClaims_CompanyId" ON "AccidentClaims"("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_AccidentClaims_VehicleId" ON "AccidentClaims"("VehicleId");
CREATE INDEX IF NOT EXISTS "IX_AccidentClaims_Status" ON "AccidentClaims"("Status");
CREATE INDEX IF NOT EXISTS "IX_AccidentClaims_AccidentDate" ON "AccidentClaims"("AccidentDate");

-- ================================================================================
-- SECTION 6: TABLE ACCIDENT_CLAIM_THIRD_PARTIES (Nouvelle)
-- ================================================================================
-- Source: SPECIFICATIONS_BACKEND_JANVIER_2026.txt (Section 12.3)

CREATE TABLE IF NOT EXISTS "AccidentClaimThirdParties" (
    "Id" SERIAL PRIMARY KEY,
    "ClaimId" INT NOT NULL,
    "Name" VARCHAR(200),
    "Phone" VARCHAR(50),
    "VehiclePlate" VARCHAR(50),
    "VehicleModel" VARCHAR(100),
    "InsuranceCompany" VARCHAR(200),
    "InsuranceNumber" VARCHAR(100),
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    CONSTRAINT "FK_AccidentClaimThirdParties_Claims" 
        FOREIGN KEY ("ClaimId") REFERENCES "AccidentClaims"("Id") ON DELETE CASCADE
);

-- ================================================================================
-- SECTION 7: TABLE ACCIDENT_CLAIM_DOCUMENTS (Nouvelle)
-- ================================================================================
-- Source: SPECIFICATIONS_BACKEND_JANVIER_2026.txt (Section 12.4)

CREATE TABLE IF NOT EXISTS "AccidentClaimDocuments" (
    "Id" SERIAL PRIMARY KEY,
    "ClaimId" INT NOT NULL,
    "DocumentType" VARCHAR(50) NOT NULL,
    "FileName" VARCHAR(255) NOT NULL,
    "FileUrl" VARCHAR(500) NOT NULL,
    "FileSize" INT,
    "MimeType" VARCHAR(100),
    "UploadedAt" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    CONSTRAINT "FK_AccidentClaimDocuments_Claims" 
        FOREIGN KEY ("ClaimId") REFERENCES "AccidentClaims"("Id") ON DELETE CASCADE
);

-- ================================================================================
-- SECTION 8: TABLE MAINTENANCE_TEMPLATES (Nouvelle)
-- ================================================================================
-- Source: SPECIFICATIONS_BACKEND_JANVIER_2026.txt (Section 13.2)

CREATE TABLE IF NOT EXISTS "MaintenanceTemplates" (
    "Id" SERIAL PRIMARY KEY,
    "CompanyId" INT NOT NULL,
    "Name" VARCHAR(100) NOT NULL,
    "Description" VARCHAR(500),
    "Category" VARCHAR(50) NOT NULL,
    "Priority" VARCHAR(20) NOT NULL DEFAULT 'medium',
    "IntervalKm" INT,
    "IntervalMonths" INT,
    "EstimatedCost" DECIMAL(10,2),
    "IsActive" BOOLEAN DEFAULT TRUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    CONSTRAINT "CK_MaintenanceTemplates_Interval" 
        CHECK ("IntervalKm" IS NOT NULL OR "IntervalMonths" IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS "IX_MaintenanceTemplates_CompanyId" ON "MaintenanceTemplates"("CompanyId");

-- ================================================================================
-- SECTION 9: TABLE VEHICLE_MAINTENANCE_SCHEDULES (Nouvelle)
-- ================================================================================
-- Source: SPECIFICATIONS_BACKEND_JANVIER_2026.txt (Section 13.3)

CREATE TABLE IF NOT EXISTS "VehicleMaintenanceSchedules" (
    "Id" SERIAL PRIMARY KEY,
    "VehicleId" INT NOT NULL,
    "TemplateId" INT NOT NULL,
    "LastDoneDate" DATE,
    "LastDoneKm" INT,
    "NextDueDate" DATE,
    "NextDueKm" INT,
    "Status" VARCHAR(20) DEFAULT 'upcoming',
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    CONSTRAINT "FK_VehicleMaintenanceSchedules_Vehicles" 
        FOREIGN KEY ("VehicleId") REFERENCES vehicles("id") ON DELETE CASCADE,
    CONSTRAINT "FK_VehicleMaintenanceSchedules_Templates" 
        FOREIGN KEY ("TemplateId") REFERENCES "MaintenanceTemplates"("Id") ON DELETE CASCADE,
    CONSTRAINT "UQ_VehicleMaintenanceSchedule" UNIQUE ("VehicleId", "TemplateId")
);

CREATE INDEX IF NOT EXISTS "IX_VehicleMaintenanceSchedules_VehicleId" ON "VehicleMaintenanceSchedules"("VehicleId");
CREATE INDEX IF NOT EXISTS "IX_VehicleMaintenanceSchedules_Status" ON "VehicleMaintenanceSchedules"("Status");

-- ================================================================================
-- SECTION 10: TABLE MAINTENANCE_LOGS (Nouvelle)
-- ================================================================================
-- Source: SPECIFICATIONS_BACKEND_JANVIER_2026.txt (Section 13.4)

CREATE TABLE IF NOT EXISTS "MaintenanceLogs" (
    "Id" SERIAL PRIMARY KEY,
    "VehicleId" INT NOT NULL,
    "TemplateId" INT NOT NULL,
    "ScheduleId" INT,
    "CostId" INT,
    "DoneDate" DATE NOT NULL,
    "DoneKm" INT NOT NULL,
    "ActualCost" DECIMAL(10,2) NOT NULL,
    "SupplierId" INT,
    "Notes" TEXT,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    CONSTRAINT "FK_MaintenanceLogs_Vehicles" 
        FOREIGN KEY ("VehicleId") REFERENCES vehicles("id"),
    CONSTRAINT "FK_MaintenanceLogs_Templates" 
        FOREIGN KEY ("TemplateId") REFERENCES "MaintenanceTemplates"("Id"),
    CONSTRAINT "FK_MaintenanceLogs_Schedules" 
        FOREIGN KEY ("ScheduleId") REFERENCES "VehicleMaintenanceSchedules"("Id"),
    CONSTRAINT "FK_MaintenanceLogs_Suppliers" 
        FOREIGN KEY ("SupplierId") REFERENCES suppliers("Id")
);

CREATE INDEX IF NOT EXISTS "IX_MaintenanceLogs_VehicleId" ON "MaintenanceLogs"("VehicleId");

-- ================================================================================
-- SECTION 11: MODIFICATIONS TABLE MAINTENANCE_RECORDS (Optionnel)
-- ================================================================================
-- Source: SPECIFICATIONS_BACKEND_JANVIER_2026.txt (Section 4.2)

ALTER TABLE maintenance_records ADD COLUMN IF NOT EXISTS "SupplierId" INT;
-- Note: FK vers suppliers si la table existe avec le bon nom

-- ================================================================================
-- FIN DU SCRIPT
-- ================================================================================

-- Vérification des tables créées:
-- SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;

-- Vérification des colonnes vehicles:
-- SELECT column_name, data_type FROM information_schema.columns WHERE table_name = 'vehicles' AND column_name LIKE '%Expiry%';
