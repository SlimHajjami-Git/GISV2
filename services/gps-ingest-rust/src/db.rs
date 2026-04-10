use anyhow::{Context, Result};
use async_trait::async_trait;
use chrono::{DateTime, NaiveDateTime, Utc};
use serde_json::json;
use sqlx::{postgres::PgPool, Row};
use std::collections::HashMap;
use std::sync::Mutex;

use crate::{
    ports::TelemetryStore,
    services::gap_filler::{GapFiller, LastPosition},
    services::geofence_detector::{Geofence, GeofenceEvent, GeofenceType, Point},
    telemetry::model::{HhFrame, HhInfoFrame, ProtocolMetadata},
};

/// Number of consecutive rejected odometer values before we trust the new stream.
/// If the GPS sends N coherent values in a row that all differ from our cached reference,
/// the cache is likely poisoned (bad historical data) — reset and trust the device.
const ODOMETER_RESET_THRESHOLD: u32 = 5;

/// Tracks consecutive odometer rejections for poisoned-cache detection
#[derive(Debug, Clone, Default)]
struct OdometerRejectTracker {
    /// Number of consecutive rejections
    count: u32,
    /// Last rejected value (to check if the new stream is coherent with itself)
    last_rejected_value: Option<i64>,
}

pub struct Database {
    pool: PgPool,
    gap_filler: GapFiller,
    /// Accumulates fractional km per device for GPS-based mileage (V1 frames)
    /// Flushes to DB when accumulated distance >= 1 km
    mileage_accumulator: Mutex<HashMap<i32, f64>>,
    /// In-memory cache: device_uid -> (device_id, firmware_version)
    /// Avoids DB UPSERT on every frame for known devices
    device_cache: Mutex<HashMap<String, (i32, String)>>,
    /// In-memory cache: device_id -> last known position
    /// Avoids ORDER BY recorded_at DESC query on every frame
    last_position_cache: Mutex<HashMap<i32, LastKnownPosition>>,
    /// Tracks consecutive odometer spike rejections per device.
    /// Used to detect poisoned cache and auto-recover.
    odometer_reject_tracker: Mutex<HashMap<i32, OdometerRejectTracker>>,
}

pub struct LastKnownPosition {
    pub latitude: f64,
    pub longitude: f64,
    pub recorded_at: NaiveDateTime,
    pub ignition_on: bool,
    pub odometer_km: Option<i64>,
}

/// Maximum allowed odometer jump between two consecutive frames (km).
/// GPS sends every 1 min (moving) or 30 min (stopped).
/// At 250 km/h for 30 min = 125 km max. Use 200 km as reasonable threshold.
const MAX_ODOMETER_JUMP_KM: i64 = 200;

/// Absolute maximum realistic odometer for fleet vehicles (km).
/// Any value above this is GPS noise / firmware artifact — reject unconditionally.
/// No fleet vehicle can realistically reach 2,000,000 km.
const MAX_ABSOLUTE_ODOMETER_KM: i64 = 2_000_000;

/// Maximum allowed odometer value for the auto-update to vehicles.mileage (km).
/// Last line of defense: even if odometer passes spike filter,
/// the UPDATE query itself refuses values above this cap.
const MAX_VEHICLE_MILEAGE_KM: i64 = 2_000_000;

/// Minimum interval (in seconds) between position recordings for stopped vehicles (ignition OFF)
const STOPPED_VEHICLE_INTERVAL_SECS: i64 = 30; // 30 seconds when ignition OFF

/// Speed threshold (km/h) below which vehicle is considered stopped
const STOPPED_SPEED_THRESHOLD: f64 = 10.0;

impl Database {
    async fn ingest_hh_frame_impl(
        &self,
        device_uid: &str,
        protocol_type: &str,
        frame: &HhFrame,
        event_key: &str,
    ) -> Result<()> {
        let metadata = ProtocolMetadata::from_protocol(protocol_type);
        let (device_id, _firmware_version) = self
            .ensure_device(device_uid, protocol_type, metadata)
            .await?;

        // Fetch last position (needed for stopped-vehicle check AND GPS-based mileage)
        let last_pos = self.fetch_last_position(device_id).await?;

        // Check if vehicle is stopped (ignition off AND speed < 10 km/h)
        let is_stopped = !frame.ignition_on && frame.speed_kph < STOPPED_SPEED_THRESHOLD;

        // For stopped vehicles, apply recording interval (but always record ignition state changes)
        if is_stopped {
            if let Some(ref last_pos) = last_pos {
                // Always record if ignition state changed (first stop or engine turned on)
                let ignition_changed = last_pos.ignition_on != frame.ignition_on;
                
                if !ignition_changed {
                    let elapsed_secs = (frame.recorded_at - last_pos.recorded_at).num_seconds();
                    
                    if elapsed_secs < STOPPED_VEHICLE_INTERVAL_SECS {
                        // Skip this position - not enough time has passed and ignition didn't change
                        tracing::debug!(
                            device_uid,
                            elapsed_secs,
                            required_secs = STOPPED_VEHICLE_INTERVAL_SECS,
                            "Skipping stopped vehicle position (interval not reached)"
                        );
                        // Still update last communication even if we skip the position
                        self.update_device_last_communication(device_id).await?;
                        return Ok(());
                    }
                } else {
                    tracing::info!(
                        device_uid,
                        ignition_on = frame.ignition_on,
                        "Recording position due to ignition state change"
                    );
                }
            }
        }

        // Auto-detect FMS capability from the frame itself:
        // V3 frames (payload >= 100 chars) contain FMS data (fuel, odometer, RPM, temp)
        // V1 frames (payload < 100 chars) are GPS-only (no FMS)
        let has_fms = matches!(frame.version, crate::telemetry::model::FrameVersion::V3);

        // NEMS L devices (gps_type_1) have CAN bus odometer — their mileage comes from
        // odometer_km, NOT from Haversine GPS distance. Even when a NEMS L sends a V1 frame
        // (no FMS data in that particular frame), we must NOT accumulate Haversine mileage
        // to avoid double-counting on top of the FMS odometer auto-update.
        let is_fms_device = protocol_type == "gps_type_1";

        // Gap filling: DISABLED - Store raw GPS positions only (like GISV1)
        // The interpolated positions were causing less accurate playback compared to raw data
        // To re-enable, uncomment the code below
        /*
        if let Some(last_pos) = self.fetch_last_position(device_id).await? {
            let new_time = DateTime::<Utc>::from_naive_utc_and_offset(frame.recorded_at, Utc);
            let last_time = DateTime::<Utc>::from_naive_utc_and_offset(last_pos.recorded_at, Utc);
            
            let last_position = LastPosition {
                device_id,
                latitude: last_pos.latitude,
                longitude: last_pos.longitude,
                recorded_at: last_time,
                speed_kph: 0.0, // Not stored in LastKnownPosition
                ignition_on: last_pos.ignition_on,
            };

            // Fill gap with interpolated positions
            match self.gap_filler.fill_gap(
                device_id,
                &last_position,
                frame.latitude,
                frame.longitude,
                new_time,
            ).await {
                Ok((positions, _distance_km)) => {
                    if !positions.is_empty() {
                        if let Err(e) = self.gap_filler.insert_interpolated_positions(&self.pool, &positions).await {
                            tracing::warn!(device_id, error = %e, "Failed to insert interpolated positions");
                        }
                    }
                }
                Err(e) => {
                    tracing::warn!(device_id, error = %e, "Gap filling failed");
                }
            }
        }
        */

        let (position_id, filtered_odometer) = self.insert_position(device_id, frame, event_key, has_fms, &last_pos).await?;

        // Update last position cache for next frame's stopped-vehicle check
        // IMPORTANT: use filtered_odometer (not raw frame.odometer_km) to prevent
        // caching erroneous spikes that would poison subsequent comparisons
        if position_id > 0 {
            self.update_last_position_cache_filtered(device_id, frame, filtered_odometer);
        }
        
        if position_id == 0 {
            tracing::warn!(
                device_uid,
                event_key,
                speed_kph = frame.speed_kph,
                ignition = frame.ignition_on,
                "Position rejected by DB (duplicate event_key)"
            );
        }

        // GPS-based mileage calculation for non-FMS devices (NEMS S, Noron)
        // Accumulate Haversine distance between consecutive positions.
        // NEMS L (is_fms_device) is excluded: its mileage comes from CAN bus odometer_km,
        // even when it occasionally sends V1 frames without FMS data.
        if !is_fms_device && !has_fms && position_id > 0 && frame.speed_kph > 0.0 && frame.is_valid {
            if let Some(ref last) = last_pos {
                let distance_km = haversine_distance_km(
                    last.latitude, last.longitude,
                    frame.latitude, frame.longitude,
                );
                // Min 10m (filter noise), max 10km per frame (filter GPS jumps)
                if distance_km > 0.01 && distance_km < 10.0 {
                    self.increment_vehicle_mileage(device_id, distance_km).await;
                }
            }
        }

        // Insert alert if send_flag indicates an event
        if frame.send_flag != 0 {
            let _alert_id = self.insert_alert(device_id, frame).await?;
        }

        // Update device last communication
        self.update_device_last_communication(device_id).await?;

        Ok(())
    }

