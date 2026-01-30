using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// This migration sets up table partitioning for gps_positions.
    /// 
    /// IMPORTANT: The actual partitioning must be done manually using the SQL scripts:
    /// - Scripts/PartitionGpsPositions.sql - Main partitioning setup
    /// - Scripts/SetupPartitionMaintenance.sql - Automatic maintenance
    /// 
    /// Partitioning Strategy:
    /// - Partition by recorded_at (monthly partitions)
    /// - Improves query performance for date-range queries
    /// - Enables efficient data retention (drop old partitions)
    /// - Recommended for tables with 100+ million rows
    /// 
    /// Steps to apply:
    /// 1. Run this migration to create the maintenance functions
    /// 2. During maintenance window, run PartitionGpsPositions.sql manually
    /// 3. Set up pg_cron or external scheduler for automatic partition creation
    /// </summary>
    public partial class AddGpsPositionsPartitioning : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create partition maintenance function
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION create_gps_positions_partition(partition_date DATE)
                RETURNS void AS $$
                DECLARE
                    partition_name TEXT;
                    start_date DATE;
                    end_date DATE;
                BEGIN
                    start_date := date_trunc('month', partition_date);
                    end_date := start_date + INTERVAL '1 month';
                    partition_name := 'gps_positions_' || to_char(start_date, 'YYYY_MM');
                    
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE c.relname = partition_name AND n.nspname = 'public'
                    ) THEN
                        EXECUTE format(
                            'CREATE TABLE IF NOT EXISTS %I PARTITION OF gps_positions
                             FOR VALUES FROM (%L) TO (%L)',
                            partition_name, start_date, end_date
                        );
                        RAISE NOTICE 'Created partition: %', partition_name;
                    END IF;
                EXCEPTION WHEN undefined_table THEN
                    RAISE NOTICE 'Table gps_positions is not partitioned yet. Run PartitionGpsPositions.sql first.';
                END;
                $$ LANGUAGE plpgsql;
            ");

            // Create partition maintenance log table
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS partition_maintenance_log (
                    id SERIAL PRIMARY KEY,
                    table_name TEXT NOT NULL,
                    partition_name TEXT NOT NULL,
                    action TEXT NOT NULL,
                    executed_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                    details TEXT
                );
            ");

            // Create view for partition monitoring
            migrationBuilder.Sql(@"
                CREATE OR REPLACE VIEW v_gps_positions_partitions AS
                SELECT 
                    child.relname AS partition_name,
                    pg_get_expr(child.relpartbound, child.oid) AS partition_range,
                    pg_size_pretty(pg_relation_size(child.oid)) AS size,
                    pg_relation_size(child.oid) AS size_bytes
                FROM pg_inherits
                JOIN pg_class parent ON pg_inherits.inhparent = parent.oid
                JOIN pg_class child ON pg_inherits.inhrelid = child.oid
                WHERE parent.relname = 'gps_positions'
                ORDER BY child.relname;
            ");

            // Create maintenance function
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION maintain_gps_positions_partitions()
                RETURNS void AS $$
                DECLARE
                    future_months INT := 3;
                    current_month DATE;
                    i INT;
                BEGIN
                    FOR i IN 0..future_months LOOP
                        current_month := date_trunc('month', CURRENT_DATE) + (i || ' months')::INTERVAL;
                        PERFORM create_gps_positions_partition(current_month::DATE);
                    END LOOP;
                END;
                $$ LANGUAGE plpgsql;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS maintain_gps_positions_partitions();");
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_gps_positions_partitions;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS partition_maintenance_log;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS create_gps_positions_partition(DATE);");
        }
    }
}


