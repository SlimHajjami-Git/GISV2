-- ============================================================
-- MIGRATION: Système d'Entretien Programmable Amélioré
-- Date: 2026-01-31
-- Description: Ajout des fonctionnalités avancées de maintenance
-- ============================================================

BEGIN;

-- ============================================================
-- PARTIE 1: AMÉLIORATIONS DES TABLES EXISTANTES
-- ============================================================

-- 1.1 Ajout de colonnes à maintenance_templates
ALTER TABLE maintenance_templates 
    ADD COLUMN IF NOT EXISTS warning_km INTEGER DEFAULT 1000,
    ADD COLUMN IF NOT EXISTS warning_days INTEGER DEFAULT 30,
    ADD COLUMN IF NOT EXISTS critical_km INTEGER DEFAULT 0,
    ADD COLUMN IF NOT EXISTS critical_days INTEGER DEFAULT 0,
    ADD COLUMN IF NOT EXISTS estimated_duration_minutes INTEGER,
    ADD COLUMN IF NOT EXISTS instructions TEXT,
    ADD COLUMN IF NOT EXISTS applies_to_vehicle_types TEXT[] DEFAULT '{}',
    ADD COLUMN IF NOT EXISTS icon VARCHAR(50) DEFAULT 'wrench';

COMMENT ON COLUMN maintenance_templates.warning_km IS 'Seuil km avant échéance pour alerte warning';
COMMENT ON COLUMN maintenance_templates.warning_days IS 'Seuil jours avant échéance pour alerte warning';
COMMENT ON COLUMN maintenance_templates.critical_km IS 'Seuil km pour alerte critique (0 = désactivé)';
COMMENT ON COLUMN maintenance_templates.critical_days IS 'Seuil jours pour alerte critique (0 = désactivé)';
COMMENT ON COLUMN maintenance_templates.applies_to_vehicle_types IS 'Types de véhicules concernés (vide = tous)';

-- 1.2 Ajout de colonnes à vehicle_maintenance_schedules
ALTER TABLE vehicle_maintenance_schedules
    ADD COLUMN IF NOT EXISTS is_paused BOOLEAN DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS paused_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS paused_reason VARCHAR(255),
    ADD COLUMN IF NOT EXISTS custom_interval_km INTEGER,
    ADD COLUMN IF NOT EXISTS custom_interval_months INTEGER,
    ADD COLUMN IF NOT EXISTS last_notification_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS notification_count INTEGER DEFAULT 0,
    ADD COLUMN IF NOT EXISTS notes TEXT;

COMMENT ON COLUMN vehicle_maintenance_schedules.is_paused IS 'Programme en pause (ex: véhicule en réparation)';
COMMENT ON COLUMN vehicle_maintenance_schedules.custom_interval_km IS 'Override de l intervalle km du template';
COMMENT ON COLUMN vehicle_maintenance_schedules.custom_interval_months IS 'Override de l intervalle mois du template';

-- 1.3 Ajout de colonnes à maintenance_logs
ALTER TABLE maintenance_logs
    ADD COLUMN IF NOT EXISTS technician_name VARCHAR(100),
    ADD COLUMN IF NOT EXISTS work_order_number VARCHAR(50),
    ADD COLUMN IF NOT EXISTS parts_replaced JSONB DEFAULT '[]',
    ADD COLUMN IF NOT EXISTS labor_hours DECIMAL(5,2),
    ADD COLUMN IF NOT EXISTS labor_cost DECIMAL(10,2),
    ADD COLUMN IF NOT EXISTS parts_cost DECIMAL(10,2),
    ADD COLUMN IF NOT EXISTS quality_rating INTEGER CHECK (quality_rating BETWEEN 1 AND 5),
    ADD COLUMN IF NOT EXISTS photos TEXT[];

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
    preferred_supplier_id INTEGER REFERENCES suppliers("Id") ON DELETE SET NULL,
    notes TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX idx_template_parts_template_id ON maintenance_template_parts(template_id);

COMMENT ON TABLE maintenance_template_parts IS 'Pièces requises pour chaque type d entretien';