    async fn fetch_last_position(&self, device_id: i32) -> Result<Option<LastKnownPosition>> {
        // Check in-memory cache first
        {
            let cache = self.last_position_cache.lock().unwrap();
            if let Some(cached) = cache.get(&device_id) {
                return Ok(Some(LastKnownPosition {
                    latitude: cached.latitude,
                    longitude: cached.longitude,
                    recorded_at: cached.recorded_at,
                    ignition_on: cached.ignition_on,
                    odometer_km: cached.odometer_km,
                }));
            }
        }

        // Cache miss — hit DB
        let row = sqlx::query(
            r#"
            SELECT latitude, longitude, recorded_at, ignition_on, odometer_km
            FROM gps_positions
            WHERE device_id = $1
            ORDER BY recorded_at DESC
            LIMIT 1
            "#,
        )
        .bind(device_id)
        .fetch_optional(&self.pool)
        .await?;

        let result = row.map(|r| {
            let recorded_at: DateTime<Utc> = r.get("recorded_at");

            LastKnownPosition {
                latitude: r.get("latitude"),
                longitude: r.get("longitude"),
                recorded_at: recorded_at.naive_utc(),
                ignition_on: r.get::<Option<bool>, _>("ignition_on").unwrap_or(false),
                odometer_km: r.get::<Option<i64>, _>("odometer_km"),
            }
        });

        // Store in cache if found
        if let Some(ref pos) = result {
            let mut cache = self.last_position_cache.lock().unwrap();
            cache.insert(device_id, LastKnownPosition {
                latitude: pos.latitude,
                longitude: pos.longitude,
                recorded_at: pos.recorded_at,
                ignition_on: pos.ignition_on,
                odometer_km: pos.odometer_km,
            });
        }

        Ok(result)
    }

    /// Update the last position cache after a successful insert.
    /// Uses the FILTERED odometer (from spike filter) instead of raw frame value
    /// to prevent caching erroneous spikes that would poison subsequent comparisons.
    fn update_last_position_cache_filtered(&self, device_id: i32, frame: &HhFrame, filtered_odometer: Option<i64>) {
        let mut cache = self.last_position_cache.lock().unwrap();
        cache.insert(device_id, LastKnownPosition {
            latitude: frame.latitude,
            longitude: frame.longitude,
            recorded_at: frame.recorded_at,
            ignition_on: frame.ignition_on,
            odometer_km: filtered_odometer,
        });
    }

