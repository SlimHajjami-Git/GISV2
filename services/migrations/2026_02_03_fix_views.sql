-- ============================================================
-- FIX: Correction des vues avec plate_number au lieu de plate
-- Date: 2026-02-03
-- ============================================================

-- Vue kilométrage actuel
CREATE OR REPLACE VIEW vehicle_current_mileage AS
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

-- Vue maintenances dues
CREATE OR REPLACE VIEW maintenance_due_view AS
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

-- Fonction mise à jour statuts
CREATE OR REPLACE FUNCTION update_maintenance_schedule_status()
RETURNS INTEGER AS $$
DECLARE
    updated_count INTEGER := 0;
    schedule RECORD;
    current_km INTEGER;
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
        current_km := COALESCE(schedule.curr_km, 0);
        warning_km_threshold := COALESCE(schedule.warning_km, 1000);
        warning_days_threshold := COALESCE(schedule.warning_days, 30);
        
        IF (schedule.next_due_km IS NOT NULL AND current_km > schedule.next_due_km) OR
           (schedule.next_due_date IS NOT NULL AND CURRENT_DATE > schedule.next_due_date) THEN
            new_status := 'overdue';
        ELSIF (schedule.next_due_km IS NOT NULL AND (schedule.next_due_km - current_km) <= warning_km_threshold) OR
              (schedule.next_due_date IS NOT NULL AND (schedule.next_due_date - CURRENT_DATE) <= warning_days_threshold) THEN
            new_status := 'due';
        ELSIF (schedule.next_due_km IS NOT NULL AND (schedule.next_due_km - current_km) <= 5000) OR
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
