//! Daily Statistics Calculator Service
//!
//! Calculates daily statistics for each vehicle by aggregating data from:
//! - gps_positions (distance, speed, driving time)
//! - trips (trip count)
//! - driving_events (harsh braking, acceleration, sharp turns)
//! - geofence_events (geofence events count)
//! - gps_alerts (alert count)
//!
//! NOTE: This service should be run as a scheduled job (CRON), not in real-time.
//! It aggregates data from the previous day or a specified date range.

use anyhow::Result;
use chrono::{NaiveDate, Utc};
use sqlx::{postgres::PgPool, Row};
use tracing::{info, warn};

/// Daily statistics for a vehicle
#[derive(Debug, Clone)]
pub struct DailyStatisticsRecord {
    pub vehicle_id: i32,
    pub company_id: i32,
    pub date: NaiveDate,
    pub distance_km: f64,
    pub driving_time_minutes: i32,
    pub idle_time_minutes: i32,
    pub stopped_time_minutes: i32,
    pub trip_count: i32,
    pub average_speed_kph: Option<f64>,
    pub max_speed_kph: Option<f64>,
    pub overspeeding_events: i32,
    pub overspeeding_minutes: i32,
    pub fuel_consumed_liters: Option<f64>,
    pub fuel_efficiency_km_per_liter: Option<f64>,
    pub harsh_braking_count: i32,
    pub harsh_acceleration_count: i32,
    pub sharp_turn_count: i32,
    pub start_mileage: i32,
    pub end_mileage: i32,
    pub engine_on_time_minutes: Option<i32>,
    pub engine_off_time_minutes: Option<i32>,
    pub alert_count: i32,
    pub geofence_events_count: i32,
    pub driver_score: Option<i32>,
}

/// Service for calculating daily statistics
pub struct DailyStatisticsCalculator {
    pool: PgPool,
}

impl DailyStatisticsCalculator {
    pub fn new(pool: PgPool) -> Self {
        Self { pool }
    }

    /// Calculate daily statistics for all vehicles for a specific date
    pub async fn calculate_for_date(&self, date: NaiveDate) -> Result<Vec<DailyStatisticsRecord>> {
        info!(%date, "Calculating daily statistics for all vehicles");

        // Get all vehicles with GPS data for this date
        let vehicles: Vec<(i32, i32)> = sqlx::query(
            r#"
            SELECT DISTINCT v.id as vehicle_id, v.company_id
            FROM vehicles v
            JOIN gps_devices d ON d.id = v.gps_device_id
            JOIN gps_positions p ON p.device_id = d.id
            WHERE DATE(p.recorded_at) = $1
            "#,
        )
        .bind(date)
        .fetch_all(&self.pool)
        .await?
        .iter()
        .map(|row| (row.get("vehicle_id"), row.get("company_id")))
        .collect();

        info!(vehicle_count = vehicles.len(), "Found vehicles with GPS data");

        let mut results = Vec::new();
        for (vehicle_id, company_id) in vehicles {
            match self.calculate_for_vehicle(vehicle_id, company_id, date).await {
                Ok(stats) => results.push(stats),
                Err(e) => warn!(?e, vehicle_id, "Failed to calculate statistics"),
            }
        }

        Ok(results)
    }

