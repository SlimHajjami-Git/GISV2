//! GPS Position Validator Service
//!
//! Validates GPS positions for quality and coherence before storage.
//! Detects and filters:
//! - Aberrant points (GPS jumps/teleportation)
//! - Speed incoherence (reported vs calculated speed mismatch)
//! - Invalid coordinates
//! - Unrealistic movements

use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;
use chrono::{DateTime, Utc};
use tracing::{info, warn, debug};

use crate::telemetry::model::HhFrame;

/// Maximum allowed speed (km/h) - anything above is rejected
const MAX_SPEED_KPH: f64 = 250.0;

/// Maximum realistic odometer for a fleet vehicle (500,000 km).
/// Anything above is likely a GPS firmware artifact (e.g. 0x0FFFFE = 1048574).
const MAX_REALISTIC_ODOMETER_KM: u32 = 500_000;

/// Maximum distance jump in meters for a single frame interval (~1 min)
/// 250 km/h = ~4.2 km/min, so 5km is a reasonable max
const MAX_JUMP_DISTANCE_M: f64 = 5000.0;

/// Maximum implied average speed (km/h) between two consecutive frames of the
/// same device. Replaces the former MAX_ABSOLUTE_JUMP_DISTANCE_M rule: now the
/// threshold scales naturally with the time gap, so devices that reconnect
/// after long offline periods (e.g. 3h+ coverage loss on long-haul trips) are
/// no longer wrongly rejected just because their last known position is far.
///
/// Examples:
///   - 200 km in  1 h   → 200 km/h → REJECTED (physically suspect)
///   - 200 km in  6 h   →  33 km/h → ACCEPTED (device was offline, resync)
///   - 500 km in 10 min → 3000 km/h → REJECTED (teleportation)
///
/// Set at 1.5× MAX_SPEED_KPH to allow for missed intermediate frames during
/// short bursts of fast driving.
const MAX_IMPLIED_AVG_SPEED_KPH: f64 = 375.0; // == MAX_SPEED_KPH * 1.5

/// When the gap between two frames exceeds this threshold we consider it an
/// extended reconnection and emit an INFO log for observability. The frame is
/// still validated against MAX_IMPLIED_AVG_SPEED_KPH so spoofing attempts
/// remain blocked, but the log lets operators spot "long offline → resumed"
/// events in the pipeline.
const RESYNC_LOG_THRESHOLD_SECS: i64 = 3600; // 1 h

/// Minimum number of satellites for a reliable GPS fix.
/// 3 satellites = 2D fix (no altitude), 4 = 3D fix.
/// Below 3, the position is unreliable and should be rejected.
const MIN_SATELLITES_FOR_VALID_FIX: u8 = 3;

/// Geographic bounding box for operational area (Tunisia only).
/// Any position outside this box is rejected as GPS noise or hardware glitch.
/// Small margins around Tunisia's actual borders for GPS accuracy.
const GEO_BOUNDS_LAT_MIN: f64 = 30.0;  // South (Borj El Khadra ~30.2°)
const GEO_BOUNDS_LAT_MAX: f64 = 37.6;  // North (Cap Angela ~37.35°)
const GEO_BOUNDS_LON_MIN: f64 = 7.4;   // West  (Nefta/Tozeur ~7.5°)
const GEO_BOUNDS_LON_MAX: f64 = 11.7;  // East  (Ras Ajdir ~11.6°)

/// Speed tolerance for coherence check (percentage)
/// Allows 100% difference between reported and calculated speed
const SPEED_COHERENCE_TOLERANCE: f64 = 1.0;

/// Minimum time between frames to perform speed coherence check (seconds)
const MIN_TIME_FOR_SPEED_CHECK: i64 = 10;

/// Maximum time gap to consider for speed coherence (seconds)
/// Beyond this, we don't validate speed coherence as the vehicle could have stopped
const MAX_TIME_FOR_SPEED_CHECK: i64 = 300; // 5 minutes

/// Last known valid position for coherence checking
#[derive(Debug, Clone)]
pub struct LastValidPosition {
    pub latitude: f64,
    pub longitude: f64,
    pub speed_kph: f64,
    pub timestamp: DateTime<Utc>,
}

/// Validation result
#[derive(Debug, Clone)]
pub enum ValidationResult {
    /// Position is valid
    Valid,
    /// Position is valid but was corrected
    ValidCorrected { reason: String },
    /// Position should be skipped (soft reject)
    Skip { reason: String },
    /// Position is invalid and should be rejected (hard reject)
    Invalid { reason: String },
}

