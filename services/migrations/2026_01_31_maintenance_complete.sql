-- ============================================================
-- MIGRATION COMPLÈTE: Système d'Entretien Programmable
-- Date: 2026-01-31
-- ============================================================

-- ============================================================
-- PARTIE 1: CRÉATION DES TABLES DE BASE (si elles n'existent pas)
-- ============================================================

-- 1.1 Table des templates de maintenance
CREATE TABLE IF NOT EXISTS maintenance_templates (
    "Id" SERIAL PRIMARY KEY,
    "CompanyId" INTEGER NOT NULL,
    "Name" VARCHAR(100) NOT NULL,
    "Description" VARCHAR(500),
    "Category" VARCHAR(50) NOT NULL,
    "Priority" VARCHAR(20) DEFAULT 'medium',
    "IntervalKm" INTEGER,
    "IntervalMonths" INTEGER,
    "EstimatedCost" DECIMAL(10,2),
    "IsActive" BOOLEAN DEFAULT TRUE,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ DEFAULT NOW(),
    warning_km INTEGER DEFAULT 1000,
    warning_days INTEGER DEFAULT 30,
    critical_km INTEGER DEFAULT 0,
    critical_days INTEGER DEFAULT 0,
    estimated_duration_minutes INTEGER,
    instructions TEXT,
    applies_to_vehicle_types TEXT[] DEFAULT '{}',
    icon VARCHAR(50) DEFAULT 'wrench',
    CONSTRAINT chk_template_interval CHECK ("IntervalKm" > 0 OR "IntervalMonths" > 0)
);

CREATE INDEX IF NOT EXISTS idx_maintenance_templates_company ON maintenance_templates("CompanyId");

-- 1.2 Table des schedules véhicule-maintenance
CREATE TABLE IF NOT EXISTS vehicle_maintenance_schedules (
    "Id" SERIAL PRIMARY KEY,
    "VehicleId" INTEGER NOT NULL,
    "TemplateId" INTEGER NOT NULL REFERENCES maintenance_templates("Id") ON DELETE CASCADE,
    "LastDoneDate" DATE,
    "LastDoneKm" INTEGER,
    "NextDueDate" DATE,
    "NextDueKm" INTEGER,
    "Status" VARCHAR(20) DEFAULT 'upcoming',
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ DEFAULT NOW(),
    is_paused BOOLEAN DEFAULT FALSE,
    paused_at TIMESTAMPTZ,
    paused_reason VARCHAR(255),
    custom_interval_km INTEGER,
    custom_interval_months INTEGER,
    last_notification_at TIMESTAMPTZ,
    notification_count INTEGER DEFAULT 0,
    notes TEXT,
    UNIQUE("VehicleId", "TemplateId")
);

CREATE INDEX IF NOT EXISTS idx_vms_vehicle ON vehicle_maintenance_schedules("VehicleId");
CREATE INDEX IF NOT EXISTS idx_vms_template ON vehicle_maintenance_schedules("TemplateId");
CREATE INDEX IF NOT EXISTS idx_vms_status ON vehicle_maintenance_schedules("Status");

-- 1.3 Table des logs de maintenance
CREATE TABLE IF NOT EXISTS maintenance_logs (
    "Id" SERIAL PRIMARY KEY,
    "VehicleId" INTEGER NOT NULL,
    "TemplateId" INTEGER NOT NULL REFERENCES maintenance_templates("Id") ON DELETE CASCADE,
    "ScheduleId" INTEGER REFERENCES vehicle_maintenance_schedules("Id") ON DELETE SET NULL,
    "CostId" INTEGER,
    "DoneDate" DATE NOT NULL,
    "DoneKm" INTEGER NOT NULL,
    "ActualCost" DECIMAL(10,2) DEFAULT 0,
    "SupplierId" INTEGER,
    "Notes" TEXT,
    "CreatedAt" TIMESTAMPTZ DEFAULT NOW(),
    technician_name VARCHAR(100),
    work_order_number VARCHAR(50),
    parts_replaced JSONB DEFAULT '[]',
    labor_hours DECIMAL(5,2),
    labor_cost DECIMAL(10,2),
    parts_cost DECIMAL(10,2),
    quality_rating INTEGER CHECK (quality_rating BETWEEN 1 AND 5),
    photos TEXT[]
);

CREATE INDEX IF NOT EXISTS idx_maint_logs_vehicle ON maintenance_logs("VehicleId");
CREATE INDEX IF NOT EXISTS idx_maint_logs_template ON maintenance_logs("TemplateId");

-- ============================================================
-- PARTIE 2: NOUVELLES TABLES
-- ============================================================

-- 2.1 Table des pièces par template
CREATE TABLE IF NOT EXISTS maintenance_template_parts (
    id SERIAL PRIMARY KEY,
    template_id INTEGER NOT NULL REFERENCES maintenance_templates("Id") ON DELETE CASCADE,
    part_name VARCHAR(200) NOT NULL,
    part_number VARCHAR(100),
    quantity INTEGER DEFAULT 1,
    unit VARCHAR(20) DEFAULT 'unit',
    estimated_unit_cost DECIMAL(10,2),
    is_required BOOLEAN DEFAULT TRUE,
    preferred_supplier_id INTEGER,
    notes TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_template_parts_template_id ON maintenance_template_parts(template_id);

-- 2.2 Table des notifications de maintenance
CREATE TABLE IF NOT EXISTS maintenance_notifications (
    id BIGSERIAL PRIMARY KEY,
    schedule_id INTEGER NOT NULL REFERENCES vehicle_maintenance_schedules("Id") ON DELETE CASCADE,
    vehicle_id INTEGER NOT NULL,
    template_id INTEGER NOT NULL REFERENCES maintenance_templates("Id") ON DELETE CASCADE,
    company_id INTEGER NOT NULL,
    notification_type VARCHAR(20) NOT NULL,
    trigger_reason VARCHAR(20) NOT NULL,
    current_km INTEGER,
    km_remaining INTEGER,
    days_remaining INTEGER,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    sent_at TIMESTAMPTZ,
    sent_channels TEXT[] DEFAULT '{}',
    acknowledged_at TIMESTAMPTZ,
    acknowledged_by INTEGER,
    dismissed_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_maint_notif_vehicle ON maintenance_notifications(vehicle_id);
CREATE INDEX IF NOT EXISTS idx_maint_notif_company ON maintenance_notifications(company_id);
CREATE INDEX IF NOT EXISTS idx_maint_notif_type ON maintenance_notifications(notification_type);
CREATE INDEX IF NOT EXISTS idx_maint_notif_schedule ON maintenance_notifications(schedule_id);

-- 2.3 Table des paramètres d'alerte par société
CREATE TABLE IF NOT EXISTS maintenance_alert_settings (
    id SERIAL PRIMARY KEY,
    company_id INTEGER NOT NULL UNIQUE,
    enable_push BOOLEAN DEFAULT TRUE,
    enable_email BOOLEAN DEFAULT TRUE,
    enable_sms BOOLEAN DEFAULT FALSE,
    notify_driver BOOLEAN DEFAULT TRUE,
    notify_supervisor BOOLEAN DEFAULT TRUE,
    notify_fleet_manager BOOLEAN DEFAULT TRUE,
    additional_emails TEXT[] DEFAULT '{}',
    additional_phones TEXT[] DEFAULT '{}',
    reminder_frequency_days INTEGER DEFAULT 7,
    max_reminders INTEGER DEFAULT 3,
    quiet_hours_start TIME,
    quiet_hours_end TIME,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- ============================================================
-- PARTIE 3: DONNÉES INITIALES
-- ============================================================

-- Templates de maintenance par défaut (pour company_id = 1)
INSERT INTO maintenance_templates ("CompanyId", "Name", "Description", "Category", "Priority", "IntervalKm", "IntervalMonths", "EstimatedCost", warning_km, warning_days, icon)
VALUES 
    (1, 'Vidange moteur', 'Changement huile moteur et filtre', 'Moteur', 'high', 10000, 6, 80.00, 1000, 30, 'oil-can'),
    (1, 'Filtre à air', 'Remplacement filtre à air', 'Moteur', 'medium', 20000, 12, 25.00, 2000, 60, 'wind'),
    (1, 'Plaquettes de frein', 'Remplacement plaquettes avant et arrière', 'Freinage', 'critical', 30000, NULL, 150.00, 3000, NULL, 'brake'),
    (1, 'Courroie distribution', 'Remplacement kit distribution', 'Moteur', 'critical', 100000, 60, 600.00, 5000, 180, 'cog'),
    (1, 'Climatisation', 'Recharge et contrôle climatisation', 'Confort', 'low', NULL, 24, 120.00, NULL, 60, 'snowflake'),
    (1, 'Pneus', 'Contrôle usure et remplacement si nécessaire', 'Sécurité', 'high', 40000, NULL, 400.00, 5000, NULL, 'tire'),
    (1, 'Batterie', 'Contrôle et remplacement batterie', 'Électrique', 'medium', NULL, 36, 150.00, NULL, 90, 'battery')
ON CONFLICT DO NOTHING;

-- ============================================================
-- PARTIE 4: VUE POUR LE KILOMÉTRAGE ACTUEL
-- ============================================================

CREATE OR REPLACE VIEW vehicle_current_mileage AS
SELECT 
    v."Id" AS vehicle_id,
    v."Name" AS vehicle_name,
    v."Plate" AS plate,
    v."CompanyId" AS company_id,
    COALESCE(
        (SELECT gp."OdometerKm" 
         FROM gps_positions gp 
         WHERE gp."DeviceId" = v."GpsDeviceId" 
           AND gp."OdometerKm" IS NOT NULL 
           AND gp."OdometerKm" > 0
         ORDER BY gp."RecordedAt" DESC 
         LIMIT 1),
        v."Mileage"
    ) AS current_km,
    CASE 
        WHEN v."GpsDeviceId" IS NOT NULL AND EXISTS (
            SELECT 1 FROM gps_positions gp 
            WHERE gp."DeviceId" = v."GpsDeviceId" 
              AND gp."OdometerKm" > COALESCE(v."Mileage", 0)
        ) THEN 'gps'
        ELSE 'manual'
    END AS km_source
FROM vehicles v;

-- ============================================================
-- PARTIE 5: VUE DES MAINTENANCES DUES
-- ============================================================

CREATE OR REPLACE VIEW maintenance_due_view AS
SELECT 
    vms."Id" AS schedule_id,
    vms."VehicleId" AS vehicle_id,
    v."Name" AS vehicle_name,
    v."Plate" AS plate,
    v."CompanyId" AS company_id,
    mt."Id" AS template_id,
    mt."Name" AS maintenance_name,
    mt."Category" AS category,
    mt."Priority" AS priority,
    vms."Status" AS status,
    vms."NextDueKm" AS next_due_km,
    vms."NextDueDate" AS next_due_date,
    vms."LastDoneDate" AS last_done_date,
    vms."LastDoneKm" AS last_done_km,
    vcm.current_km,
    CASE WHEN vms."NextDueKm" IS NOT NULL THEN vms."NextDueKm" - vcm.current_km ELSE NULL END AS km_remaining,
    CASE WHEN vms."NextDueDate" IS NOT NULL THEN vms."NextDueDate" - CURRENT_DATE ELSE NULL END AS days_remaining,
    mt."EstimatedCost" AS estimated_cost,
    vms.is_paused
FROM vehicle_maintenance_schedules vms
JOIN maintenance_templates mt ON mt."Id" = vms."TemplateId"
JOIN vehicles v ON v."Id" = vms."VehicleId"
LEFT JOIN vehicle_current_mileage vcm ON vcm.vehicle_id = v."Id"
WHERE mt."IsActive" = TRUE AND vms.is_paused = FALSE;

-- ============================================================
-- PARTIE 6: FONCTION DE MISE À JOUR DES STATUTS
-- ============================================================

CREATE OR REPLACE FUNCTION update_maintenance_schedule_status()
RETURNS INTEGER AS $$
DECLARE
    updated_count INTEGER := 0;
    schedule RECORD;
    current_km INTEGER;
    new_status VARCHAR(20);
    warning_km_threshold INTEGER;
    warning_days_threshold INTEGER;
    critical_km_threshold INTEGER;
    critical_days_threshold INTEGER;
BEGIN
    FOR schedule IN 
        SELECT vms.*, mt.warning_km, mt.warning_days, mt.critical_km, mt.critical_days, vcm.current_km
        FROM vehicle_maintenance_schedules vms
        JOIN maintenance_templates mt ON mt."Id" = vms."TemplateId"
        LEFT JOIN vehicle_current_mileage vcm ON vcm.vehicle_id = vms."VehicleId"
        WHERE vms.is_paused = FALSE AND mt."IsActive" = TRUE
    LOOP
        current_km := COALESCE(schedule.current_km, 0);
        warning_km_threshold := COALESCE(schedule.warning_km, 1000);
        warning_days_threshold := COALESCE(schedule.warning_days, 30);
        critical_km_threshold := COALESCE(schedule.critical_km, 0);
        critical_days_threshold := COALESCE(schedule.critical_days, 0);
        
        -- Déterminer le nouveau statut
        IF (schedule."NextDueKm" IS NOT NULL AND current_km > schedule."NextDueKm") OR
           (schedule."NextDueDate" IS NOT NULL AND CURRENT_DATE > schedule."NextDueDate") THEN
            new_status := 'overdue';
        ELSIF critical_km_threshold > 0 AND schedule."NextDueKm" IS NOT NULL AND 
              (schedule."NextDueKm" - current_km) <= critical_km_threshold THEN
            new_status := 'critical';
        ELSIF critical_days_threshold > 0 AND schedule."NextDueDate" IS NOT NULL AND 
              (schedule."NextDueDate" - CURRENT_DATE) <= critical_days_threshold THEN
            new_status := 'critical';
        ELSIF (schedule."NextDueKm" IS NOT NULL AND (schedule."NextDueKm" - current_km) <= warning_km_threshold) OR
              (schedule."NextDueDate" IS NOT NULL AND (schedule."NextDueDate" - CURRENT_DATE) <= warning_days_threshold) THEN
            new_status := 'due';
        ELSIF (schedule."NextDueKm" IS NOT NULL AND (schedule."NextDueKm" - current_km) <= 5000) OR
              (schedule."NextDueDate" IS NOT NULL AND (schedule."NextDueDate" - CURRENT_DATE) <= 90) THEN
            new_status := 'upcoming';
        ELSE
            new_status := 'ok';
        END IF;
        
        -- Mettre à jour si changement
        IF schedule."Status" != new_status THEN
            UPDATE vehicle_maintenance_schedules 
            SET "Status" = new_status, "UpdatedAt" = NOW()
            WHERE "Id" = schedule."Id";
            updated_count := updated_count + 1;
        END IF;
    END LOOP;
    
    RETURN updated_count;
END;
$$ LANGUAGE plpgsql;

-- ============================================================
-- FIN DE LA MIGRATION
-- ============================================================
