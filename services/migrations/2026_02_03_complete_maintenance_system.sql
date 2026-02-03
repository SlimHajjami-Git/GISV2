-- ============================================================
-- MIGRATION COMPLÈTE: Système Maintenance + Carburant + Réparations
-- Date: 2026-02-03
-- Compatible avec schema production existant
-- ============================================================

-- ============================================================
-- PARTIE 1: TABLES DE MAINTENANCE
-- ============================================================

-- 1.1 Table des templates de maintenance
CREATE TABLE IF NOT EXISTS maintenance_templates (
    id SERIAL PRIMARY KEY,
    company_id INTEGER NOT NULL REFERENCES societes(id) ON DELETE CASCADE,
    name VARCHAR(100) NOT NULL,
    description VARCHAR(500),
    category VARCHAR(50) NOT NULL,
    priority VARCHAR(20) DEFAULT 'medium',
    interval_km INTEGER,
    interval_months INTEGER,
    estimated_cost DECIMAL(10,2),
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    warning_km INTEGER DEFAULT 1000,
    warning_days INTEGER DEFAULT 30,
    critical_km INTEGER DEFAULT 0,
    critical_days INTEGER DEFAULT 0,
    estimated_duration_minutes INTEGER,
    instructions TEXT,
    applies_to_vehicle_types TEXT[] DEFAULT '{}',
    icon VARCHAR(50) DEFAULT 'wrench',
    CONSTRAINT chk_template_interval CHECK (interval_km > 0 OR interval_months > 0)
);

CREATE INDEX IF NOT EXISTS idx_maintenance_templates_company ON maintenance_templates(company_id);

-- 1.2 Table des schedules véhicule-maintenance
CREATE TABLE IF NOT EXISTS vehicle_maintenance_schedules (
    id SERIAL PRIMARY KEY,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    template_id INTEGER NOT NULL REFERENCES maintenance_templates(id) ON DELETE CASCADE,
    last_done_date DATE,
    last_done_km INTEGER,
    next_due_date DATE,
    next_due_km INTEGER,
    status VARCHAR(20) DEFAULT 'upcoming',
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    is_paused BOOLEAN DEFAULT FALSE,
    paused_at TIMESTAMPTZ,
    paused_reason VARCHAR(255),
    custom_interval_km INTEGER,
    custom_interval_months INTEGER,
    last_notification_at TIMESTAMPTZ,
    notification_count INTEGER DEFAULT 0,
    notes TEXT,
    UNIQUE(vehicle_id, template_id)
);

CREATE INDEX IF NOT EXISTS idx_vms_vehicle ON vehicle_maintenance_schedules(vehicle_id);
CREATE INDEX IF NOT EXISTS idx_vms_template ON vehicle_maintenance_schedules(template_id);
CREATE INDEX IF NOT EXISTS idx_vms_status ON vehicle_maintenance_schedules(status);

-- 1.3 Table des logs de maintenance
CREATE TABLE IF NOT EXISTS maintenance_logs (
    id SERIAL PRIMARY KEY,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    template_id INTEGER NOT NULL REFERENCES maintenance_templates(id) ON DELETE CASCADE,
    schedule_id INTEGER REFERENCES vehicle_maintenance_schedules(id) ON DELETE SET NULL,
    cost_id INTEGER REFERENCES vehicle_costs(id) ON DELETE SET NULL,
    done_date DATE NOT NULL,
    done_km INTEGER NOT NULL,
    actual_cost DECIMAL(10,2) DEFAULT 0,
    supplier_id INTEGER REFERENCES suppliers("Id") ON DELETE SET NULL,
    notes TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    technician_name VARCHAR(100),
    work_order_number VARCHAR(50),
    parts_replaced JSONB DEFAULT '[]',
    labor_hours DECIMAL(5,2),
    labor_cost DECIMAL(10,2),
    parts_cost DECIMAL(10,2),
    quality_rating INTEGER CHECK (quality_rating BETWEEN 1 AND 5),
    photos TEXT[]
);

CREATE INDEX IF NOT EXISTS idx_maint_logs_vehicle ON maintenance_logs(vehicle_id);
CREATE INDEX IF NOT EXISTS idx_maint_logs_template ON maintenance_logs(template_id);