impl ValidationResult {
    pub fn is_valid(&self) -> bool {
        matches!(self, ValidationResult::Valid | ValidationResult::ValidCorrected { .. })
    }
    
    pub fn should_store(&self) -> bool {
        matches!(self, ValidationResult::Valid | ValidationResult::ValidCorrected { .. })
    }
}

/// Operational-area bounding box, configurable per deployment via environment
/// variables so the same binary serves Tunisia (default) and Algeria (Bougeo)
/// without rejecting out-of-Tunisia positions. GISV1 had no such geo-filter;
/// this keeps the noise-rejection benefit while staying multi-country.
#[derive(Debug, Clone, Copy)]
struct GeoBounds {
    lat_min: f64,
    lat_max: f64,
    lon_min: f64,
    lon_max: f64,
}

impl GeoBounds {
    fn from_env() -> Self {
        fn env_f64(key: &str, default: f64) -> f64 {
            std::env::var(key)
                .ok()
                .and_then(|v| v.trim().parse::<f64>().ok())
                .unwrap_or(default)
        }
        Self {
            lat_min: env_f64("GEO_BOUNDS_LAT_MIN", GEO_BOUNDS_LAT_MIN),
            lat_max: env_f64("GEO_BOUNDS_LAT_MAX", GEO_BOUNDS_LAT_MAX),
            lon_min: env_f64("GEO_BOUNDS_LON_MIN", GEO_BOUNDS_LON_MIN),
            lon_max: env_f64("GEO_BOUNDS_LON_MAX", GEO_BOUNDS_LON_MAX),
        }
    }
}

/// GPS Position Validator
pub struct GpsValidator {
    /// Last valid positions by device_id
    last_positions: Arc<RwLock<HashMap<i32, LastValidPosition>>>,
    /// Operational-area bounds (env-configurable per deployment).
    geo: GeoBounds,
}

impl GpsValidator {
    pub fn new() -> Self {
        let geo = GeoBounds::from_env();
        info!(
            lat_min = geo.lat_min, lat_max = geo.lat_max,
            lon_min = geo.lon_min, lon_max = geo.lon_max,
            "GPS validator operational-area bounds loaded"
        );
        Self {
            last_positions: Arc::new(RwLock::new(HashMap::new())),
            geo,
        }
    }

    /// Validate a GPS frame against coherence rules
    pub async fn validate(
        &self,
        device_id: i32,
        frame: &HhFrame,
    ) -> ValidationResult {
        let now = DateTime::<Utc>::from_naive_utc_and_offset(frame.recorded_at, Utc);
        
        // Basic validation (already done in transport.rs, but double-check)
        if let Some(result) = self.validate_basic(frame) {
            return result;
        }

        // Get last valid position for this device
        let last_pos_opt = {
            let positions = self.last_positions.read().await;
            positions.get(&device_id).cloned()
        };

        let result = if let Some(last_pos) = last_pos_opt {
            self.validate_coherence(device_id, frame, &last_pos, now)
        } else {
            // First position for this device - accept it
            ValidationResult::Valid
        };

        // If valid, update last position
        if result.should_store() {
            let mut positions = self.last_positions.write().await;
            positions.insert(device_id, LastValidPosition {
                latitude: frame.latitude,
                longitude: frame.longitude,
                speed_kph: frame.speed_kph,
                timestamp: now,
            });
        }

        result
    }

