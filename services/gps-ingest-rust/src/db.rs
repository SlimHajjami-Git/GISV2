use anyhow::{Context, Result};
use async_trait::async_trait;
use chrono::{DateTime, NaiveDateTime, Utc};
use serde_json::json;
use sqlx::{postgres::PgPool, Row};

use crate::{
    ports::TelemetryStore,
    services::gap_filler::{GapFiller, LastPosition},
    services::geofence_detector::{Geofence, GeofenceEvent, GeofenceType, Point},
    telemetry::model::{HhFrame, HhInfoFrame, ProtocolMetadata},
};

pub struct Database {
    pool: PgPool,
    gap_filler: GapFiller,
}

pub struct LastKnownPosition {
    pub latitude: f64,
    pub longitude: f64,
    pub recorded_at: NaiveDateTime,
    pub ignition_on: bool,
}

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
        let device_id = self
            .ensure_device(device_uid, protocol_type, metadata)
            .await?;

        // Check if vehicle is stopped (ignition off AND speed < 10 km/h)
        let is_stopped = !frame.ignition_on && frame.speed_kph < STOPPED_SPEED_THRESHOLD;

        // For stopped vehicles, apply recording interval (but always record ignition state changes)
        if is_stopped {
            if let Some(last_pos) = self.fetch_last_position(device_id).await? {
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

        // Gap filling: Check for missing frames and interpolate if needed
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

        let _position_id = self.insert_position(device_id, frame, event_key).await?;

        // Insert alert if send_flag indicates an event
        if frame.send_flag != 0 {
            let _alert_id = self.insert_alert(device_id, frame).await?;
        }

        // Update device last communication
        self.update_device_last_communication(device_id).await?;

        Ok(())
    }

    async fn fetch_last_position(&self, device_id: i32) -> Result<Option<LastKnownPosition>> {
        let row = sqlx::query(
            r#"
            SELECT latitude, longitude, recorded_at, ignition_on
            FROM gps_positions
            WHERE device_id = $1
            ORDER BY recorded_at DESC
            LIMIT 1
            "#,
        )
        .bind(device_id)
        .fetch_optional(&self.pool)
        .await?;

        Ok(row.map(|r| {
            let recorded_at: DateTime<Utc> = r.get("recorded_at");

            LastKnownPosition {
                latitude: r.get("latitude"),
                longitude: r.get("longitude"),
                recorded_at: recorded_at.naive_utc(),
                ignition_on: r.get::<Option<bool>, _>("ignition_on").unwrap_or(false),
            }
        }))
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
        let model_value: Option<String> = protocol_metadata.model_name.map(|m| m.to_string());
        let firmware_value: String = protocol_metadata
            .firmware_flavor
            .map(|f| f.to_string())
            .unwrap_or_else(|| firmware.clone());

        // MAT is the GPS logical identifier (e.g., "NR08G0663"), distinct from vehicle plate_number
        let mat = info.mat.as_ref().map(|m| m.trim().to_string());

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

        Ok(imei)
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
        .fetch_one(&self.pool)
        .await?;

        Ok(row.get::<i64, _>("id"))
    }

    async fn get_device_vehicle_info(&self, device_id: i32) -> Result<(Option<i32>, i32)> {
        // Get company_id from gps_devices and vehicle_id from vehicles table
        let row = sqlx::query(
            r#"
            SELECT d.company_id, v.id as vehicle_id
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
            Ok((vehicle_id, company_id))
        } else {
            Ok((None, 1)) // Default company_id = 1
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
                vehicle_id,
                type,
                severity,
                g_force,
                speed_kph,
                latitude,
                longitude,
                timestamp,
                company_id,
                created_at,
                updated_at
            ) VALUES (
                $1, $2, $3, $4, $5, $6, $7, $8, $9, NOW(), NOW()
            )
            RETURNING id
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

    async fn insert_trip(&self, trip: &crate::services::trip_detector::CompletedTrip) -> Result<i64> {
        let row = sqlx::query(
            r#"
            INSERT INTO trips (
                vehicle_id,
                driver_id,
                start_time,
                end_time,
                start_latitude,
                start_longitude,
                end_latitude,
                end_longitude,
                distance_km,
                duration_minutes,
                average_speed_kph,
                max_speed_kph,
                fuel_consumed_liters,
                start_odometer_km,
                end_odometer_km,
                harsh_braking_count,
                harsh_acceleration_count,
                overspeeding_count,
                status,
                company_id,
                created_at,
                updated_at
            ) VALUES (
                $1, NULL, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19, NOW(), NOW()
            )
            RETURNING id
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
}

impl Database {
    pub fn new(pool: PgPool, osrm_base_url: String) -> Self {
        Self { 
            pool,
            gap_filler: GapFiller::new(osrm_base_url),
        }
    }

    async fn ensure_device(
        &self,
        device_uid: &str,
        protocol_type: &str,
        metadata: ProtocolMetadata,
    ) -> Result<i32> {
        // Use gps_devices table with EF Core column naming (PascalCase)
        // Default CompanyId = 1 (Belive) for testing
        const DEFAULT_COMPANY_ID: i32 = 1; // Belive company
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
            RETURNING id
            "#,
        )
        .bind(device_uid)
        .bind(protocol_type)
        .bind(model)
        .bind(firmware)
        .bind(DEFAULT_COMPANY_ID)
        .fetch_one(&self.pool)
        .await?;

        Ok(row.get::<i32, _>("id"))
    }

    async fn insert_position(&self, device_id: i32, frame: &HhFrame, event_key: &str) -> Result<i64> {
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

        // Temperature: raw value needs conversion (value - 40 for Celsius)
        let temperature_c: Option<i16> = if frame.temperature_raw > 0 {
            Some((frame.temperature_raw as i16).saturating_sub(40))
        } else {
            None
        };

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
                address
            ) VALUES (
                $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, NOW(),
                $16, $17, $18, $19, $20, $21, $22, $23, $24
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
        .bind(i32::from(frame.fuel_raw))
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
        .bind(frame.odometer_km as i64)
        .bind(frame.rpm.map(|r| r as i16))
        .bind(frame.send_flag as i16)
        .bind(protocol_version)
        .bind(&frame.address) // Geocoded address
        .fetch_optional(&self.pool)
        .await?;

        Ok(row.map(|r| r.get::<i64, _>("id")).unwrap_or(0))
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
