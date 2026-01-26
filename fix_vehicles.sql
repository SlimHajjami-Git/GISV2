ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS "InsuranceExpiry" timestamp with time zone;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS "TechnicalInspectionExpiry" timestamp with time zone;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS "TaxExpiry" timestamp with time zone;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS "RegistrationExpiry" timestamp with time zone;
ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS "TransportPermitExpiry" timestamp with time zone;