-- 2.2 Table des notifications de maintenance
CREATE TABLE IF NOT EXISTS maintenance_notifications (
    id BIGSERIAL PRIMARY KEY,
    schedule_id INTEGER NOT NULL REFERENCES vehicle_maintenance_schedules("Id") ON DELETE CASCADE,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    template_id INTEGER NOT NULL REFERENCES maintenance_templates("Id") ON DELETE CASCADE,
    company_id INTEGER NOT NULL,
    
    notification_type VARCHAR(20) NOT NULL CHECK (notification_type IN ('upcoming', 'due', 'overdue', 'critical')),
    trigger_reason VARCHAR(20) NOT NULL CHECK (trigger_reason IN ('km', 'date', 'both')),
    
    -- Valeurs au moment de la notification
    current_km INTEGER,
    km_remaining INTEGER,
    days_remaining INTEGER,
    
    -- Statut
    created_at TIMESTAMPTZ DEFAULT NOW(),
    sent_at TIMESTAMPTZ,
    sent_channels TEXT[] DEFAULT '{}',
    acknowledged_at TIMESTAMPTZ,
    acknowledged_by INTEGER REFERENCES users(id),
    dismissed_at TIMESTAMPTZ,
    
    -- Éviter les doublons
    UNIQUE(schedule_id, notification_type, DATE(created_at))
);

CREATE INDEX idx_maint_notif_vehicle ON maintenance_notifications(vehicle_id);
CREATE INDEX idx_maint_notif_company ON maintenance_notifications(company_id);
CREATE INDEX idx_maint_notif_pending ON maintenance_notifications(sent_at) WHERE sent_at IS NULL;
CREATE INDEX idx_maint_notif_type ON maintenance_notifications(notification_type);

COMMENT ON TABLE maintenance_notifications IS 'Historique des notifications de maintenance envoyées';

