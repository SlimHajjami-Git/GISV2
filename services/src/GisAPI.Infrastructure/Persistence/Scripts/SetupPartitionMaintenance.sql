-- ============================================================================
-- Automatic Partition Maintenance for GPS Positions
-- ============================================================================
-- This script sets up automatic partition creation and cleanup
-- Run this after the main partitioning script
-- ============================================================================

-- Create a table to track partition maintenance
CREATE TABLE IF NOT EXISTS partition_maintenance_log (
    id SERIAL PRIMARY KEY,
    table_name TEXT NOT NULL,
    partition_name TEXT NOT NULL,
    action TEXT NOT NULL,  -- 'created', 'dropped', 'error'
    executed_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    details TEXT
);

-- Function to create future partitions
CREATE OR REPLACE FUNCTION maintain_gps_positions_partitions()
RETURNS void AS $$
DECLARE
    future_months INT := 3;  -- Create partitions 3 months ahead
    retention_months INT := 24;  -- Keep 24 months of data
    current_month DATE;
    partition_name TEXT;
    start_date DATE;
    end_date DATE;
    i INT;
BEGIN
    -- Create future partitions
    FOR i IN 0..future_months LOOP
        current_month := date_trunc('month', CURRENT_DATE) + (i || ' months')::INTERVAL;
        start_date := current_month;
        end_date := current_month + INTERVAL '1 month';
        partition_name := 'gps_positions_' || to_char(start_date, 'YYYY_MM');
        
        -- Check if partition exists
        IF NOT EXISTS (
            SELECT 1 FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relname = partition_name AND n.nspname = 'public'
        ) THEN
            BEGIN
                EXECUTE format(
                    'CREATE TABLE %I PARTITION OF gps_positions
                     FOR VALUES FROM (%L) TO (%L)',
                    partition_name, start_date, end_date
                );
                
                INSERT INTO partition_maintenance_log (table_name, partition_name, action, details)
                VALUES ('gps_positions', partition_name, 'created', 
                        format('Range: %s to %s', start_date, end_date));
                        
                RAISE NOTICE 'Created partition: %', partition_name;
            EXCEPTION WHEN OTHERS THEN
                INSERT INTO partition_maintenance_log (table_name, partition_name, action, details)
                VALUES ('gps_positions', partition_name, 'error', SQLERRM);
            END;
        END IF;
    END LOOP;
    
    -- Optional: Drop old partitions (uncomment to enable)
    /*
    FOR partition_name IN
        SELECT child.relname
        FROM pg_inherits
        JOIN pg_class parent ON pg_inherits.inhparent = parent.oid
        JOIN pg_class child ON pg_inherits.inhrelid = child.oid
        WHERE parent.relname = 'gps_positions'
        AND child.relname < 'gps_positions_' || to_char(
            CURRENT_DATE - (retention_months || ' months')::INTERVAL, 'YYYY_MM'
        )
    LOOP
        EXECUTE format('DROP TABLE %I', partition_name);
        
        INSERT INTO partition_maintenance_log (table_name, partition_name, action)
        VALUES ('gps_positions', partition_name, 'dropped');
        
        RAISE NOTICE 'Dropped old partition: %', partition_name;
    END LOOP;
    */
END;
$$ LANGUAGE plpgsql;

-- Create a view to monitor partition status
CREATE OR REPLACE VIEW v_gps_positions_partitions AS
SELECT 
    child.relname AS partition_name,
    pg_get_expr(child.relpartbound, child.oid) AS partition_range,
    pg_size_pretty(pg_relation_size(child.oid)) AS size,
    pg_relation_size(child.oid) AS size_bytes,
    (SELECT count(*) FROM pg_stat_user_tables WHERE relname = child.relname) AS stats_available
FROM pg_inherits
JOIN pg_class parent ON pg_inherits.inhparent = parent.oid
JOIN pg_class child ON pg_inherits.inhrelid = child.oid
WHERE parent.relname = 'gps_positions'
ORDER BY child.relname;

-- Create a function to get partition statistics
CREATE OR REPLACE FUNCTION get_gps_positions_stats()
RETURNS TABLE (
    total_partitions INT,
    total_size TEXT,
    oldest_partition TEXT,
    newest_partition TEXT,
    estimated_rows BIGINT
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        COUNT(*)::INT AS total_partitions,
        pg_size_pretty(SUM(pg_relation_size(child.oid))) AS total_size,
        MIN(child.relname) AS oldest_partition,
        MAX(child.relname) AS newest_partition,
        (SELECT reltuples::BIGINT FROM pg_class WHERE relname = 'gps_positions') AS estimated_rows
    FROM pg_inherits
    JOIN pg_class parent ON pg_inherits.inhparent = parent.oid
    JOIN pg_class child ON pg_inherits.inhrelid = child.oid
    WHERE parent.relname = 'gps_positions';
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Usage Examples
-- ============================================================================

-- Run maintenance manually:
-- SELECT maintain_gps_positions_partitions();

-- View partition status:
-- SELECT * FROM v_gps_positions_partitions;

-- Get overall statistics:
-- SELECT * FROM get_gps_positions_stats();

-- View maintenance log:
-- SELECT * FROM partition_maintenance_log ORDER BY executed_at DESC LIMIT 20;

-- ============================================================================
-- Scheduling with pg_cron (if available)
-- ============================================================================
-- 
-- Install pg_cron extension:
-- CREATE EXTENSION IF NOT EXISTS pg_cron;
-- 
-- Schedule daily maintenance at 3 AM:
-- SELECT cron.schedule('maintain-gps-partitions', '0 3 * * *', 
--     'SELECT maintain_gps_positions_partitions()');
-- 
-- View scheduled jobs:
-- SELECT * FROM cron.job;
-- ============================================================================