    async fn ingest_info_frame_impl(&self, protocol_type: &str, info: &HhInfoFrame) -> Result<String> {
        let imei = info
            .imei
            .as_ref()
            .context("HH info frame missing IMEI")?
            .trim()
            .to_string();

        let firmware = info
            .firmware_version
            .trim()
            .to_string();

        let protocol_metadata = ProtocolMetadata::from_protocol(protocol_type);
        let firmware_value: String = protocol_metadata
            .firmware_flavor
            .map(|f| f.to_string())
            .unwrap_or_else(|| firmware.clone());

        // MAT is the GPS logical identifier (e.g., "NR08G0664"), distinct from vehicle plate_number
        let mat = info.mat.as_ref().map(|m| m.trim().to_string());
        
        // Use MAT as GPS model if it looks like a model number (e.g., "NR08G0664")
        // This is important to differentiate GPS capabilities (FMS support, etc.)
        let model_value: Option<String> = mat.as_ref()
            .filter(|m| m.starts_with("NR") || m.starts_with("MF"))
            .cloned()
            .or_else(|| protocol_metadata.model_name.map(|m| m.to_string()));
        
        if let Some(ref model) = model_value {
            tracing::info!(
                imei = %imei,
                model = %model,
                "GPS model detected from info frame"
            );
        }

        // Insert into gps_devices table (EF Core schema)
        // Default CompanyId = 1 (Belive) for testing - devices will be reassigned when linked to vehicles
        const DEFAULT_COMPANY_ID: i32 = 1; // Belive company
        
        sqlx::query(
            r#"
            INSERT INTO gps_devices (
                device_uid,
                mat,
                label,
                protocol_type,
                firmware_version,
                model,
                status,
                last_communication,
                company_id,
                created_at,
                updated_at
            )
            VALUES ($1, $2, $3, $4, $5, $6, 'unassigned', NOW(), $7, NOW(), NOW())
            ON CONFLICT (device_uid) DO UPDATE
                SET mat = COALESCE(EXCLUDED.mat, gps_devices.mat),
                    label = COALESCE(gps_devices.label, EXCLUDED.label),
                    protocol_type = EXCLUDED.protocol_type,
                    firmware_version = CASE
                        WHEN gps_devices.firmware_version IS NULL OR gps_devices.firmware_version = ''
                            THEN EXCLUDED.firmware_version
                        ELSE gps_devices.firmware_version
                    END,
                    model = CASE
                        WHEN gps_devices.model IS NULL OR gps_devices.model = ''
                            THEN EXCLUDED.model
                        ELSE gps_devices.model
                    END,
                    last_communication = NOW(),
                    updated_at = NOW()
            "#,
        )
        .bind(&imei)
        .bind(&mat)
        .bind(&firmware)
        .bind(protocol_type)
        .bind(&firmware_value)
        .bind(&model_value)
        .bind(DEFAULT_COMPANY_ID)
        .execute(&self.pool)
        .await?;

        // Auto-link device to vehicle: match gps_devices.mat with vehicles.plate_number
        // Only link if vehicle doesn't already have a device assigned
        if let Some(ref mat_value) = mat {
            let linked = sqlx::query(
                r#"
                UPDATE vehicles
                SET gps_device_id = d.id, updated_at = NOW()
                FROM gps_devices d
                WHERE d.device_uid = $1
                  AND vehicles.plate_number = $2
                  AND vehicles.gps_device_id IS NULL
                RETURNING vehicles.id
                "#,
            )
            .bind(&imei)
            .bind(mat_value)
            .fetch_optional(&self.pool)
            .await?;

            if let Some(row) = linked {
                let vehicle_id: i32 = row.get("id");
                tracing::info!(
                    imei = %imei,
                    mat = %mat_value,
                    vehicle_id = vehicle_id,
                    "Auto-linked GPS device to vehicle via MAT/plate_number match"
                );
            }
        }

        Ok(imei)
    }

    pub async fn update_device_last_communication(&self, device_id: i32) -> Result<()> {
        sqlx::query(
            r#"
            UPDATE gps_devices 
            SET last_communication = NOW(), updated_at = NOW()
            WHERE id = $1
            "#,
        )
        .bind(device_id)
        .execute(&self.pool)
        .await?;
        Ok(())
    }
}

#[async_trait]
impl TelemetryStore for Database {
    async fn ingest_info_frame(&self, protocol_type: &str, info: &HhInfoFrame) -> Result<String> {
        self.ingest_info_frame_impl(protocol_type, info).await
    }

    async fn ingest_hh_frame(
        &self,
        device_uid: &str,
        protocol_type: &str,
        frame: &HhFrame,
        event_key: &str,
    ) -> Result<()> {
        self
            .ingest_hh_frame_impl(device_uid, protocol_type, frame, event_key)
            .await
    }

    async fn get_device_id(&self, imei: &str) -> Result<Option<i32>> {
        let row = sqlx::query(
            r#"SELECT id FROM gps_devices WHERE device_uid = $1"#,
        )
        .bind(imei)
        .fetch_optional(&self.pool)
        .await?;

        Ok(row.map(|r| r.get::<i32, _>("id")))
    }

    async fn get_device_mat(&self, device_id: i32) -> Result<Option<String>> {
        let row = sqlx::query(
            r#"SELECT mat FROM gps_devices WHERE id = $1"#,
        )
        .bind(device_id)
        .fetch_optional(&self.pool)
        .await?;

        Ok(row.and_then(|r| r.get::<Option<String>, _>("mat")))
    }

    async fn get_device_uid_by_mat(&self, mat: &str) -> Result<Option<String>> {
        let row = sqlx::query(
            r#"SELECT device_uid FROM gps_devices WHERE mat = $1 AND device_uid != 'UNKNOWN_DEVICE'"#,
        )
        .bind(mat)
        .fetch_optional(&self.pool)
        .await?;

        Ok(row.map(|r| r.get::<String, _>("device_uid")))
    }

    async fn insert_vehicle_stop(
        &self,
        stop: &crate::services::stop_detector::CompletedStop,
        vehicle_id: Option<i32>,
        company_id: i32,
    ) -> Result<i64> {
        let row = sqlx::query(
            r#"
            INSERT INTO vehicle_stops (
                vehicle_id,
                device_id,
                start_time,
                end_time,
                duration_seconds,
                latitude,
                longitude,
                stop_type,
                ignition_off,
                is_authorized,
                start_mileage,
                end_mileage,
                fuel_level_start,
                fuel_level_end,
                inside_geofence,
                company_id,
                created_at,
                updated_at
            ) VALUES (
                $1, $2, $3, $4, $5, $6, $7, $8, $9, true, $10, $11, $12, $13, false, $14, NOW(), NOW()
            )
            RETURNING id
            "#,
        )
        .bind(vehicle_id.unwrap_or(0))
        .bind(stop.device_id)
        .bind(stop.start_time)
        .bind(stop.end_time)
        .bind(stop.duration_seconds as i32)
        .bind(stop.latitude)
        .bind(stop.longitude)
        .bind(stop.stop_type.as_str())
        .bind(stop.ignition_off)
        .bind(stop.start_mileage.map(|m| m as i32))
        .bind(stop.end_mileage.map(|m| m as i32))
        .bind(stop.fuel_level_start)
        .bind(stop.fuel_level_end)
        .bind(company_id)
        .fetch_one(&self.pool)
        .await?;

        Ok(row.get::<i64, _>("id"))
    }