    /// Calculate daily statistics for a specific vehicle
    pub async fn calculate_for_vehicle(
        &self,
        vehicle_id: i32,
        company_id: i32,
        date: NaiveDate,
    ) -> Result<DailyStatisticsRecord> {
        // Get GPS device for this vehicle
        let device_id: Option<i32> = sqlx::query_scalar(
            "SELECT gps_device_id FROM vehicles WHERE id = $1"
        )
        .bind(vehicle_id)
        .fetch_optional(&self.pool)
        .await?
        .flatten();

        let device_id = device_id.unwrap_or(0);

        // Calculate position-based statistics
        let pos_stats = self.calculate_position_stats(device_id, date).await?;

        // Count trips
        let trip_count: i64 = sqlx::query_scalar(
            r#"
            SELECT COUNT(*) FROM trips 
            WHERE vehicle_id = $1 AND DATE(start_time) = $2
            "#,
        )
        .bind(vehicle_id)
        .bind(date)
        .fetch_one(&self.pool)
        .await?;

        // Count driving events
        let driving_events = self.count_driving_events(vehicle_id, date).await?;

        // Count geofence events
        let geofence_events_count: i64 = sqlx::query_scalar(
            r#"
            SELECT COUNT(*) FROM geofence_events 
            WHERE vehicle_id = $1 AND DATE(timestamp) = $2
            "#,
        )
        .bind(vehicle_id)
        .bind(date)
        .fetch_one(&self.pool)
        .await?;

        // Count alerts
        let alert_count: i64 = sqlx::query_scalar(
            r#"
            SELECT COUNT(*) FROM gps_alerts 
            WHERE device_id = $1 AND DATE(recorded_at) = $2
            "#,
        )
        .bind(device_id)
        .bind(date)
        .fetch_one(&self.pool)
        .await?;

        // Calculate driver score (simple algorithm)
        let driver_score = self.calculate_driver_score(
            driving_events.harsh_braking,
            driving_events.harsh_acceleration,
            driving_events.sharp_turns,
            driving_events.overspeeding,
            pos_stats.distance_km,
        );

        // Fuel efficiency
        let fuel_efficiency = if let Some(fuel) = pos_stats.fuel_consumed {
            if fuel > 0.0 && pos_stats.distance_km > 0.0 {
                Some(pos_stats.distance_km / fuel)
            } else {
                None
            }
        } else {
            None
        };

        Ok(DailyStatisticsRecord {
            vehicle_id,
            company_id,
            date,
            distance_km: pos_stats.distance_km,
            driving_time_minutes: pos_stats.driving_time_minutes,
            idle_time_minutes: pos_stats.idle_time_minutes,
            stopped_time_minutes: pos_stats.stopped_time_minutes,
            trip_count: trip_count as i32,
            average_speed_kph: pos_stats.avg_speed,
            max_speed_kph: pos_stats.max_speed,
            overspeeding_events: driving_events.overspeeding,
            overspeeding_minutes: driving_events.overspeeding_minutes,
            fuel_consumed_liters: pos_stats.fuel_consumed,
            fuel_efficiency_km_per_liter: fuel_efficiency,
            harsh_braking_count: driving_events.harsh_braking,
            harsh_acceleration_count: driving_events.harsh_acceleration,
            sharp_turn_count: driving_events.sharp_turns,
            start_mileage: pos_stats.start_mileage,
            end_mileage: pos_stats.end_mileage,
            engine_on_time_minutes: Some(pos_stats.engine_on_minutes),
            engine_off_time_minutes: Some(pos_stats.engine_off_minutes),
            alert_count: alert_count as i32,
            geofence_events_count: geofence_events_count as i32,
            driver_score: Some(driver_score),
        })
    }

