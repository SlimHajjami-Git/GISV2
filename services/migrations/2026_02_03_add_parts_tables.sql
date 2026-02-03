-- ============================================================
-- MIGRATION: Ajouter tables part_categories et vehicle_parts
-- Date: 2026-02-03
-- ============================================================

-- Table des catégories de pièces
CREATE TABLE IF NOT EXISTS part_categories (
    "Id" SERIAL PRIMARY KEY,
    "Name" VARCHAR(100) NOT NULL,
    "Description" VARCHAR(500),
    "ParentId" INTEGER REFERENCES part_categories("Id") ON DELETE SET NULL,
    "IsActive" BOOLEAN DEFAULT TRUE,
    "SortOrder" INTEGER DEFAULT 0,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_part_categories_name ON part_categories("Name");
CREATE INDEX IF NOT EXISTS idx_part_categories_parent ON part_categories("ParentId");

-- Table des pièces véhicules
CREATE TABLE IF NOT EXISTS vehicle_parts (
    "Id" SERIAL PRIMARY KEY,
    "CategoryId" INTEGER NOT NULL REFERENCES part_categories("Id") ON DELETE CASCADE,
    "Name" VARCHAR(200) NOT NULL,
    "Description" VARCHAR(500),
    "PartNumber" VARCHAR(100),
    "Brand" VARCHAR(100),
    "Unit" VARCHAR(20) DEFAULT 'unit',
    "MinStock" INTEGER DEFAULT 0,
    "CurrentStock" INTEGER DEFAULT 0,
    "UnitPrice" DECIMAL(10,2) DEFAULT 0,
    "IsActive" BOOLEAN DEFAULT TRUE,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_vehicle_parts_category ON vehicle_parts("CategoryId");
CREATE INDEX IF NOT EXISTS idx_vehicle_parts_name ON vehicle_parts("Name");
CREATE INDEX IF NOT EXISTS idx_vehicle_parts_number ON vehicle_parts("PartNumber");

-- Insérer quelques catégories par défaut
INSERT INTO part_categories ("Name", "Description", "SortOrder") VALUES
    ('Moteur', 'Pièces moteur et composants', 1),
    ('Freinage', 'Système de freinage', 2),
    ('Filtres', 'Filtres air, huile, carburant', 3),
    ('Électrique', 'Composants électriques', 4),
    ('Suspension', 'Pièces suspension et direction', 5),
    ('Transmission', 'Boîte de vitesses et embrayage', 6),
    ('Carrosserie', 'Pièces carrosserie', 7),
    ('Pneumatiques', 'Pneus et jantes', 8)
ON CONFLICT DO NOTHING;
