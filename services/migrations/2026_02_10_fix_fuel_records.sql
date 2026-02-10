-- 1. Delete duplicate fuel_records (keep only the first inserted per vehicle_id, recorded_at, fuel_percent)
DELETE FROM fuel_records
WHERE id NOT IN (
    SELECT MIN(id)
    FROM fuel_records
    GROUP BY vehicle_id, recorded_at, fuel_percent
);

-- 2. Create unique index for ON CONFLICT dedup in Rust ingest
CREATE UNIQUE INDEX IF NOT EXISTS ix_fuel_records_dedup
ON fuel_records (vehicle_id, recorded_at, fuel_percent);

-- 3. Fix fake refuel events: records where fuel_change > 0 but it was just
--    the first reading after a restart (comparing to 0% baseline)
--    Mark them as 'reading' instead of 'refuel' if previous record has same fuel_percent
UPDATE fuel_records fr
SET event_type = 'reading', fuel_change = 0
WHERE fr.event_type = 'refuel'
  AND fr.fuel_change = fr.fuel_percent  -- fuel_change equals current level = compared to 0 baseline
  AND EXISTS (
      SELECT 1 FROM fuel_records prev
      WHERE prev.vehicle_id = fr.vehicle_id
        AND prev.recorded_at < fr.recorded_at
        AND prev.fuel_percent = fr.fuel_percent
        AND prev.id != fr.id
  );

-- 4. Delete garbage fuel_records from S-type devices (fuel_percent always 100, no real data)
-- These come from gps_type_2 devices that send constant fuel_raw=100
DELETE FROM fuel_records
WHERE vehicle_id IN (
    SELECT v.id FROM vehicles v
    JOIN gps_devices gd ON v.gps_device_id = gd.id
    WHERE gd.firmware_version = 'S'
)
AND id NOT IN (
    -- Keep at least some records in case they were manually entered
    SELECT MIN(id) FROM fuel_records GROUP BY vehicle_id, recorded_at, fuel_percent
);

-- 5. Show cleanup results
SELECT 'fuel_records count after cleanup' as info, count(*) as total FROM fuel_records;
SELECT event_type, count(*) FROM fuel_records GROUP BY event_type ORDER BY count DESC;