    /// Calculate position-based statistics
    async fn calculate_position_stats(&self, device_id: i32, date: NaiveDate) -> Result<PositionStats> {
        let row = sqlx::query(
            r#"
            WITH ordered_positions AS (
                SELECT 
                    recorded_at,
                    latitude,
                    longitude,
                    speed_kph,
                    ignition_on,
                    odometer_km,
                    LAG(latitude) OVER (ORDER BY recorded_at) as prev_lat,
                    LAG(longitude) OVER (ORDER BY recorded_at) as prev_lng,
                    LAG(recorded_at) OVER (ORDER BY recorded_at) as prev_time,
                    LAG(ignition_on) OVER (ORDER BY recorded_at) as prev_ignition
                FROM gps_positions
                WHERE device_id = $1 AND DATE(recorded_at) = $2
                ORDER BY recorded_at
            )
            SELECT 
                COUNT(*) as position_count,
                -- Distance calculation using Haversine approximation
                COALESCE(SUM(
                    CASE WHEN prev_lat IS NOT NULL THEN
                        111.0 * SQRT(
                            POWER(latitude - prev_lat, 2) + 
                            POWER((longitude - prev_lng) * COS(RADIANS(latitude)), 2)
                        )
                    ELSE 0 END
                ), 0) as total_distance_km,
                -- Speed stats
                COALESCE(AVG(CASE WHEN speed_kph > 0 THEN speed_kph END), 0) as avg_speed,
                COALESCE(MAX(speed_kph), 0) as max_speed,
                -- Time calculations
                COALESCE(SUM(
                    CASE WHEN prev_time IS NOT NULL AND speed_kph > 5 THEN
                        EXTRACT(EPOCH FROM (recorded_at - prev_time)) / 60.0
                    ELSE 0 END
                ), 0) as driving_time_minutes,
                COALESCE(SUM(
                    CASE WHEN prev_time IS NOT NULL AND speed_kph <= 5 AND COALESCE(ignition_on, false) = true THEN
                        EXTRACT(EPOCH FROM (recorded_at - prev_time)) / 60.0
                    ELSE 0 END
                ), 0) as idle_time_minutes,
                COALESCE(SUM(
                    CASE WHEN prev_time IS NOT NULL AND COALESCE(ignition_on, false) = false THEN
                        EXTRACT(EPOCH FROM (recorded_at - prev_time)) / 60.0
                    ELSE 0 END
                ), 0) as stopped_time_minutes,
                -- Engine time
                COALESCE(SUM(
                    CASE WHEN prev_time IS NOT NULL AND COALESCE(ignition_on, false) = true THEN
                        EXTRACT(EPOCH FROM (recorded_at - prev_time)) / 60.0
                    ELSE 0 END
                ), 0) as engine_on_minutes,
                COALESCE(SUM(
                    CASE WHEN prev_time IS NOT NULL AND COALESCE(ignition_on, false) = false THEN
                        EXTRACT(EPOCH FROM (recorded_at - prev_time)) / 60.0
                    ELSE 0 END
                ), 0) as engine_off_minutes,
                -- Mileage
                COALESCE(MIN(odometer_km), 0) as start_mileage,
                COALESCE(MAX(odometer_km), 0) as end_mileage
            FROM ordered_positions
            "#,
        )
        .bind(device_id)
        .bind(date)
        .fetch_one(&self.pool)
        .await?;

        // Get fuel consumption from fuel_records
        let fuel_consumed: Option<f64> = sqlx::query_scalar(
            r#"
            SELECT SUM(ABS(fuel_change)) 
            FROM fuel_records 
            WHERE device_id = $1 
              AND DATE(recorded_at) = $2 
              AND event_type = 'consumption'
            "#,
        )
        .bind(device_id)
        .bind(date)
        .fetch_optional(&self.pool)
        .await?
        .flatten();

        Ok(PositionStats {
            distance_km: row.try_get::<f64, _>("total_distance_km").unwrap_or(0.0),
            avg_speed: row.try_get::<Option<f64>, _>("avg_speed").ok().flatten(),
            max_speed: row.try_get::<Option<f64>, _>("max_speed").ok().flatten(),
            driving_time_minutes: row.try_get::<f64, _>("driving_time_minutes").unwrap_or(0.0) as i32,
            idle_time_minutes: row.try_get::<f64, _>("idle_time_minutes").unwrap_or(0.0) as i32,
            stopped_time_minutes: row.try_get::<f64, _>("stopped_time_minutes").unwrap_or(0.0) as i32,
            engine_on_minutes: row.try_get::<f64, _>("engine_on_minutes").unwrap_or(0.0) as i32,
            engine_off_minutes: row.try_get::<f64, _>("engine_off_minutes").unwrap_or(0.0) as i32,
            start_mileage: row.try_get::<i32, _>("start_mileage").unwrap_or(0),
            end_mileage: row.try_get::<i32, _>("end_mileage").unwrap_or(0),
            fuel_consumed,
        })
    }

