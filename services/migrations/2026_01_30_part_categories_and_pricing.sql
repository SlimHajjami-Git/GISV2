-- ============================================================================
-- Migration: Part Categories, Vehicle Parts & Part Pricing
-- Date: 2026-01-30
-- Description: Adds tables for managing vehicle parts catalog and company-specific pricing
-- ============================================================================

BEGIN;

-- ============================================================================
-- 1. PART CATEGORIES TABLE
-- ============================================================================
CREATE TABLE IF NOT EXISTS part_categories (
    "Id" SERIAL PRIMARY KEY,
    "Name" VARCHAR(100) NOT NULL,
    "Description" VARCHAR(500),
    "Icon" VARCHAR(50),
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_part_categories_name ON part_categories ("Name");

-- ============================================================================
-- 2. VEHICLE PARTS TABLE
-- ============================================================================
CREATE TABLE IF NOT EXISTS vehicle_parts (
    "Id" SERIAL PRIMARY KEY,
    "CategoryId" INTEGER NOT NULL REFERENCES part_categories("Id") ON DELETE CASCADE,
    "Name" VARCHAR(100) NOT NULL,
    "Description" VARCHAR(500),
    "PartNumber" VARCHAR(50),
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_vehicle_parts_categoryid ON vehicle_parts ("CategoryId");
CREATE INDEX IF NOT EXISTS ix_vehicle_parts_partnumber ON vehicle_parts ("PartNumber");

-- ============================================================================
-- 3. PART PRICING TABLE (Company-specific prices)
-- ============================================================================
CREATE TABLE IF NOT EXISTS part_pricing (
    "Id" SERIAL PRIMARY KEY,
    "CompanyId" INTEGER NOT NULL REFERENCES societes(id) ON DELETE CASCADE,
    "PartId" INTEGER NOT NULL REFERENCES vehicle_parts("Id") ON DELETE CASCADE,
    "Price" DECIMAL(18,2) NOT NULL,
    "Supplier" VARCHAR(200),
    "Notes" VARCHAR(500),
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_part_pricing_company_part UNIQUE ("CompanyId", "PartId")
);

CREATE INDEX IF NOT EXISTS ix_part_pricing_companyid ON part_pricing ("CompanyId");
CREATE INDEX IF NOT EXISTS ix_part_pricing_partid ON part_pricing ("PartId");

-- ============================================================================
-- 4. SEED DEFAULT PART CATEGORIES
-- ============================================================================
INSERT INTO part_categories ("Name", "Description", "Icon", "IsActive")
VALUES 
    ('Moteur', 'Pièces du moteur et composants associés', '🔧', TRUE),
    ('Freinage', 'Système de freinage', '🛑', TRUE),
    ('Suspension', 'Amortisseurs et suspension', '🔩', TRUE),
    ('Électrique', 'Composants électriques et électroniques', '⚡', TRUE),
    ('Carrosserie', 'Pièces de carrosserie', '🚗', TRUE),
    ('Transmission', 'Boîte de vitesses et embrayage', '⚙️', TRUE),
    ('Climatisation', 'Système de climatisation et chauffage', '❄️', TRUE),
    ('Filtres', 'Filtres à air, huile, carburant', '🔘', TRUE),
    ('Pneus', 'Pneumatiques et jantes', '⭕', TRUE),
    ('Éclairage', 'Phares, feux et ampoules', '💡', TRUE)
ON CONFLICT DO NOTHING;

-- ============================================================================
-- 5. SEED DEFAULT VEHICLE PARTS
-- ============================================================================
INSERT INTO vehicle_parts ("CategoryId", "Name", "Description", "IsActive")
SELECT c."Id", p.part_name, p.part_desc, TRUE
FROM part_categories c
CROSS JOIN (VALUES
    ('Moteur', 'Filtre à huile', 'Filtre pour huile moteur'),
    ('Moteur', 'Filtre à air', 'Filtre pour air moteur'),
    ('Moteur', 'Courroie de distribution', 'Courroie de synchronisation moteur'),
    ('Moteur', 'Pompe à eau', 'Pompe de circulation du liquide de refroidissement'),
    ('Moteur', 'Bougies d''allumage', 'Bougies pour moteur essence'),
    ('Moteur', 'Injecteurs', 'Injecteurs de carburant'),
    ('Freinage', 'Plaquettes de frein avant', 'Jeu de plaquettes avant'),
    ('Freinage', 'Plaquettes de frein arrière', 'Jeu de plaquettes arrière'),
    ('Freinage', 'Disques de frein avant', 'Paire de disques avant'),
    ('Freinage', 'Disques de frein arrière', 'Paire de disques arrière'),
    ('Freinage', 'Liquide de frein', 'Liquide DOT4'),
    ('Suspension', 'Amortisseurs avant', 'Paire d''amortisseurs avant'),
    ('Suspension', 'Amortisseurs arrière', 'Paire d''amortisseurs arrière'),
    ('Suspension', 'Silent blocs', 'Jeu de silent blocs'),
    ('Électrique', 'Batterie', 'Batterie de démarrage'),
    ('Électrique', 'Alternateur', 'Alternateur de charge'),
    ('Électrique', 'Démarreur', 'Moteur de démarrage'),
    ('Transmission', 'Huile de boîte', 'Huile pour transmission'),
    ('Transmission', 'Kit d''embrayage', 'Kit complet embrayage'),
    ('Climatisation', 'Filtre habitacle', 'Filtre de climatisation'),
    ('Climatisation', 'Compresseur clim', 'Compresseur de climatisation'),
    ('Filtres', 'Filtre à carburant', 'Filtre pour carburant'),
    ('Pneus', 'Pneu été', 'Pneumatique été'),
    ('Pneus', 'Pneu hiver', 'Pneumatique hiver'),
    ('Pneus', 'Pneu 4 saisons', 'Pneumatique toutes saisons'),
    ('Éclairage', 'Ampoule phare', 'Ampoule H7/H4'),
    ('Éclairage', 'Feu arrière', 'Bloc feu arrière'),
    ('Carrosserie', 'Rétroviseur', 'Rétroviseur extérieur'),
    ('Carrosserie', 'Pare-chocs avant', 'Pare-chocs avant'),
    ('Carrosserie', 'Pare-brise', 'Pare-brise avant')
) AS p(cat_name, part_name, part_desc)
WHERE c."Name" = p.cat_name
ON CONFLICT DO NOTHING;

COMMIT;

-- ============================================================================
-- VERIFICATION QUERIES (run manually to verify)
-- ============================================================================
-- SELECT COUNT(*) FROM part_categories;
-- SELECT COUNT(*) FROM vehicle_parts;
-- SELECT COUNT(*) FROM part_pricing;
-- SELECT pc."Name" as category, COUNT(vp."Id") as parts_count 
-- FROM part_categories pc 
-- LEFT JOIN vehicle_parts vp ON vp."CategoryId" = pc."Id" 
-- GROUP BY pc."Name";