    /// Basic validation checks
    fn validate_basic(&self, frame: &HhFrame) -> Option<ValidationResult> {
        // Check Frame Validity bit (Bit 7 / 0x40 in flags byte)
        // When GPS module has no valid satellite fix, coordinates are stale/unreliable.
        // This is the primary cause of vehicles appearing in wrong locations (e.g., lakes).
        if !frame.is_valid {
            return Some(ValidationResult::Invalid {
                reason: format!(
                    "Frame Validity bit is FALSE (flags=0x{:02X}) - GPS has no valid fix, coordinates unreliable",
                    frame.flags_raw
                ),
            });
        }

        // Check satellite count: minimum 3 for a 2D fix
        // With fewer satellites, trilateration fails and position is meaningless.
        if let Some(sats) = frame.satellites_in_view {
            if sats < MIN_SATELLITES_FOR_VALID_FIX {
                return Some(ValidationResult::Invalid {
                    reason: format!(
                        "Only {} satellites in view (minimum {} required for valid fix)",
                        sats, MIN_SATELLITES_FOR_VALID_FIX
                    ),
                });
            }
        }

        // Check for impossible speed
        if frame.speed_kph > MAX_SPEED_KPH {
            return Some(ValidationResult::Invalid {
                reason: format!("Speed {} km/h exceeds maximum {}", frame.speed_kph, MAX_SPEED_KPH),
            });
        }

        // Geographic bounding box: reject positions outside operational area
        // Catches null-island (0,0), GPS noise in wrong hemisphere, and hardware glitches
        if frame.latitude < self.geo.lat_min || frame.latitude > self.geo.lat_max
            || frame.longitude < self.geo.lon_min || frame.longitude > self.geo.lon_max
        {
            return Some(ValidationResult::Invalid {
                reason: format!(
                    "Position ({:.6}, {:.6}) outside operational area [lat {}-{}, lon {}-{}]",
                    frame.latitude, frame.longitude,
                    self.geo.lat_min, self.geo.lat_max,
                    self.geo.lon_min, self.geo.lon_max
                ),
            });
        }

        // Odometer sanity check: detect firmware sentinel values or unrealistic readings
        // 0x0FFFFE (1048574) is a known NEMS sentinel for "no CAN bus odometer"
        // Values > 500,000 km are unrealistic for fleet vehicles
        if frame.odometer_km > MAX_REALISTIC_ODOMETER_KM {
            warn!(
                odometer_km = frame.odometer_km,
                "Odometer value {} km exceeds maximum realistic {} km — likely firmware artifact",
                frame.odometer_km, MAX_REALISTIC_ODOMETER_KM
            );
            // Don't reject the frame (position data may still be valid),
            // but downstream should treat odometer_km as unreliable.
            // The parser should have already filtered known sentinels to 0.
        }

        None
    }

    /// Validate coherence with previous position
    fn validate_coherence(
        &self,
        device_id: i32,
        frame: &HhFrame,
        last_pos: &LastValidPosition,
        now: DateTime<Utc>,
    ) -> ValidationResult {
        let time_diff_secs = (now - last_pos.timestamp).num_seconds();
        
        // Skip coherence check if time diff is too small or too large
        if time_diff_secs < MIN_TIME_FOR_SPEED_CHECK {
            return ValidationResult::Valid;
        }

        // Calculate distance from last position
        let distance_m = haversine_distance(
            last_pos.latitude, last_pos.longitude,
            frame.latitude, frame.longitude,
        );

        // Check for GPS jump (teleportation) — two layered rules:
        //   1. Short-gap burst: > 5 km in < 2 min (physically impossible acceleration).
        //   2. Implied avg speed: distance / time > 375 km/h at any gap.
        //      This scales with time, replacing the old 200km-absolute rule that
        //      wrongly rejected legitimate reconnections after long offline periods.
        if distance_m > MAX_JUMP_DISTANCE_M && time_diff_secs < 120 {
            warn!(
                device_id,
                distance_m,
                time_diff_secs,
                threshold = "short-gap",
                "GPS jump detected - position rejected"
            );
            return ValidationResult::Invalid {
                reason: format!(
                    "GPS short-gap jump: {:.0} m in {} seconds (threshold: 5km within 2min)",
                    distance_m, time_diff_secs
                ),
            };
        }

        let time_diff_hours = time_diff_secs as f64 / 3600.0;
        let implied_avg_speed_kph = (distance_m / 1000.0) / time_diff_hours;

        if implied_avg_speed_kph > MAX_IMPLIED_AVG_SPEED_KPH {
            warn!(
                device_id,
                distance_m,
                time_diff_secs,
                implied_avg_speed_kph,
                threshold = "implied-speed",
                "GPS jump detected - position rejected"
            );
            return ValidationResult::Invalid {
                reason: format!(
                    "GPS jump: {:.0} m in {} s implies {:.0} km/h avg (max {:.0} km/h)",
                    distance_m, time_diff_secs,
                    implied_avg_speed_kph, MAX_IMPLIED_AVG_SPEED_KPH
                ),
            };
        }

        // Extended reconnection: accept but log for observability.
        if time_diff_secs >= RESYNC_LOG_THRESHOLD_SECS {
            info!(
                device_id,
                distance_m,
                time_diff_hours,
                implied_avg_speed_kph,
                "Device reconnection after extended offline - position jump accepted"
            );
        }

        // Speed coherence check (only if time gap is reasonable)
        if time_diff_secs <= MAX_TIME_FOR_SPEED_CHECK {
            let calculated_speed_kph = (distance_m / 1000.0) / (time_diff_secs as f64 / 3600.0);
            let reported_speed = frame.speed_kph;
            
            // Only check if both speeds are significant
            if calculated_speed_kph > 5.0 && reported_speed > 5.0 {
                let speed_diff_ratio = (calculated_speed_kph - reported_speed).abs() / calculated_speed_kph.max(reported_speed);
                
                if speed_diff_ratio > SPEED_COHERENCE_TOLERANCE {
                    // Log but don't reject - just note the inconsistency
                    debug!(
                        device_id,
                        calculated_speed_kph,
                        reported_speed,
                        speed_diff_ratio,
                        "Speed incoherence detected"
                    );
                    // We don't reject on speed incoherence, just log it
                    // This could be due to stops during the interval
                }
            }

            // Check for impossible speed based on distance/time
            if calculated_speed_kph > MAX_SPEED_KPH * 1.5 {
                warn!(
                    device_id,
                    calculated_speed_kph,
                    distance_m,
                    time_diff_secs,
                    "Calculated speed impossibly high - likely GPS error"
                );
                return ValidationResult::Invalid {
                    reason: format!(
                        "Calculated speed {} km/h too high (distance: {} m, time: {} s)",
                        calculated_speed_kph as i64, distance_m as i64, time_diff_secs
                    ),
                };
            }
        }

        ValidationResult::Valid
    }