    /// Count driving events by type
    async fn count_driving_events(&self, vehicle_id: i32, date: NaiveDate) -> Result<DrivingEventCounts> {
        let row = sqlx::query(
            r#"
            SELECT 
                COALESCE(SUM(CASE WHEN type = 'harsh_braking' THEN 1 ELSE 0 END), 0) as harsh_braking,
                COALESCE(SUM(CASE WHEN type = 'harsh_acceleration' THEN 1 ELSE 0 END), 0) as harsh_acceleration,
                COALESCE(SUM(CASE WHEN type IN ('sharp_turn', 'cornering') THEN 1 ELSE 0 END), 0) as sharp_turns,
                COALESCE(SUM(CASE WHEN type = 'overspeeding' THEN 1 ELSE 0 END), 0) as overspeeding
            FROM driving_events
            WHERE vehicle_id = $1 AND DATE(timestamp) = $2
            "#,
        )
        .bind(vehicle_id)
        .bind(date)
        .fetch_one(&self.pool)
        .await?;

        // Calculate overspeeding minutes from gps_positions
        let overspeeding_minutes: f64 = sqlx::query_scalar(
            r#"
            WITH ordered AS (
                SELECT 
                    recorded_at,
                    speed_kph,
                    LAG(recorded_at) OVER (ORDER BY recorded_at) as prev_time
                FROM gps_positions p
                JOIN gps_devices d ON d.id = p.device_id
                JOIN vehicles v ON v.gps_device_id = d.id
                WHERE v.id = $1 AND DATE(p.recorded_at) = $2
            )
            SELECT COALESCE(SUM(
                CASE WHEN speed_kph > 120 AND prev_time IS NOT NULL THEN
                    EXTRACT(EPOCH FROM (recorded_at - prev_time)) / 60.0
                ELSE 0 END
            ), 0)
            FROM ordered
            "#,
        )
        .bind(vehicle_id)
        .bind(date)
        .fetch_one(&self.pool)
        .await?;

        Ok(DrivingEventCounts {
            harsh_braking: row.get::<i64, _>("harsh_braking") as i32,
            harsh_acceleration: row.get::<i64, _>("harsh_acceleration") as i32,
            sharp_turns: row.get::<i64, _>("sharp_turns") as i32,
            overspeeding: row.get::<i64, _>("overspeeding") as i32,
            overspeeding_minutes: overspeeding_minutes as i32,
        })
    }

    /// Calculate driver score (0-100)
    fn calculate_driver_score(
        &self,
        harsh_braking: i32,
        harsh_acceleration: i32,
        sharp_turns: i32,
        overspeeding: i32,
        distance_km: f64,
    ) -> i32 {
        if distance_km < 1.0 {
            return 100; // Not enough data
        }

        // Penalties per event type (per 100 km)
        let normalized_distance = distance_km / 100.0;
        let penalty_harsh_braking = (harsh_braking as f64 / normalized_distance.max(1.0)) * 3.0;
        let penalty_harsh_accel = (harsh_acceleration as f64 / normalized_distance.max(1.0)) * 2.0;
        let penalty_sharp_turns = (sharp_turns as f64 / normalized_distance.max(1.0)) * 1.5;
        let penalty_overspeeding = (overspeeding as f64 / normalized_distance.max(1.0)) * 4.0;

        let total_penalty = penalty_harsh_braking + penalty_harsh_accel + penalty_sharp_turns + penalty_overspeeding;
        let score = (100.0 - total_penalty).max(0.0).min(100.0);

        score as i32
    }

