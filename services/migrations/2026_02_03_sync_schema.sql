-- ============================================================
-- MIGRATION: Synchroniser le schéma DB avec les entités C#
-- Date: 2026-02-03
-- ============================================================

-- 1. Ajouter colonnes manquantes à maintenance_templates (si pas présentes)
ALTER TABLE maintenance_templates ADD COLUMN IF NOT EXISTS warning_km INTEGER DEFAULT 1000;
ALTER TABLE maintenance_templates ADD COLUMN IF NOT EXISTS warning_days INTEGER DEFAULT 30;
ALTER TABLE maintenance_templates ADD COLUMN IF NOT EXISTS critical_km INTEGER DEFAULT 0;
ALTER TABLE maintenance_templates ADD COLUMN IF NOT EXISTS critical_days INTEGER DEFAULT 0;
ALTER TABLE maintenance_templates ADD COLUMN IF NOT EXISTS estimated_duration_minutes INTEGER;
ALTER TABLE maintenance_templates ADD COLUMN IF NOT EXISTS instructions TEXT;
ALTER TABLE maintenance_templates ADD COLUMN IF NOT EXISTS applies_to_vehicle_types TEXT[];
ALTER TABLE maintenance_templates ADD COLUMN IF NOT EXISTS icon VARCHAR(50) DEFAULT 'wrench';

-- 2. Ajouter colonnes manquantes à vehicle_maintenance_schedules
ALTER TABLE vehicle_maintenance_schedules ADD COLUMN IF NOT EXISTS is_paused BOOLEAN DEFAULT FALSE;
ALTER TABLE vehicle_maintenance_schedules ADD COLUMN IF NOT EXISTS paused_at TIMESTAMPTZ;
ALTER TABLE vehicle_maintenance_schedules ADD COLUMN IF NOT EXISTS paused_reason VARCHAR(255);
ALTER TABLE vehicle_maintenance_schedules ADD COLUMN IF NOT EXISTS custom_interval_km INTEGER;
ALTER TABLE vehicle_maintenance_schedules ADD COLUMN IF NOT EXISTS custom_interval_months INTEGER;
ALTER TABLE vehicle_maintenance_schedules ADD COLUMN IF NOT EXISTS last_notification_at TIMESTAMPTZ;
ALTER TABLE vehicle_maintenance_schedules ADD COLUMN IF NOT EXISTS notification_count INTEGER DEFAULT 0;
ALTER TABLE vehicle_maintenance_schedules ADD COLUMN IF NOT EXISTS notes TEXT;

-- 3. Ajouter colonnes manquantes à maintenance_logs
ALTER TABLE maintenance_logs ADD COLUMN IF NOT EXISTS technician_name VARCHAR(100);
ALTER TABLE maintenance_logs ADD COLUMN IF NOT EXISTS work_order_number VARCHAR(50);
ALTER TABLE maintenance_logs ADD COLUMN IF NOT EXISTS parts_replaced JSONB;
ALTER TABLE maintenance_logs ADD COLUMN IF NOT EXISTS labor_hours NUMERIC(5,2);
ALTER TABLE maintenance_logs ADD COLUMN IF NOT EXISTS labor_cost NUMERIC(10,2);
ALTER TABLE maintenance_logs ADD COLUMN IF NOT EXISTS parts_cost NUMERIC(10,2);
ALTER TABLE maintenance_logs ADD COLUMN IF NOT EXISTS quality_rating INTEGER;
ALTER TABLE maintenance_logs ADD COLUMN IF NOT EXISTS photos TEXT[];

-- 4. S'assurer que maintenance_notifications existe
CREATE TABLE IF NOT EXISTS maintenance_notifications (
    id SERIAL PRIMARY KEY,
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
    sent_channels TEXT[],
    acknowledged_at TIMESTAMPTZ,
    acknowledged_by INTEGER REFERENCES users(id) ON DELETE SET NULL,
    dismissed_at TIMESTAMPTZ
);

-- 5. S'assurer que maintenance_template_parts existe
CREATE TABLE IF NOT EXISTS maintenance_template_parts (
    id SERIAL PRIMARY KEY,
    template_id INTEGER NOT NULL REFERENCES maintenance_templates(id) ON DELETE CASCADE,
    part_name VARCHAR(200) NOT NULL,
    part_number VARCHAR(100),
    quantity INTEGER DEFAULT 1,
    unit VARCHAR(20) DEFAULT 'unit',
    estimated_unit_cost NUMERIC(10,2),
    is_required BOOLEAN DEFAULT TRUE,
    preferred_supplier_id INTEGER REFERENCES suppliers("Id") ON DELETE SET NULL,
    notes TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- 6. S'assurer que maintenance_alert_settings existe
CREATE TABLE IF NOT EXISTS maintenance_alert_settings (
    id SERIAL PRIMARY KEY,
    company_id INTEGER NOT NULL REFERENCES societes(id) ON DELETE CASCADE,
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
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    CONSTRAINT ix_maint_alert_settings_company UNIQUE (company_id)
);

-- 7. Créer la vue vehicle_current_mileage si elle n'existe pas
CREATE OR REPLACE VIEW vehicle_current_mileage AS
SELECT DISTINCT ON (v.id)
    v.id as vehicle_id,
    v.plate_number,
    COALESCE(gp.odometer_km, v.mileage, 0) as current_km,
    gp.recorded_at as last_update
FROM vehicles v
LEFT JOIN gps_devices gd ON v.gps_device_id = gd.id
LEFT JOIN gps_positions gp ON gd.id = gp.device_id AND gp.odometer_km IS NOT NULL AND gp.odometer_km > 0
ORDER BY v.id, gp.recorded_at DESC NULLS LAST;

-- 8. Créer les index manquants
CREATE INDEX IF NOT EXISTS idx_maint_notif_schedule ON maintenance_notifications(schedule_id);
CREATE INDEX IF NOT EXISTS idx_maint_notif_vehicle ON maintenance_notifications(vehicle_id);
CREATE INDEX IF NOT EXISTS idx_maint_notif_company ON maintenance_notifications(company_id);
CREATE INDEX IF NOT EXISTS idx_maint_notif_type ON maintenance_notifications(notification_type);
CREATE INDEX IF NOT EXISTS idx_template_parts_template_id ON maintenance_template_parts(template_id);

-- 9. Vérification finale
DO $$
BEGIN
    RAISE NOTICE 'Migration completed successfully!';
    RAISE NOTICE 'Tables synchronized with C# entities.';
END $$;