-- 2.3 Table de configuration des alertes par société
CREATE TABLE IF NOT EXISTS maintenance_alert_settings (
    id SERIAL PRIMARY KEY,
    company_id INTEGER NOT NULL UNIQUE,
    
    -- Canaux de notification
    enable_push BOOLEAN DEFAULT TRUE,
    enable_email BOOLEAN DEFAULT TRUE,
    enable_sms BOOLEAN DEFAULT FALSE,
    
    -- Destinataires
    notify_driver BOOLEAN DEFAULT TRUE,
    notify_supervisor BOOLEAN DEFAULT TRUE,
    notify_fleet_manager BOOLEAN DEFAULT TRUE,
    additional_emails TEXT[],
    additional_phones TEXT[],
    
    -- Fréquence
    reminder_frequency_days INTEGER DEFAULT 7,
    max_reminders INTEGER DEFAULT 3,
    
    -- Heures calmes
    quiet_hours_start TIME,
    quiet_hours_end TIME,
    
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

COMMENT ON TABLE maintenance_alert_settings IS 'Configuration des alertes de maintenance par société';

-- 2.4 Vue matérialisée pour le kilométrage actuel des véhicules
CREATE MATERIALIZED VIEW IF NOT EXISTS vehicle_current_mileage AS
SELECT DISTINCT ON (v.id)
    v.id AS vehicle_id,
    v.company_id,
    v.name AS vehicle_name,
    v.plate,
    COALESCE(
        (SELECT gp.odometer_km 
         FROM gps_positions gp 
         WHERE gp.device_id = v.gps_device_id 
           AND gp.odometer_km IS NOT NULL 
           AND gp.odometer_km > 0
         ORDER BY gp.recorded_at DESC 
         LIMIT 1),
        v.mileage
    ) AS current_km,
    CASE 
        WHEN v.gps_device_id IS NOT NULL 
             AND EXISTS (
                 SELECT 1 FROM gps_positions gp 
                 WHERE gp.device_id = v.gps_device_id 
                   AND gp.odometer_km IS NOT NULL 
                   AND gp.odometer_km > 0
             )
        THEN 'gps'
        ELSE 'manual'
    END AS km_source,
    (SELECT MAX(gp.recorded_at) 
     FROM gps_positions gp 
     WHERE gp.device_id = v.gps_device_id) AS last_gps_update
FROM vehicles v;

CREATE UNIQUE INDEX idx_vcm_vehicle_id ON vehicle_current_mileage(vehicle_id);
CREATE INDEX idx_vcm_company_id ON vehicle_current_mileage(company_id);

COMMENT ON MATERIALIZED VIEW vehicle_current_mileage IS 'Kilométrage actuel de chaque véhicule (GPS ou manuel)';

-- 2.5 Vue pour les entretiens à venir
CREATE OR REPLACE VIEW maintenance_due_view AS
SELECT 
    vms.id AS schedule_id,
    vms."VehicleId" AS vehicle_id,
    vms."TemplateId" AS template_id,
    mt."CompanyId" AS company_id,
    
    v.name AS vehicle_name,
    v.plate AS vehicle_plate,
    mt."Name" AS maintenance_name,
    mt."Category" AS category,
    mt."Priority" AS priority,
    
    vms."LastDoneDate" AS last_done_date,
    vms."LastDoneKm" AS last_done_km,
    vms."NextDueDate" AS next_due_date,
    vms."NextDueKm" AS next_due_km,
    
    vcm.current_km,
    vcm.km_source,
    
    -- Calculs
    CASE 
        WHEN vms."NextDueKm" IS NOT NULL AND vcm.current_km IS NOT NULL 
        THEN vms."NextDueKm" - vcm.current_km
        ELSE NULL 
    END AS km_remaining,
    
    CASE 
        WHEN vms."NextDueDate" IS NOT NULL 
        THEN (vms."NextDueDate"::date - CURRENT_DATE)
        ELSE NULL 
    END AS days_remaining,
    
    -- Statut calculé
    CASE
        -- Overdue
        WHEN (vms."NextDueKm" IS NOT NULL AND vcm.current_km > vms."NextDueKm")
             OR (vms."NextDueDate" IS NOT NULL AND CURRENT_DATE > vms."NextDueDate"::date)
        THEN 'overdue'
        
        -- Critical (seuil critique)
        WHEN (mt.critical_km > 0 AND vms."NextDueKm" IS NOT NULL 
              AND vms."NextDueKm" - vcm.current_km <= mt.critical_km)
             OR (mt.critical_days > 0 AND vms."NextDueDate" IS NOT NULL 
                 AND (vms."NextDueDate"::date - CURRENT_DATE) <= mt.critical_days)
        THEN 'critical'
        
        -- Due (seuil warning)
        WHEN (vms."NextDueKm" IS NOT NULL 
              AND vms."NextDueKm" - vcm.current_km <= COALESCE(mt.warning_km, 1000))
             OR (vms."NextDueDate" IS NOT NULL 
                 AND (vms."NextDueDate"::date - CURRENT_DATE) <= COALESCE(mt.warning_days, 30))
        THEN 'due'
        
        -- Upcoming (< 5000 km ou < 90 jours)
        WHEN (vms."NextDueKm" IS NOT NULL 
              AND vms."NextDueKm" - vcm.current_km <= 5000)
             OR (vms."NextDueDate" IS NOT NULL 
                 AND (vms."NextDueDate"::date - CURRENT_DATE) <= 90)
        THEN 'upcoming'
        
        ELSE 'ok'
    END AS calculated_status,
    
    vms."Status" AS stored_status,
    vms.is_paused
    
FROM vehicle_maintenance_schedules vms
JOIN maintenance_templates mt ON mt."Id" = vms."TemplateId"
JOIN vehicles v ON v.id = vms."VehicleId"
LEFT JOIN vehicle_current_mileage vcm ON vcm.vehicle_id = v.id
WHERE mt."IsActive" = true
  AND COALESCE(vms.is_paused, false) = false;

COMMENT ON VIEW maintenance_due_view IS 'Vue temps réel des statuts d entretien avec kilométrage GPS';

-- ============================================================
-- PARTIE 3: FONCTIONS ET TRIGGERS
-- ============================================================

-- 3.1 Fonction pour rafraîchir le kilométrage
CREATE OR REPLACE FUNCTION refresh_vehicle_mileage()
RETURNS void AS $$
BEGIN
    REFRESH MATERIALIZED VIEW CONCURRENTLY vehicle_current_mileage;
END;
$$ LANGUAGE plpgsql;

-- 3.2 Fonction pour mettre à jour le statut des schedules
CREATE OR REPLACE FUNCTION update_maintenance_schedule_status(p_vehicle_id INTEGER DEFAULT NULL)
RETURNS INTEGER AS $$
DECLARE
    updated_count INTEGER := 0;
    rec RECORD;
BEGIN
    FOR rec IN 
        SELECT 
            mdv.schedule_id,
            mdv.calculated_status
        FROM maintenance_due_view mdv
        WHERE (p_vehicle_id IS NULL OR mdv.vehicle_id = p_vehicle_id)
          AND mdv.calculated_status != mdv.stored_status
    LOOP
        UPDATE vehicle_maintenance_schedules 
        SET "Status" = rec.calculated_status,
            "UpdatedAt" = NOW()
        WHERE "Id" = rec.schedule_id;
        
        updated_count := updated_count + 1;
    END LOOP;
    
    RETURN updated_count;
END;
$$ LANGUAGE plpgsql;

-- 3.3 Fonction pour générer les notifications de maintenance
CREATE OR REPLACE FUNCTION generate_maintenance_notifications()
RETURNS TABLE(
    schedule_id INTEGER,
    vehicle_id INTEGER,
    notification_type VARCHAR,
    trigger_reason VARCHAR
) AS $$
BEGIN
    RETURN QUERY
    INSERT INTO maintenance_notifications (
        schedule_id, vehicle_id, template_id, company_id,
        notification_type, trigger_reason,
        current_km, km_remaining, days_remaining
    )
    SELECT 
        mdv.schedule_id,
        mdv.vehicle_id,
        mdv.template_id,
        mdv.company_id,
        mdv.calculated_status,
        CASE 
            WHEN mdv.km_remaining IS NOT NULL AND mdv.days_remaining IS NOT NULL THEN 'both'
            WHEN mdv.km_remaining IS NOT NULL THEN 'km'
            ELSE 'date'
        END,
        mdv.current_km,
        mdv.km_remaining,
        mdv.days_remaining
    FROM maintenance_due_view mdv
    WHERE mdv.calculated_status IN ('due', 'overdue', 'critical')
      AND NOT EXISTS (
          SELECT 1 FROM maintenance_notifications mn
          WHERE mn.schedule_id = mdv.schedule_id
            AND mn.notification_type = mdv.calculated_status
            AND DATE(mn.created_at) = CURRENT_DATE
      )
    RETURNING 
        maintenance_notifications.schedule_id,
        maintenance_notifications.vehicle_id,
        maintenance_notifications.notification_type,
        maintenance_notifications.trigger_reason;
END;
$$ LANGUAGE plpgsql;

-- 3.4 Fonction pour calculer la prochaine échéance après réalisation
CREATE OR REPLACE FUNCTION calculate_next_maintenance(
    p_schedule_id INTEGER,
    p_done_date DATE,
    p_done_km INTEGER
) RETURNS TABLE(next_due_date DATE, next_due_km INTEGER) AS $$
DECLARE
    v_interval_km INTEGER;
    v_interval_months INTEGER;
BEGIN
    SELECT 
        COALESCE(vms.custom_interval_km, mt."IntervalKm"),
        COALESCE(vms.custom_interval_months, mt."IntervalMonths")
    INTO v_interval_km, v_interval_months
    FROM vehicle_maintenance_schedules vms
    JOIN maintenance_templates mt ON mt."Id" = vms."TemplateId"
    WHERE vms."Id" = p_schedule_id;
    
    RETURN QUERY SELECT 
        CASE WHEN v_interval_months IS NOT NULL 
             THEN (p_done_date + (v_interval_months || ' months')::INTERVAL)::DATE 
             ELSE NULL 
        END,
        CASE WHEN v_interval_km IS NOT NULL 
             THEN p_done_km + v_interval_km 
             ELSE NULL 
        END;
END;
$$ LANGUAGE plpgsql;

-- ============================================================
-- PARTIE 4: DONNÉES INITIALES
-- ============================================================

-- 4.1 Configuration par défaut des alertes (pour les sociétés existantes)
INSERT INTO maintenance_alert_settings (company_id)
SELECT id FROM societes
WHERE NOT EXISTS (
    SELECT 1 FROM maintenance_alert_settings mas 
    WHERE mas.company_id = societes.id
)
ON CONFLICT (company_id) DO NOTHING;

-- 4.2 Templates système par défaut (si pas déjà créés)
DO $$
BEGIN
    -- Vidange moteur
    INSERT INTO maintenance_templates ("CompanyId", "Name", "Description", "Category", "Priority", "IntervalKm", "IntervalMonths", "EstimatedCost", warning_km, warning_days, icon)
    SELECT 0, 'Vidange moteur', 'Changement huile moteur et filtre à huile', 'Moteur', 'high', 10000, 6, 80.00, 1000, 30, 'oil-can'
    WHERE NOT EXISTS (SELECT 1 FROM maintenance_templates WHERE "Name" = 'Vidange moteur' AND "CompanyId" = 0);
    
    -- Filtre à air
    INSERT INTO maintenance_templates ("CompanyId", "Name", "Description", "Category", "Priority", "IntervalKm", "IntervalMonths", "EstimatedCost", warning_km, warning_days, icon)
    SELECT 0, 'Remplacement filtre à air', 'Changement du filtre à air moteur', 'Filtres', 'medium', 20000, 12, 35.00, 2000, 30, 'wind'
    WHERE NOT EXISTS (SELECT 1 FROM maintenance_templates WHERE "Name" = 'Remplacement filtre à air' AND "CompanyId" = 0);
    
    -- Plaquettes de frein
    INSERT INTO maintenance_templates ("CompanyId", "Name", "Description", "Category", "Priority", "IntervalKm", "IntervalMonths", "EstimatedCost", warning_km, warning_days, icon)
    SELECT 0, 'Plaquettes de frein', 'Vérification et remplacement plaquettes avant', 'Freinage', 'critical', 30000, NULL, 150.00, 3000, NULL, 'brake-warning'
    WHERE NOT EXISTS (SELECT 1 FROM maintenance_templates WHERE "Name" = 'Plaquettes de frein' AND "CompanyId" = 0);
    
    -- Courroie distribution
    INSERT INTO maintenance_templates ("CompanyId", "Name", "Description", "Category", "Priority", "IntervalKm", "IntervalMonths", "EstimatedCost", warning_km, warning_days, icon)
    SELECT 0, 'Courroie de distribution', 'Remplacement courroie et galets', 'Moteur', 'critical', 100000, 60, 600.00, 5000, 60, 'cog'
    WHERE NOT EXISTS (SELECT 1 FROM maintenance_templates WHERE "Name" = 'Courroie de distribution' AND "CompanyId" = 0);
    
    -- Pneus
    INSERT INTO maintenance_templates ("CompanyId", "Name", "Description", "Category", "Priority", "IntervalKm", "IntervalMonths", "EstimatedCost", warning_km, warning_days, icon)
    SELECT 0, 'Rotation/Remplacement pneus', 'Contrôle usure et rotation pneus', 'Pneumatiques', 'medium', 15000, NULL, 400.00, 2000, NULL, 'tire'
    WHERE NOT EXISTS (SELECT 1 FROM maintenance_templates WHERE "Name" = 'Rotation/Remplacement pneus' AND "CompanyId" = 0);
    
    -- Climatisation
    INSERT INTO maintenance_templates ("CompanyId", "Name", "Description", "Category", "Priority", "IntervalKm", "IntervalMonths", "EstimatedCost", warning_km, warning_days, icon)
    SELECT 0, 'Recharge climatisation', 'Contrôle et recharge gaz climatisation', 'Climatisation', 'low', NULL, 24, 120.00, NULL, 60, 'snowflake'
    WHERE NOT EXISTS (SELECT 1 FROM maintenance_templates WHERE "Name" = 'Recharge climatisation' AND "CompanyId" = 0);
END $$;

-- ============================================================
-- PARTIE 5: INDEX DE PERFORMANCE
-- ============================================================

CREATE INDEX IF NOT EXISTS idx_vms_status ON vehicle_maintenance_schedules("Status");
CREATE INDEX IF NOT EXISTS idx_vms_next_due_km ON vehicle_maintenance_schedules("NextDueKm") WHERE "NextDueKm" IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_vms_next_due_date ON vehicle_maintenance_schedules("NextDueDate") WHERE "NextDueDate" IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_vms_not_paused ON vehicle_maintenance_schedules("VehicleId") WHERE is_paused = false;

CREATE INDEX IF NOT EXISTS idx_gps_pos_odo ON gps_positions(device_id, recorded_at DESC) 
    WHERE odometer_km IS NOT NULL AND odometer_km > 0;

-- ============================================================
-- RAFRAÎCHISSEMENT INITIAL
-- ============================================================

SELECT refresh_vehicle_mileage();
SELECT update_maintenance_schedule_status();

COMMIT;

-- ============================================================
-- NOTE: Planifier le job de rafraîchissement périodique
-- ============================================================
-- Exécuter toutes les heures via cron ou pg_cron:
-- SELECT cron.schedule('refresh-mileage', '0 * * * *', 'SELECT refresh_vehicle_mileage()');
-- SELECT cron.schedule('update-maintenance-status', '*/15 * * * *', 'SELECT update_maintenance_schedule_status()');
-- SELECT cron.schedule('generate-maintenance-alerts', '0 8 * * *', 'SELECT generate_maintenance_notifications()');