    /// Save statistics to database (upsert)
    pub async fn save_statistics(&self, stats: &DailyStatisticsRecord) -> Result<i64> {
        let row = sqlx::query(
            r#"
            INSERT INTO daily_statistics (
                "VehicleId", "Date", "DistanceKm", "DrivingTimeMinutes", "IdleTimeMinutes",
                "StoppedTimeMinutes", "TripCount", "AverageSpeedKph", "MaxSpeedKph",
                "OverspeedingEvents", "OverspeedingMinutes", "FuelConsumedLiters",
                "FuelEfficiencyKmPerLiter", "HarshBrakingCount", "HarshAccelerationCount",
                "SharpTurnCount", "StartMileage", "EndMileage", "EngineOnTimeMinutes",
                "EngineOffTimeMinutes", "AlertCount", "GeofenceEventsCount", "DriverScore",
                "CompanyId", "CreatedAt", "UpdatedAt"
            ) VALUES (
                $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19, $20, $21, $22, $23, $24, NOW(), NOW()
            )
            ON CONFLICT ("VehicleId", "Date") DO UPDATE SET
                "DistanceKm" = EXCLUDED."DistanceKm",
                "DrivingTimeMinutes" = EXCLUDED."DrivingTimeMinutes",
                "IdleTimeMinutes" = EXCLUDED."IdleTimeMinutes",
                "StoppedTimeMinutes" = EXCLUDED."StoppedTimeMinutes",
                "TripCount" = EXCLUDED."TripCount",
                "AverageSpeedKph" = EXCLUDED."AverageSpeedKph",
                "MaxSpeedKph" = EXCLUDED."MaxSpeedKph",
                "OverspeedingEvents" = EXCLUDED."OverspeedingEvents",
                "OverspeedingMinutes" = EXCLUDED."OverspeedingMinutes",
                "FuelConsumedLiters" = EXCLUDED."FuelConsumedLiters",
                "FuelEfficiencyKmPerLiter" = EXCLUDED."FuelEfficiencyKmPerLiter",
                "HarshBrakingCount" = EXCLUDED."HarshBrakingCount",
                "HarshAccelerationCount" = EXCLUDED."HarshAccelerationCount",
                "SharpTurnCount" = EXCLUDED."SharpTurnCount",
                "StartMileage" = EXCLUDED."StartMileage",
                "EndMileage" = EXCLUDED."EndMileage",
                "EngineOnTimeMinutes" = EXCLUDED."EngineOnTimeMinutes",
                "EngineOffTimeMinutes" = EXCLUDED."EngineOffTimeMinutes",
                "AlertCount" = EXCLUDED."AlertCount",
                "GeofenceEventsCount" = EXCLUDED."GeofenceEventsCount",
                "DriverScore" = EXCLUDED."DriverScore",
                "UpdatedAt" = NOW()
            RETURNING "Id"
            "#,
        )
        .bind(stats.vehicle_id)
        .bind(stats.date)
        .bind(stats.distance_km)
        .bind(stats.driving_time_minutes)
        .bind(stats.idle_time_minutes)
        .bind(stats.stopped_time_minutes)
        .bind(stats.trip_count)
        .bind(stats.average_speed_kph)
        .bind(stats.max_speed_kph)
        .bind(stats.overspeeding_events)
        .bind(stats.overspeeding_minutes)
        .bind(stats.fuel_consumed_liters)
        .bind(stats.fuel_efficiency_km_per_liter)
        .bind(stats.harsh_braking_count)
        .bind(stats.harsh_acceleration_count)
        .bind(stats.sharp_turn_count)
        .bind(stats.start_mileage)
        .bind(stats.end_mileage)
        .bind(stats.engine_on_time_minutes)
        .bind(stats.engine_off_time_minutes)
        .bind(stats.alert_count)
        .bind(stats.geofence_events_count)
        .bind(stats.driver_score)
        .bind(stats.company_id)
        .fetch_one(&self.pool)
        .await?;

        Ok(row.get::<i64, _>("Id"))
    }

    /// Calculate and save statistics for all vehicles for yesterday
    pub async fn run_daily_job(&self) -> Result<usize> {
        let yesterday = Utc::now().date_naive() - chrono::Duration::days(1);
        info!(%yesterday, "Running daily statistics job");

        let stats = self.calculate_for_date(yesterday).await?;
        let mut saved_count = 0;

        for stat in &stats {
            match self.save_statistics(stat).await {
                Ok(id) => {
                    info!(id, vehicle_id = stat.vehicle_id, "Daily statistics saved");
                    saved_count += 1;
                }
                Err(e) => warn!(?e, vehicle_id = stat.vehicle_id, "Failed to save statistics"),
            }
        }

        info!(saved_count, total = stats.len(), "Daily statistics job completed");
        Ok(saved_count)
    }
}

#[derive(Debug)]
struct PositionStats {
    distance_km: f64,
    avg_speed: Option<f64>,
    max_speed: Option<f64>,
    driving_time_minutes: i32,
    idle_time_minutes: i32,
    stopped_time_minutes: i32,
    engine_on_minutes: i32,
    engine_off_minutes: i32,
    start_mileage: i32,
    end_mileage: i32,
    fuel_consumed: Option<f64>,
}

#[derive(Debug)]
struct DrivingEventCounts {
    harsh_braking: i32,
    harsh_acceleration: i32,
    sharp_turns: i32,
    overspeeding: i32,
    overspeeding_minutes: i32,
}
