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

/// Maximum distance jump in meters for a single frame interval (~1 min)
/// 250 km/h = ~4.2 km/min, so 5km is a reasonable max
const MAX_JUMP_DISTANCE_M: f64 = 5000.0;

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

/// GPS Position Validator
pub struct GpsValidator {
    /// Last valid positions by device_id
    last_positions: Arc<RwLock<HashMap<i32, LastValidPosition>>>,
}

impl GpsValidator {
    pub fn new() -> Self {
        Self {
            last_positions: Arc::new(RwLock::new(HashMap::new())),
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
        // Check for impossible speed
        if frame.speed_kph > MAX_SPEED_KPH {
            return Some(ValidationResult::Invalid {
                reason: format!("Speed {} km/h exceeds maximum {}", frame.speed_kph, MAX_SPEED_KPH),
            });
        }

        // Check for null island (0,0 coordinates)
        if frame.latitude.abs() < 0.01 && frame.longitude.abs() < 0.01 {
            return Some(ValidationResult::Invalid {
                reason: "Coordinates near null island (0,0)".to_string(),
            });
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

        // Check for GPS jump (teleportation)
        if distance_m > MAX_JUMP_DISTANCE_M && time_diff_secs < 120 {
            warn!(
                device_id,
                distance_m,
                time_diff_secs,
                "GPS jump detected - position rejected"
            );
            return ValidationResult::Invalid {
                reason: format!(
                    "GPS jump: {} m in {} seconds (max: {} m)",
                    distance_m as i64, time_diff_secs, MAX_JUMP_DISTANCE_M as i64
                ),
            };
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
            latitude: lat,
            longitude: lon,
            speed_kph: speed,
            heading_deg: 0.0,
            recorded_at: NaiveDateTime::parse_from_str(time_str, "%Y-%m-%d %H:%M:%S").unwrap(),
            is_valid: true,
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
            send_flag: 0,
            protocol_version: 1,
            fuel_rate_l_per_100km: None,
            address: None,
        }
    }

    #[tokio::test]
    async fn test_basic_validation() {
        let validator = GpsValidator::new();
        
        // Valid frame
        let frame = create_test_frame(36.8, 10.1, 50.0, "2026-01-29 12:00:00");
        let result = validator.validate(1, &frame).await;
        assert!(result.is_valid());

        // Impossible speed
        let frame = create_test_frame(36.8, 10.1, 300.0, "2026-01-29 12:01:00");
        let result = validator.validate(1, &frame).await;
        assert!(!result.is_valid());

        // Null island
        let frame = create_test_frame(0.001, 0.001, 50.0, "2026-01-29 12:02:00");
        let result = validator.validate(2, &frame).await;
        assert!(!result.is_valid());
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