    /// Clear cached position for a device (e.g., when device reconnects)
    pub async fn clear_device(&self, device_id: i32) {
        let mut positions = self.last_positions.write().await;
        positions.remove(&device_id);
    }
}

/// Calculate Haversine distance between two coordinates in meters
fn haversine_distance(lat1: f64, lon1: f64, lat2: f64, lon2: f64) -> f64 {
    const EARTH_RADIUS_M: f64 = 6_371_000.0;

    let lat1_rad = lat1.to_radians();
    let lat2_rad = lat2.to_radians();
    let dlat = (lat2 - lat1).to_radians();
    let dlon = (lon2 - lon1).to_radians();

    let a = (dlat / 2.0).sin().powi(2)
        + lat1_rad.cos() * lat2_rad.cos() * (dlon / 2.0).sin().powi(2);
    let c = 2.0 * a.sqrt().asin();

    EARTH_RADIUS_M * c
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::NaiveDateTime;

    fn create_test_frame(lat: f64, lon: f64, speed: f64, time_str: &str) -> HhFrame {
        HhFrame {
            kind: crate::telemetry::model::FrameKind::RealTimeAndHistory,
            version: crate::telemetry::model::FrameVersion::V1,
            latitude: lat,
            longitude: lon,
            speed_kph: speed,
            heading_deg: 0.0,
            recorded_at: NaiveDateTime::parse_from_str(time_str, "%Y-%m-%d %H:%M:%S").unwrap(),
            is_valid: true,
            is_real_time: true,
            ignition_on: true,
            power_voltage: 12,
            power_source_rescue: false,
            fuel_raw: 50,
            mems_x: 0,
            mems_y: 0,
            mems_z: 0,
            temperature_raw: 0,
            odometer_km: 0,
            rpm: None,
            fuel_rate_l_per_100km: None,
            fms_temperature_c: None,
            send_flag: 0,
            added_info: 0,
            signal_quality: None,
            satellites_in_view: None,
            flags_raw: 0,
            raw_payload: String::new(),
            remaining_payload: None,
            address: None,
        }
    }

    #[tokio::test]
    async fn test_basic_validation() {
        let validator = GpsValidator::new();
        
        // Valid frame (Tunis)
        let frame = create_test_frame(36.8, 10.1, 50.0, "2026-01-29 12:00:00");
        let result = validator.validate(1, &frame).await;
        assert!(result.is_valid());

        // Impossible speed
        let frame = create_test_frame(36.8, 10.1, 300.0, "2026-01-29 12:01:00");
        let result = validator.validate(1, &frame).await;
        assert!(!result.is_valid());

        // Null island (0,0) - outside geo bounds
        let frame = create_test_frame(0.001, 0.001, 50.0, "2026-01-29 12:02:00");
        let result = validator.validate(2, &frame).await;
        assert!(!result.is_valid());
    }

    #[tokio::test]
    async fn test_geo_bounds_rejection() {
        let validator = GpsValidator::new();

        // Algeria (lon=4 < 7.4) - outside Tunisia
        let frame = create_test_frame(36.798, 4.015, 8.0, "2026-01-29 12:00:00");
        let result = validator.validate(10, &frame).await;
        assert!(!result.is_valid(), "Should reject Algeria position (lon<7.4)");

        // Partial null-island (lat=-0, lon=-0.98)
        let frame = create_test_frame(-0.0, -0.983, 0.0, "2026-01-29 12:01:00");
        let result = validator.validate(11, &frame).await;
        assert!(!result.is_valid(), "Should reject partial null-island");

        // Italy/sea (lat=39.6 > 37.6) - outside Tunisia
        let frame = create_test_frame(39.597, 10.200, 9.6, "2026-01-29 12:02:00");
        let result = validator.validate(12, &frame).await;
        assert!(!result.is_valid(), "Should reject lat>37.6");

        // Valid position in Sfax
        let frame = create_test_frame(34.74, 10.76, 40.0, "2026-01-29 12:03:00");
        let result = validator.validate(13, &frame).await;
        assert!(result.is_valid(), "Should accept Sfax position");

        // Valid position at Ras Jedir border (lon ~11.5)
        let frame = create_test_frame(33.16, 11.55, 60.0, "2026-01-29 12:04:00");
        let result = validator.validate(14, &frame).await;
        assert!(result.is_valid(), "Should accept Ras Jedir border position");
    }

    #[tokio::test]
    async fn test_implied_speed_rejects_fast_teleport() {
        let validator = GpsValidator::new();

        // Establish position in Tunis.
        let frame1 = create_test_frame(36.8, 10.1, 50.0, "2026-01-29 12:00:00");
        validator.validate(20, &frame1).await;

        // 10 minutes later, jump to Sfax (~250 km away).
        // Implied avg speed: 250 km / (10/60) h = 1500 km/h → rejected.
        let frame2 = create_test_frame(34.74, 10.76, 50.0, "2026-01-29 12:10:00");
        let result = validator.validate(20, &frame2).await;
        assert!(!result.is_valid(), "Should reject 250 km jump in 10 min (1500 km/h implied)");
    }

    #[tokio::test]
    async fn test_long_offline_reconnection_accepted() {
        let validator = GpsValidator::new();

        // Establish position in Tunis at noon.
        let frame1 = create_test_frame(36.8, 10.1, 50.0, "2026-01-29 12:00:00");
        validator.validate(21, &frame1).await;

        // Device offline for 4 hours, reappears in Sfax (~250 km away).
        // Implied avg speed: 250 / 4 ≈ 62.5 km/h → realistic highway average,
        // should be ACCEPTED. Under the old 200 km absolute rule this was wrongly
        // rejected and caused "stuck on old position" symptoms in the monitoring UI.
        let frame2 = create_test_frame(34.74, 10.76, 50.0, "2026-01-29 16:00:00");
        let result = validator.validate(21, &frame2).await;
        assert!(result.is_valid(), "Should accept 250 km jump after 4 h offline (62 km/h implied)");
    }

    #[tokio::test]
    async fn test_implied_speed_rejects_slow_teleport() {
        let validator = GpsValidator::new();

        // Establish position in Tunis.
        let frame1 = create_test_frame(36.8, 10.1, 50.0, "2026-01-29 12:00:00");
        validator.validate(22, &frame1).await;

        // 1 hour later, device reports being ~400 km south (still within Tunisia bounds).
        // Implied avg speed: 400 km / 1 h = 400 km/h → > 375 km/h max → rejected.
        let frame2 = create_test_frame(33.2, 10.1, 50.0, "2026-01-29 13:00:00");
        let result = validator.validate(22, &frame2).await;
        assert!(!result.is_valid(), "Should reject 400 km jump in 1 h (400 km/h implied > 375 max)");
    }

    #[tokio::test]
    async fn test_frame_validity_bit_rejection() {
        let validator = GpsValidator::new();

        // Frame with is_valid = false (GPS has no fix) - should be REJECTED
        // This is the exact scenario: vehicle appears in Lac de Tunis because
        // GPS module reports stale coordinates when it has no satellite fix.
        let mut frame = create_test_frame(36.82, 10.23, 0.0, "2026-01-29 12:00:00");
        frame.is_valid = false;
        frame.flags_raw = 0x63; // Bit 7 (0x40) NOT set = invalid fix
        let result = validator.validate(30, &frame).await;
        assert!(!result.is_valid(), "Should reject frame with is_valid=false (no GPS fix)");

        // Same coordinates but with valid fix - should be ACCEPTED
        let mut frame_valid = create_test_frame(36.82, 10.23, 0.0, "2026-01-29 12:01:00");
        frame_valid.is_valid = true;
        frame_valid.flags_raw = 0xE7; // Bit 7 (0x40) SET = valid fix
        let result = validator.validate(31, &frame_valid).await;
        assert!(result.is_valid(), "Should accept frame with is_valid=true");
    }

    #[tokio::test]
    async fn test_satellite_count_rejection() {
        let validator = GpsValidator::new();

        // 0 satellites = no fix at all
        let mut frame = create_test_frame(36.8, 10.1, 50.0, "2026-01-29 12:00:00");
        frame.satellites_in_view = Some(0);
        let result = validator.validate(40, &frame).await;
        assert!(!result.is_valid(), "Should reject 0 satellites");

        // 2 satellites = insufficient for 2D fix
        let mut frame = create_test_frame(36.8, 10.1, 50.0, "2026-01-29 12:01:00");
        frame.satellites_in_view = Some(2);
        let result = validator.validate(41, &frame).await;
        assert!(!result.is_valid(), "Should reject 2 satellites");

        // 3 satellites = minimum 2D fix, acceptable
        let mut frame = create_test_frame(36.8, 10.1, 50.0, "2026-01-29 12:02:00");
        frame.satellites_in_view = Some(3);
        let result = validator.validate(42, &frame).await;
        assert!(result.is_valid(), "Should accept 3 satellites (2D fix)");

        // No satellite info (None) = don't reject (older firmware may not report)
        let frame = create_test_frame(36.8, 10.1, 50.0, "2026-01-29 12:03:00");
        let result = validator.validate(43, &frame).await;
        assert!(result.is_valid(), "Should accept when satellite count unknown (None)");

        // 12 satellites = excellent fix
        let mut frame = create_test_frame(36.8, 10.1, 50.0, "2026-01-29 12:04:00");
        frame.satellites_in_view = Some(12);
        let result = validator.validate(44, &frame).await;
        assert!(result.is_valid(), "Should accept 12 satellites");
    }

    #[tokio::test]
    async fn test_lac_de_tunis_scenario() {
        // Real-world scenario: vehicle near Lac de Tunis with invalid GPS fix
        // GPS reports coordinates IN the lake because fix is stale/inaccurate
        let validator = GpsValidator::new();

        // Establish a valid position on road near the lake
        let frame1 = create_test_frame(36.78, 10.21, 60.0, "2026-01-29 12:00:00");
        let result = validator.validate(50, &frame1).await;
        assert!(result.is_valid(), "Initial valid position should be accepted");

        // GPS loses fix (enters tunnel, near water reflection, etc.)
        // Reports stale position IN the lake with is_valid=false
        let mut frame_lake = create_test_frame(36.795, 10.215, 0.0, "2026-01-29 12:01:00");
        frame_lake.is_valid = false;
        frame_lake.flags_raw = 0x63; // No validity bit
        frame_lake.satellites_in_view = Some(1); // Only 1 satellite
        let result = validator.validate(50, &frame_lake).await;
        assert!(!result.is_valid(), "Lake position with invalid fix should be REJECTED");

        // GPS regains fix, vehicle back on road
        let frame3 = create_test_frame(36.79, 10.22, 40.0, "2026-01-29 12:02:00");
        let result = validator.validate(50, &frame3).await;
        assert!(result.is_valid(), "Valid fix after recovery should be accepted");
    }

    #[tokio::test]
    async fn test_gps_jump_detection() {
        let validator = GpsValidator::new();
        
        // First position
        let frame1 = create_test_frame(36.8, 10.1, 50.0, "2026-01-29 12:00:00");
        validator.validate(1, &frame1).await;

        // Normal movement (1 minute later, ~1km away)
        let frame2 = create_test_frame(36.809, 10.1, 60.0, "2026-01-29 12:01:00");
        let result = validator.validate(1, &frame2).await;
        assert!(result.is_valid());

        // GPS jump (1 minute later, 100km away - impossible)
        let frame3 = create_test_frame(37.8, 10.1, 50.0, "2026-01-29 12:02:00");
        let result = validator.validate(1, &frame3).await;
        assert!(!result.is_valid());
    }
}