    async fn insert_fuel_record(
        &self,
        event: &crate::services::fuel_tracker::FuelEvent,
        vehicle_id: Option<i32>,
        company_id: i32,
    ) -> Result<i64> {
        let row = sqlx::query(
            r#"
            INSERT INTO fuel_records (
                vehicle_id,
                device_id,
                recorded_at,
                fuel_percent,
                fuel_change,
                odometer_km,
                speed_kph,
                rpm,
                ignition_on,
                latitude,
                longitude,
                event_type,
                is_anomaly,
                anomaly_reason,
                company_id,
                created_at,
                updated_at
            ) VALUES (
                $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, NOW(), NOW()
            )
            ON CONFLICT (vehicle_id, recorded_at, fuel_percent) DO NOTHING
            RETURNING id
            "#,
        )
        .bind(vehicle_id.unwrap_or(0))
        .bind(event.device_id)
        .bind(event.recorded_at)
        .bind(event.fuel_percent)
        .bind(event.fuel_change)
        .bind(event.odometer_km as i64)
        .bind(event.speed_kph)
        .bind(event.rpm.map(|r| r as i16))
        .bind(event.ignition_on)
        .bind(event.latitude)
        .bind(event.longitude)
        .bind(event.event_type.as_str())
        .bind(event.is_anomaly)
        .bind(&event.anomaly_reason)
        .bind(company_id)
        .fetch_optional(&self.pool)
        .await?;

        // ON CONFLICT DO NOTHING returns no row on duplicate — return 0
        Ok(row.map(|r| r.get::<i64, _>("id")).unwrap_or(0))
    }

    async fn get_device_vehicle_info(&self, device_id: i32) -> Result<(Option<i32>, i32, Option<String>)> {
        // Get company_id, vehicle_id, and firmware_version from gps_devices
        let row = sqlx::query(
            r#"
            SELECT d.company_id, d.firmware_version, v.id as vehicle_id
            FROM gps_devices d
            LEFT JOIN vehicles v ON v.gps_device_id = d.id
            WHERE d.id = $1
            "#,
        )
        .bind(device_id)
        .fetch_optional(&self.pool)
        .await?;

        if let Some(row) = row {
            let vehicle_id: Option<i32> = row.try_get("vehicle_id").ok();
            let company_id: i32 = row.get("company_id");
            let firmware_version: Option<String> = row.try_get("firmware_version").ok().flatten();
            Ok((vehicle_id, company_id, firmware_version))
        } else {
            Ok((None, 1, None)) // Default company_id = 1
        }
    }

    async fn get_fuel_config(&self, device_id: i32) -> Result<(String, Option<i32>)> {
        // Get fuel_sensor_mode from gps_devices + tank_capacity from vehicles
        let row = sqlx::query(
            r#"
            SELECT 
                COALESCE(d.fuel_sensor_mode, 'raw_255') as fuel_sensor_mode,
                v.fuel_tank_capacity
            FROM gps_devices d
            LEFT JOIN vehicles v ON v.gps_device_id = d.id
            WHERE d.id = $1
            "#,
        )
        .bind(device_id)
        .fetch_optional(&self.pool)
        .await?;

        if let Some(row) = row {
            let fuel_sensor_mode: String = row.get("fuel_sensor_mode");
            let tank_capacity: Option<i32> = row.get("fuel_tank_capacity");
            Ok((fuel_sensor_mode, tank_capacity))
        } else {
            Ok(("raw_255".to_string(), None)) // Default mode
        }
    }

    async fn load_geofences(&self) -> Result<Vec<Geofence>> {
        let rows = sqlx::query(
            r#"
            SELECT 
                g.id,
                g.name,
                g.type,
                g.coordinates,
                g.center_lat,
                g.center_lng,
                g.radius,
                g.alert_on_entry,
                g.alert_on_exit,
                g.notification_cooldown_minutes,
                g.active_start_time,
                g.active_end_time,
                g.active_days,
                g.company_id,
                COALESCE(
                    ARRAY_AGG(gv.vehicle_id) FILTER (WHERE gv.vehicle_id IS NOT NULL),
                    ARRAY[]::integer[]
                ) as assigned_vehicle_ids
            FROM geofences g
            LEFT JOIN geofence_vehicles gv ON gv.geofence_id = g.id
            WHERE g.is_active = true
            GROUP BY g.id
            "#,
        )
        .fetch_all(&self.pool)
        .await?;

        let mut geofences = Vec::new();
        for row in rows {
            let geofence_type_str: String = row.get("type");
            let geofence_type = match geofence_type_str.as_str() {
                "circle" => GeofenceType::Circle,
                _ => GeofenceType::Polygon,
            };

            // Parse coordinates from JSONB
            let coordinates: Vec<Point> = if let Ok(coords_json) = row.try_get::<serde_json::Value, _>("coordinates") {
                serde_json::from_value(coords_json).unwrap_or_default()
            } else {
                vec![]
            };

            // Parse active_days from JSONB
            let active_days: Option<Vec<String>> = if let Ok(days_json) = row.try_get::<serde_json::Value, _>("active_days") {
                serde_json::from_value(days_json).ok()
            } else {
                None
            };

            // Parse times
            let active_start_time: Option<chrono::NaiveTime> = row.try_get("active_start_time").ok();
            let active_end_time: Option<chrono::NaiveTime> = row.try_get("active_end_time").ok();

            let assigned_vehicle_ids: Vec<i32> = row.try_get("assigned_vehicle_ids").unwrap_or_default();

            geofences.push(Geofence {
                id: row.get("id"),
                name: row.get("name"),
                geofence_type,
                coordinates,
                center_lat: row.try_get("center_lat").ok(),
                center_lng: row.try_get("center_lng").ok(),
                radius: row.try_get("radius").ok(),
                alert_on_entry: row.get("alert_on_entry"),
                alert_on_exit: row.get("alert_on_exit"),
                notification_cooldown_minutes: row.try_get("notification_cooldown_minutes").unwrap_or(5),
                active_start_time,
                active_end_time,
                active_days,
                company_id: row.get("company_id"),
                assigned_vehicle_ids,
            });
        }

        Ok(geofences)
    }

    async fn insert_geofence_event(&self, event: &GeofenceEvent, duration_seconds: Option<i32>) -> Result<i32> {
        let row = sqlx::query(
            r#"
            INSERT INTO geofence_events (
                geofence_id,
                vehicle_id,
                device_id,
                type,
                latitude,
                longitude,
                speed,
                duration_inside_seconds,
                is_notified,
                notified_at,
                timestamp
            ) VALUES (
                $1, $2, $3, $4, $5, $6, $7, $8, true, NOW(), $9
            )
            RETURNING id
            "#,
        )
        .bind(event.geofence_id)
        .bind(event.vehicle_id)
        .bind(event.device_id)
        .bind(event.event_type.as_str())
        .bind(event.latitude)
        .bind(event.longitude)
        .bind(event.speed)
        .bind(duration_seconds)
        .bind(event.timestamp)
        .fetch_one(&self.pool)
        .await?;

        Ok(row.get::<i32, _>("id"))
    }