-- 1.4 Table des pièces par template
CREATE TABLE IF NOT EXISTS maintenance_template_parts (
    id SERIAL PRIMARY KEY,
    template_id INTEGER NOT NULL REFERENCES maintenance_templates(id) ON DELETE CASCADE,
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

CREATE INDEX IF NOT EXISTS idx_template_parts_template_id ON maintenance_template_parts(template_id);

-- 1.5 Table des notifications de maintenance
CREATE TABLE IF NOT EXISTS maintenance_notifications (
    id BIGSERIAL PRIMARY KEY,
    schedule_id INTEGER NOT NULL REFERENCES vehicle_maintenance_schedules(id) ON DELETE CASCADE,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    template_id INTEGER NOT NULL REFERENCES maintenance_templates(id) ON DELETE CASCADE,
    company_id INTEGER NOT NULL REFERENCES societes(id) ON DELETE CASCADE,
    notification_type VARCHAR(20) NOT NULL,
    trigger_reason VARCHAR(20) NOT NULL,
    current_km INTEGER,
    km_remaining INTEGER,
    days_remaining INTEGER,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    sent_at TIMESTAMPTZ,
    sent_channels TEXT[] DEFAULT '{}',
    acknowledged_at TIMESTAMPTZ,
    acknowledged_by INTEGER REFERENCES users(id) ON DELETE SET NULL,
    dismissed_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_maint_notif_vehicle ON maintenance_notifications(vehicle_id);
CREATE INDEX IF NOT EXISTS idx_maint_notif_company ON maintenance_notifications(company_id);
CREATE INDEX IF NOT EXISTS idx_maint_notif_type ON maintenance_notifications(notification_type);
CREATE INDEX IF NOT EXISTS idx_maint_notif_schedule ON maintenance_notifications(schedule_id);

-- 1.6 Table des paramètres d'alerte par société
CREATE TABLE IF NOT EXISTS maintenance_alert_settings (
    id SERIAL PRIMARY KEY,
    company_id INTEGER NOT NULL UNIQUE REFERENCES societes(id) ON DELETE CASCADE,
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
-- PARTIE 2: TABLES CARBURANT ET RÉPARATIONS
-- ============================================================

-- 2.1 Table des entrées carburant
CREATE TABLE IF NOT EXISTS fuel_entries (
    id SERIAL PRIMARY KEY,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    company_id INTEGER NOT NULL REFERENCES societes(id) ON DELETE CASCADE,
    entry_date TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    liters DECIMAL(10,2) NOT NULL,
    price_per_liter DECIMAL(10,4) NOT NULL,
    total_cost DECIMAL(10,2) NOT NULL,
    odometer_km INTEGER,
    is_full_tank BOOLEAN DEFAULT TRUE,
    fuel_type VARCHAR(50) DEFAULT 'diesel',
    station_name VARCHAR(200),
    station_address VARCHAR(500),
    payment_method VARCHAR(50),
    receipt_number VARCHAR(100),
    notes TEXT,
    created_by INTEGER REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_fuel_entries_vehicle ON fuel_entries(vehicle_id);
CREATE INDEX IF NOT EXISTS idx_fuel_entries_company ON fuel_entries(company_id);
CREATE INDEX IF NOT EXISTS idx_fuel_entries_date ON fuel_entries(entry_date);

-- 2.2 Table des réparations
CREATE TABLE IF NOT EXISTS repairs (
    id SERIAL PRIMARY KEY,
    vehicle_id INTEGER NOT NULL REFERENCES vehicles(id) ON DELETE CASCADE,
    company_id INTEGER NOT NULL REFERENCES societes(id) ON DELETE CASCADE,
    supplier_id INTEGER REFERENCES suppliers("Id") ON DELETE SET NULL,
    repair_date DATE NOT NULL,
    description TEXT NOT NULL,
    category VARCHAR(100),
    odometer_km INTEGER,
    labor_cost DECIMAL(10,2) DEFAULT 0,
    parts_cost DECIMAL(10,2) DEFAULT 0,
    total_cost DECIMAL(10,2) DEFAULT 0,
    parts_replaced JSONB DEFAULT '[]',
    warranty_until DATE,
    invoice_number VARCHAR(100),
    notes TEXT,
    status VARCHAR(50) DEFAULT 'completed',
    created_by INTEGER REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_repairs_vehicle ON repairs(vehicle_id);
CREATE INDEX IF NOT EXISTS idx_repairs_company ON repairs(company_id);
CREATE INDEX IF NOT EXISTS idx_repairs_date ON repairs(repair_date);

-- ============================================================
-- PARTIE 3: VUE KILOMÉTRAGE ACTUEL
-- ============================================================

DROP VIEW IF EXISTS vehicle_current_mileage CASCADE;

CREATE VIEW vehicle_current_mileage AS
SELECT 
    v.id AS vehicle_id,
    v.name AS vehicle_name,
    v.plate_number AS plate,
    v.company_id AS company_id,
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
        WHEN v.gps_device_id IS NOT NULL AND EXISTS (
            SELECT 1 FROM gps_positions gp 
            WHERE gp.device_id = v.gps_device_id 
              AND gp.odometer_km > COALESCE(v.mileage, 0)
        ) THEN 'gps'
        ELSE 'manual'
    END AS km_source
FROM vehicles v;

-- ============================================================
-- PARTIE 4: VUE MAINTENANCES DUES
-- ============================================================

DROP VIEW IF EXISTS maintenance_due_view CASCADE;

CREATE VIEW maintenance_due_view AS
SELECT 
    vms.id AS schedule_id,
    vms.vehicle_id AS vehicle_id,
    v.name AS vehicle_name,
    v.plate_number AS plate,
    v.company_id AS company_id,
    mt.id AS template_id,
    mt.name AS maintenance_name,
    mt.category AS category,
    mt.priority AS priority,
    vms.status AS status,
    vms.next_due_km AS next_due_km,
    vms.next_due_date AS next_due_date,
    vms.last_done_date AS last_done_date,
    vms.last_done_km AS last_done_km,
    vcm.current_km,
    CASE WHEN vms.next_due_km IS NOT NULL THEN vms.next_due_km - vcm.current_km ELSE NULL END AS km_remaining,
    CASE WHEN vms.next_due_date IS NOT NULL THEN vms.next_due_date - CURRENT_DATE ELSE NULL END AS days_remaining,
    mt.estimated_cost AS estimated_cost,
    vms.is_paused
FROM vehicle_maintenance_schedules vms
JOIN maintenance_templates mt ON mt.id = vms.template_id
JOIN vehicles v ON v.id = vms.vehicle_id
LEFT JOIN vehicle_current_mileage vcm ON vcm.vehicle_id = v.id
WHERE mt.is_active = TRUE AND vms.is_paused = FALSE;

-- ============================================================
-- PARTIE 5: FONCTION MISE À JOUR STATUTS
-- ============================================================

CREATE OR REPLACE FUNCTION update_maintenance_schedule_status()
RETURNS INTEGER AS $$
DECLARE
    updated_count INTEGER := 0;
    schedule RECORD;
    v_current_km INTEGER;
    new_status VARCHAR(20);
    warning_km_threshold INTEGER;
    warning_days_threshold INTEGER;
BEGIN
    FOR schedule IN 
        SELECT vms.*, mt.warning_km, mt.warning_days, vcm.current_km as curr_km
        FROM vehicle_maintenance_schedules vms
        JOIN maintenance_templates mt ON mt.id = vms.template_id
        LEFT JOIN vehicle_current_mileage vcm ON vcm.vehicle_id = vms.vehicle_id
        WHERE vms.is_paused = FALSE AND mt.is_active = TRUE
    LOOP
        v_current_km := COALESCE(schedule.curr_km, 0);
        warning_km_threshold := COALESCE(schedule.warning_km, 1000);
        warning_days_threshold := COALESCE(schedule.warning_days, 30);
        
        IF (schedule.next_due_km IS NOT NULL AND v_current_km > schedule.next_due_km) OR
           (schedule.next_due_date IS NOT NULL AND CURRENT_DATE > schedule.next_due_date) THEN
            new_status := 'overdue';
        ELSIF (schedule.next_due_km IS NOT NULL AND (schedule.next_due_km - v_current_km) <= warning_km_threshold) OR
              (schedule.next_due_date IS NOT NULL AND (schedule.next_due_date - CURRENT_DATE) <= warning_days_threshold) THEN
            new_status := 'due';
        ELSIF (schedule.next_due_km IS NOT NULL AND (schedule.next_due_km - v_current_km) <= 5000) OR
              (schedule.next_due_date IS NOT NULL AND (schedule.next_due_date - CURRENT_DATE) <= 90) THEN
            new_status := 'upcoming';
        ELSE
            new_status := 'ok';
        END IF;
        
        IF schedule.status != new_status THEN
            UPDATE vehicle_maintenance_schedules 
            SET status = new_status, updated_at = NOW()
            WHERE id = schedule.id;
            updated_count := updated_count + 1;
        END IF;
    END LOOP;
    
    RETURN updated_count;
END;
$$ LANGUAGE plpgsql;

-- ============================================================
-- FIN DE LA MIGRATION
-- ============================================================