    async fn get_last_position(&self, device_id: i32) -> Result<Option<LastKnownPosition>> {
        self.fetch_last_position(device_id).await
    }

    async fn insert_driving_event(&self, event: &crate::services::driving_events::DrivingEventRecord) -> Result<i64> {
        let row = sqlx::query(
            r#"
            INSERT INTO driving_events (
                "VehicleId",
                "Type",
                "Severity",
                "GForce",
                "SpeedKph",
                "Latitude",
                "Longitude",
                "Timestamp",
                "CompanyId",
                "CreatedAt",
                "UpdatedAt"
            ) VALUES (
                $1, $2, $3, $4, $5, $6, $7, $8, $9, NOW(), NOW()
            )
            RETURNING "Id" as id
            "#,
        )
        .bind(event.vehicle_id)
        .bind(event.event_type.as_str())
        .bind(event.severity.as_str())
        .bind(event.g_force)
        .bind(event.speed_kph)
        .bind(event.latitude)
        .bind(event.longitude)
        .bind(event.timestamp)
        .bind(event.company_id)
        .fetch_one(&self.pool)
        .await?;

        Ok(row.get::<i64, _>("id"))
    }

    async fn insert_device_event(&self, event: &crate::services::device_event::DeviceEventRecord) -> Result<i64> {
        let row = sqlx::query(
            r#"
            INSERT INTO device_events (
                device_id, vehicle_id, company_id, event_type, event_at,
                offline_duration_secs, last_known_lat, last_known_lon,
                last_known_address, was_moving, details, created_at
            ) VALUES (
                $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, NOW()
            )
            RETURNING id
            "#,
        )
        .bind(event.device_id)
        .bind(event.vehicle_id)
        .bind(event.company_id)
        .bind(event.event_type.as_str())
        .bind(event.event_at)
        .bind(event.offline_duration_secs)
        .bind(event.last_known_lat)
        .bind(event.last_known_lon)
        .bind(&event.last_known_address)
        .bind(event.was_moving)
        .bind(&event.details)
        .fetch_one(&self.pool)
        .await?;

        Ok(row.get::<i64, _>("id"))
    }

    async fn insert_trip(&self, trip: &crate::services::trip_detector::CompletedTrip) -> Result<i64> {
        let row = sqlx::query(
            r#"
            INSERT INTO trips (
                "VehicleId",
                "DriverId",
                "StartTime",
                "EndTime",
                "StartLatitude",
                "StartLongitude",
                "EndLatitude",
                "EndLongitude",
                "DistanceKm",
                "DurationMinutes",
                "AverageSpeedKph",
                "MaxSpeedKph",
                "FuelConsumedLiters",
                "StartMileage",
                "EndMileage",
                "HarshBrakingCount",
                "HarshAccelerationCount",
                "OverspeedingCount",
                "Status",
                "CompanyId",
                "CreatedAt",
                "UpdatedAt"
            ) VALUES (
                $1, NULL, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19, NOW(), NOW()
            )
            RETURNING "Id" as id
            "#,
        )
        .bind(trip.vehicle_id.unwrap_or(0))
        .bind(trip.start_time)
        .bind(trip.end_time)
        .bind(trip.start_lat)
        .bind(trip.start_lng)
        .bind(trip.end_lat)
        .bind(trip.end_lng)
        .bind(trip.distance_km)
        .bind(trip.duration_minutes)
        .bind(trip.avg_speed_kph)
        .bind(trip.max_speed_kph)
        .bind(trip.fuel_consumed)
        .bind(trip.start_odometer.map(|o| o as i64))
        .bind(trip.end_odometer.map(|o| o as i64))
        .bind(trip.harsh_braking_count)
        .bind(trip.harsh_accel_count)
        .bind(trip.overspeeding_count)
        .bind(&trip.status)
        .bind(trip.company_id)
        .fetch_one(&self.pool)
        .await?;

        Ok(row.get::<i64, _>("id"))
    }

    async fn get_last_fuel_record(&self, device_id: i32) -> Result<Option<(i16, u32, chrono::DateTime<chrono::Utc>)>> {
        let row = sqlx::query(
            r#"
            SELECT fuel_percent, odometer_km, recorded_at
            FROM fuel_records
            WHERE device_id = $1
            ORDER BY recorded_at DESC
            LIMIT 1
            "#,
        )
        .bind(device_id)
        .fetch_optional(&self.pool)
        .await?;

        Ok(row.map(|r| {
            let fuel_percent: i16 = r.get("fuel_percent");
            let odometer_km: i64 = r.try_get("odometer_km").unwrap_or(0);
            let recorded_at: DateTime<Utc> = r.get("recorded_at");
            (fuel_percent, odometer_km as u32, recorded_at)
        }))
    }

    async fn get_immobilization_state(&self, device_id: i32) -> Result<(bool, String)> {
        let row = sqlx::query(
            r#"
            SELECT
                COALESCE(immobilization_active, false) AS immobilization_active,
                COALESCE(command_go, E'AJ+GO#1311\n') AS command_go
            FROM gps_devices
            WHERE id = $1
            "#,
        )
        .bind(device_id)
        .fetch_optional(&self.pool)
        .await?;

        match row {
            Some(r) => {
                let active: bool = r.get("immobilization_active");
                let command_go: String = r.get("command_go");
                Ok((active, command_go))
            }
            None => Ok((false, "AJ+GO#1311\n".to_string())),
        }
    }

    async fn get_pending_command(&self, device_id: i32) -> Result<Option<(i64, String)>> {
        let row = sqlx::query(
            r#"
            UPDATE device_commands
            SET status = 'sent', sent_at = NOW(), attempts = attempts + 1, updated_at = NOW()
            WHERE id = (
                SELECT id FROM device_commands
                WHERE device_id = $1 AND status = 'pending'
                ORDER BY created_at ASC
                LIMIT 1
            )
            RETURNING id, command_text
            "#,
        )
        .bind(device_id)
        .fetch_optional(&self.pool)
        .await?;

        Ok(row.map(|r| {
            let id: i64 = r.get("id");
            let cmd: String = r.get("command_text");
            (id, cmd)
        }))
    }

    async fn update_command_status(&self, command_id: i64, status: &str, error: Option<&str>) -> Result<()> {
        sqlx::query(
            r#"
            UPDATE device_commands
            SET status = $2, error_message = $3, updated_at = NOW()
            WHERE id = $1
            "#,
        )
        .bind(command_id)
        .bind(status)
        .bind(error)
        .execute(&self.pool)
        .await?;
        Ok(())
    }

    async fn log_auto_recovery(&self, device_id: i32, command_text: &str, flags_hex: &str) -> Result<()> {
        // Get vehicle_id and company_id for the device
        let (vehicle_id, company_id, _) = self.get_device_vehicle_info(device_id).await?;

        sqlx::query(
            r#"
            INSERT INTO device_commands (
                device_id, vehicle_id, user_id, command_type, command_text,
                status, sent_at, attempts, source, company_id, error_message, created_at, updated_at
            ) VALUES (
                $1, $2, 0, 'GO', $3, 'sent', NOW(), 1, 'auto_recovery', $4, $5, NOW(), NOW()
            )
            "#,
        )
        .bind(device_id)
        .bind(vehicle_id)
        .bind(command_text)
        .bind(company_id)
        .bind(flags_hex)
        .execute(&self.pool)
        .await?;
        Ok(())
    }

    async fn update_device_last_communication(&self, device_id: i32) -> Result<()> {
        sqlx::query(
            r#"
            UPDATE gps_devices
            SET last_communication = NOW(), updated_at = NOW()
            WHERE id = $1
            "#,
        )
        .bind(device_id)
        .execute(&self.pool)
        .await?;
        Ok(())
    }

    async fn log_frame_debug(
        &self,
        device_id: Option<i32>,
        device_uid: Option<&str>,
        mat: Option<&str>,
        frame_type: &str,
        flags_hex: Option<&str>,
        flags_raw: Option<i16>,
        raw_frame: &str,
        reason: &str,
        peer: Option<&str>,
    ) -> Result<()> {
        sqlx::query(
            r#"
            INSERT INTO frame_debug_log (
                device_id, device_uid, mat, frame_type, flags_hex, flags_raw,
                raw_frame, reason, peer, created_at
            ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, NOW())
            "#,
        )
        .bind(device_id)
        .bind(device_uid)
        .bind(mat)
        .bind(frame_type)
        .bind(flags_hex)
        .bind(flags_raw)
        .bind(raw_frame)
        .bind(reason)
        .bind(peer)
        .execute(&self.pool)
        .await?;
        Ok(())
    }
}

impl Database {
    pub fn new(pool: PgPool, osrm_base_url: String) -> Self {
        Self {
            pool,
            gap_filler: GapFiller::new(osrm_base_url),
            mileage_accumulator: Mutex::new(HashMap::new()),
            device_cache: Mutex::new(HashMap::new()),
            last_position_cache: Mutex::new(HashMap::new()),
            odometer_reject_tracker: Mutex::new(HashMap::new()),
        }
    }

    async fn ensure_device(
        &self,
        device_uid: &str,
        protocol_type: &str,
        metadata: ProtocolMetadata,
    ) -> Result<(i32, String)> {
        // Check in-memory cache first
        {
            let cache = self.device_cache.lock().unwrap();
            if let Some(cached) = cache.get(device_uid) {
                return Ok(cached.clone());
            }
        }

        // Cache miss — hit DB
        const DEFAULT_COMPANY_ID: i32 = 1;
        let model = metadata.model_name;
        let firmware = metadata.firmware_flavor;

        let row = sqlx::query(
            r#"
            INSERT INTO gps_devices (device_uid, protocol_type, model, firmware_version, status, company_id, created_at, updated_at)
            VALUES ($1, $2, $3, $4, 'unassigned', $5, NOW(), NOW())
            ON CONFLICT (device_uid) DO UPDATE
                SET protocol_type = EXCLUDED.protocol_type,
                    model = CASE
                        WHEN gps_devices.model IS NULL OR gps_devices.model = ''
                            THEN EXCLUDED.model
                        ELSE gps_devices.model
                    END,
                    firmware_version = CASE
                        WHEN gps_devices.firmware_version IS NULL OR gps_devices.firmware_version = ''
                            THEN EXCLUDED.firmware_version
                        ELSE gps_devices.firmware_version
                    END,
                    updated_at = NOW()
            RETURNING id, firmware_version
            "#,
        )
        .bind(device_uid)
        .bind(protocol_type)
        .bind(model)
        .bind(firmware)
        .bind(DEFAULT_COMPANY_ID)
        .fetch_one(&self.pool)
        .await?;

        let id = row.get::<i32, _>("id");
        let fw: String = row.try_get("firmware_version").unwrap_or_default();

        // Store in cache
        {
            let mut cache = self.device_cache.lock().unwrap();
            cache.insert(device_uid.to_string(), (id, fw.clone()));
        }

        Ok((id, fw))
    }

    /// Returns (position_id, filtered_odometer_km)
    async fn insert_position(&self, device_id: i32, frame: &HhFrame, event_key: &str, has_fms: bool, last_pos: &Option<LastKnownPosition>) -> Result<(i64, Option<i64>)> {
        // Metadata for additional fields not in dedicated columns
        let metadata = json!({
            "power_source_rescue": frame.power_source_rescue,
            "temperature_raw": frame.temperature_raw,
            "remaining_payload": frame.remaining_payload,
            "added_info": frame.added_info,
            "signal_quality": frame.signal_quality,
            "raw_payload": frame.raw_payload,
        });

        // Decode protocol version from frame
        let protocol_version: i16 = match frame.version {
            crate::telemetry::model::FrameVersion::V1 => 1,
            crate::telemetry::model::FrameVersion::V3 => 3,
            crate::telemetry::model::FrameVersion::Unknown(v) => v as i16,
        };

        // Temperature: ONLY use FMS temperature from CAN bus (V3 frames, J1939 raw - 40)
        // The base frame temperature_raw is "80+Analogic1 or digital temp" (raw analog value),
        // NOT a J1939 temperature — applying -40 to it gives wrong readings (e.g. 112°C).
        let temperature_c: Option<i16> = frame.fms_temperature_c
            .filter(|&t| t != 0 && t > -50 && t < 200);
        
        // FMS fields: only store for V3 frames (has_fms) — V1 frames don't have FMS data
        let fuel_raw: Option<i32> = if has_fms && frame.fuel_raw > 0 { Some(i32::from(frame.fuel_raw)) } else { None };
        // Odometer spike filter: detect and reject erroneous jumps
        // Example: vehicle at 2310 km suddenly reports 31332 km = GPS glitch
        let last_valid_odo = last_pos.as_ref().and_then(|p| p.odometer_km);
        let odometer_km: Option<i64> = if has_fms && frame.odometer_km > 0 && frame.odometer_km != 1048574 {
            let new_odo = frame.odometer_km as i64;

            // Absolute max: reject impossible values regardless of history
            if new_odo > MAX_ABSOLUTE_ODOMETER_KM {
                tracing::warn!(
                    device_id,
                    received_km = new_odo,
                    max_km = MAX_ABSOLUTE_ODOMETER_KM,
                    "Odometer REJECTED: {}km exceeds absolute maximum {}km — GPS noise",
                    new_odo, MAX_ABSOLUTE_ODOMETER_KM
                );
                // Use last valid odometer (cache → DB fallback)
                let fallback = last_valid_odo.or_else(|| {
                    // Synchronous fallback not possible — already covered by DB query below
                    None
                });
                fallback
            } else {
                // Check for spike: compare with last known odometer
                // Resolve prev_odo: prefer cache, fallback to DB query
                let prev_odo = if let Some(cached) = last_valid_odo {
                    Some(cached)
                } else {
                    // Cache miss (pod restart) — query DB for last valid odometer
                    sqlx::query_scalar::<_, i64>(
                        r#"SELECT odometer_km FROM gps_positions
                           WHERE device_id = $1 AND odometer_km IS NOT NULL AND odometer_km > 0
                             AND odometer_km != 1048574 AND odometer_km < $2
                           ORDER BY recorded_at DESC LIMIT 1"#,
                    )
                    .bind(device_id)
                    .bind(MAX_ABSOLUTE_ODOMETER_KM)
                    .fetch_optional(&self.pool)
                    .await
                    .unwrap_or(None)
                };

                if let Some(prev) = prev_odo {
                    let jump = (new_odo - prev).abs();
                    if jump > MAX_ODOMETER_JUMP_KM {
                        // Check for poisoned cache: if we've been rejecting consecutive values
                        // that are coherent with each other, the cache is wrong — trust the device
                        let should_reset = {
                            let mut tracker = self.odometer_reject_tracker.lock().unwrap();
                            let entry = tracker.entry(device_id).or_default();
                            let coherent_with_previous_reject = entry.last_rejected_value
                                .map(|prev_rejected| (new_odo - prev_rejected).abs() <= MAX_ODOMETER_JUMP_KM)
                                .unwrap_or(true);

                            if coherent_with_previous_reject {
                                entry.count += 1;
                                entry.last_rejected_value = Some(new_odo);
                            } else {
                                // New value is also incoherent with previous rejections — true noise
                                entry.count = 1;
                                entry.last_rejected_value = Some(new_odo);
                            }
                            entry.count >= ODOMETER_RESET_THRESHOLD
                        };

                        if should_reset {
                            // Cache was poisoned — the device has been sending coherent values
                            // that we kept rejecting. Trust the new stream.
                            tracing::warn!(
                                device_id,
                                previous_km = prev,
                                received_km = new_odo,
                                consecutive_rejects = ODOMETER_RESET_THRESHOLD,
                                "Odometer cache RESET: {} consecutive coherent values rejected — cache was poisoned, trusting device",
                                ODOMETER_RESET_THRESHOLD
                            );
                            self.odometer_reject_tracker.lock().unwrap().remove(&device_id);
                            Some(new_odo)
                        } else {
                            tracing::warn!(
                                device_id,
                                previous_km = prev,
                                received_km = new_odo,
                                jump_km = jump,
                                "Odometer spike detected! Jump of {}km exceeds {}km threshold — using last valid value",
                                jump, MAX_ODOMETER_JUMP_KM
                            );
                            Some(prev) // Use last valid odometer instead of erroneous value
                        }
                    } else {
                        // Good value — clear any rejection streak
                        self.odometer_reject_tracker.lock().unwrap().remove(&device_id);
                        Some(new_odo)
                    }
                } else {
                    // No previous odometer from positions — cross-check against vehicle's current mileage
                    // to prevent first-boot garbage values from being accepted blindly
                    let vehicle_mileage: Option<i64> = sqlx::query_scalar::<_, i64>(
                        r#"SELECT mileage FROM vehicles
                           WHERE gps_device_id = (SELECT id FROM gps_devices WHERE id = $1)
                             AND mileage > 0"#,
                    )
                    .bind(device_id)
                    .fetch_optional(&self.pool)
                    .await
                    .unwrap_or(None);

                    if let Some(current_mileage) = vehicle_mileage {
                        let jump = (new_odo - current_mileage).abs();
                        if jump > MAX_ODOMETER_JUMP_KM {
                            tracing::warn!(
                                device_id,
                                vehicle_mileage = current_mileage,
                                received_km = new_odo,
                                jump_km = jump,
                                "Odometer spike on first frame! Jump of {}km from vehicle mileage {}km — rejecting",
                                jump, current_mileage
                            );
                            Some(current_mileage) // Use vehicle's known mileage
                        } else {
                            Some(new_odo)
                        }
                    } else {
                        // Truly new vehicle with no mileage at all — accept
                        Some(new_odo)
                    }
                }
            }
        } else if has_fms && frame.odometer_km == 1048574 {
            // GPS protocol artifact: replace with last valid odometer_km
            if let Some(prev_odo) = last_valid_odo {
                tracing::warn!(device_id, "GPS artifact 1048574 detected, replacing with last valid odometer: {}", prev_odo);
                Some(prev_odo)
            } else {
                // Fallback: query DB if cache miss
                let db_odo = sqlx::query_scalar::<_, i64>(
                    r#"SELECT odometer_km FROM gps_positions
                       WHERE device_id = $1 AND odometer_km IS NOT NULL AND odometer_km > 0 AND odometer_km != 1048574
                       ORDER BY recorded_at DESC LIMIT 1"#,
                )
                .bind(device_id)
                .fetch_optional(&self.pool)
                .await
                .unwrap_or(None);
                if db_odo.is_some() {
                    tracing::warn!(device_id, "GPS artifact 1048574 detected, replacing with DB odometer: {:?}", db_odo);
                }
                db_odo
            }
        } else {
            None
        };
        let rpm: Option<i16> = if has_fms { frame.rpm.filter(|&r| r > 0).map(|r| r as i16) } else { None };

        let row = sqlx::query(
            r#"
            INSERT INTO gps_positions (
                device_id,
                recorded_at,
                latitude,
                longitude,
                event_key,
                speed_kph,
                course_deg,
                altitude_m,
                ignition_on,
                fuel_raw,
                power_voltage,
                satellites,
                is_valid,
                is_real_time,
                metadata,
                created_at,
                mems_x,
                mems_y,
                mems_z,
                temperature_c,
                odometer_km,
                rpm,
                send_flag,
                protocol_version,
                address,
                fuel_rate_l_per_100km
            ) VALUES (
                $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, NOW(),
                $16, $17, $18, $19, $20, $21, $22, $23, $24, $25
            )
            ON CONFLICT (event_key) DO NOTHING
            RETURNING id
            "#,
        )
        .bind(device_id)
        .bind(frame.recorded_at)
        .bind(frame.latitude)
        .bind(frame.longitude)
        .bind(event_key)
        .bind(frame.speed_kph)
        .bind(frame.heading_deg)
        .bind(Option::<f64>::None) // AltitudeM
        .bind(frame.ignition_on)
        .bind(fuel_raw)  // NULL if GPS doesn't support FMS
        .bind(i32::from(frame.power_voltage))
        .bind(frame.satellites_in_view.map(|v| i32::from(v)))
        .bind(frame.is_valid)
        .bind(frame.is_real_time)
        .bind(metadata)
        // New AAP columns
        .bind(frame.mems_x as i16)
        .bind(frame.mems_y as i16)
        .bind(frame.mems_z as i16)
        .bind(temperature_c)
        .bind(odometer_km)  // NULL if GPS doesn't support FMS
        .bind(rpm)  // NULL if GPS doesn't support FMS
        .bind(frame.send_flag as i16)
        .bind(protocol_version)
        .bind(&frame.address) // Geocoded address
        .bind(if has_fms { frame.fuel_rate_l_per_100km } else { None }) // FMS Fuel Rate in L/100km
        .fetch_optional(&self.pool)
        .await?;

        // Auto-update vehicle mileage from GPS odometer (NEMS L sends odometer, NEMS S does not)
        // Only update if odometer > 0, only increase (never decrease),
        // and enforce absolute cap + max jump as last line of defense
        if let Some(odo) = odometer_km {
            if odo > 0 && odo < MAX_VEHICLE_MILEAGE_KM {
                let updated = sqlx::query(
                    r#"
                    UPDATE vehicles
                    SET mileage = $1, updated_at = NOW()
                    WHERE gps_device_id = (SELECT id FROM gps_devices WHERE id = $2)
                      AND mileage < $1
                      AND $1 < $3
                      AND ($1 - mileage) < $4
                    "#,
                )
                .bind(odo as i32)
                .bind(device_id)
                .bind(MAX_VEHICLE_MILEAGE_KM as i32)
                .bind(MAX_ODOMETER_JUMP_KM as i32)
                .execute(&self.pool)
                .await;

                if let Ok(result) = updated {
                    if result.rows_affected() > 0 {
                        tracing::info!(
                            device_id = device_id,
                            odometer_km = odo,
                            "Auto-updated vehicle mileage from GPS odometer"
                        );
                    }
                }
            } else if odo >= MAX_VEHICLE_MILEAGE_KM {
                tracing::warn!(
                    device_id,
                    odometer_km = odo,
                    max = MAX_VEHICLE_MILEAGE_KM,
                    "Auto-update BLOCKED: odometer {}km exceeds absolute cap {}km",
                    odo, MAX_VEHICLE_MILEAGE_KM
                );
            }
        }

        let position_id = row.map(|r| r.get::<i64, _>("id")).unwrap_or(0);
        Ok((position_id, odometer_km))
    }

    /// Accumulate GPS-calculated distance and flush to DB when >= 1 km
    /// This avoids losing fractional km due to integer mileage column
    async fn increment_vehicle_mileage(&self, device_id: i32, distance_km: f64) {
        let km_to_flush = {
            let mut acc = self.mileage_accumulator.lock().unwrap();
            let total = acc.entry(device_id).or_insert(0.0);
            *total += distance_km;
            if *total >= 1.0 {
                let flush = *total as i32; // Floor to integer km
                *total -= flush as f64;    // Keep remainder
                flush
            } else {
                0
            }
        };

        if km_to_flush > 0 {
            let result = sqlx::query(
                r#"
                UPDATE vehicles
                SET mileage = mileage + $1, updated_at = NOW()
                WHERE gps_device_id = (SELECT id FROM gps_devices WHERE id = $2)
                "#,
            )
            .bind(km_to_flush)
            .bind(device_id)
            .execute(&self.pool)
            .await;

            if let Ok(r) = result {
                if r.rows_affected() > 0 {
                    tracing::info!(
                        device_id,
                        added_km = km_to_flush,
                        "GPS-based mileage increment (V1 frame, no FMS odometer)"
                    );
                }
            }
        }
    }

    async fn insert_alert(&self, device_id: i32, frame: &HhFrame) -> Result<i32> {
        // Use gps_alerts table with EF Core column naming
        let alert_type = map_send_flag(frame.send_flag);
        let severity = map_severity(frame.send_flag);
        let message = format!("{} event detected", alert_type);

        let row = sqlx::query(
            r#"
            INSERT INTO gps_alerts (
                device_id,
                type,
                severity,
                message,
                resolved,
                latitude,
                longitude,
                timestamp,
                created_at
            ) VALUES ($1, $2, $3, $4, false, $5, $6, $7, NOW())
            RETURNING id
            "#,
        )
        .bind(device_id)
        .bind(alert_type)
        .bind(severity)
        .bind(&message)
        .bind(frame.latitude)
        .bind(frame.longitude)
        .bind(frame.recorded_at)
        .fetch_one(&self.pool)
        .await?;

        Ok(row.get::<i32, _>("id"))
    }
}

/// Calculate Haversine distance between two GPS coordinates in kilometers
fn haversine_distance_km(lat1: f64, lon1: f64, lat2: f64, lon2: f64) -> f64 {
    const EARTH_RADIUS_KM: f64 = 6371.0;

    let lat1_rad = lat1.to_radians();
    let lat2_rad = lat2.to_radians();
    let delta_lat = (lat2 - lat1).to_radians();
    let delta_lon = (lon2 - lon1).to_radians();

    let a = (delta_lat / 2.0).sin().powi(2)
        + lat1_rad.cos() * lat2_rad.cos() * (delta_lon / 2.0).sin().powi(2);
    let c = 2.0 * a.sqrt().asin();

    EARTH_RADIUS_KM * c
}

fn map_send_flag(flag: u8) -> &'static str {
    match flag {
        0 => "normal",
        1 => "periodic",
        2 => "gps_valid",
        3 => "device_cap",
        4 => "io_change",
        5 => "speeding",
        6 => "harsh_braking",
        7 => "ibutton",
        8 => "input1_event",
        9 => "input2_event",
        10 => "input3_event",
        11 => "alert",
        _ => "unknown",
    }
}

fn map_severity(flag: u8) -> &'static str {
    match flag {
        5 => "high",      // speeding
        6 => "high",      // harsh_braking
        11 => "critical", // alert
        _ => "medium",
    }
}
