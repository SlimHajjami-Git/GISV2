//! GPS Drift Stabilization Service
//! 
//! Prevents GPS drift when vehicles are stationary by keeping the last stable position.
//! This mimics GISV1 behavior where coordinates are stabilized when:
//! - Speed is 0 (or below threshold)
//! - Ignition is OFF
//! 
//! The service stores the "anchor" position when a vehicle stops and returns
//! that position for subsequent frames until the vehicle starts moving again.

use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;
use chrono::{DateTime, Utc};
use tracing::{info, debug};

use crate::telemetry::model::HhFrame;

/// Speed threshold below which vehicle is considered stopped (km/h)
/// Using 3 km/h to account for GPS noise
const STOPPED_SPEED_THRESHOLD: f64 = 3.0;

/// Maximum drift distance allowed when stopped (meters)
/// If new position is within this radius of anchor, use anchor position
const MAX_DRIFT_DISTANCE_METERS: f64 = 50.0;

/// Minimum distance to consider the vehicle has actually moved (meters)
/// This prevents small GPS jumps from being recorded as movement
const MIN_MOVEMENT_DISTANCE_METERS: f64 = 10.0;

/// Stable position anchor for a device
#[derive(Debug, Clone)]
pub struct StablePosition {
    pub latitude: f64,
    pub longitude: f64,
    pub timestamp: DateTime<Utc>,
    pub ignition_was_off: bool,
}

/// Result of GPS stabilization
#[derive(Debug, Clone)]
pub struct StabilizedFrame {
    pub latitude: f64,
    pub longitude: f64,
    pub was_stabilized: bool,
    pub drift_distance_meters: Option<f64>,
}

/// Service for stabilizing GPS coordinates when vehicles are stopped
pub struct GpsStabilizer {
    /// Stable positions by device_id
    anchors: Arc<RwLock<HashMap<i32, StablePosition>>>,
}

impl GpsStabilizer {
    pub fn new() -> Self {
        Self {
            anchors: Arc::new(RwLock::new(HashMap::new())),
        }
    }

    /// Process a frame and return stabilized coordinates
    /// 
    /// Logic:
    /// 1. If vehicle is moving (speed > threshold AND ignition on): clear anchor, use new position
    /// 2. If vehicle just stopped: set anchor to current position
    /// 3. If vehicle is stopped and has anchor: use anchor position (ignore drift)
    pub async fn stabilize(
        &self,
        device_id: i32,
        frame: &HhFrame,
    ) -> StabilizedFrame {
        let is_moving = frame.speed_kph > STOPPED_SPEED_THRESHOLD && frame.ignition_on;
        let is_stopped = frame.speed_kph <= STOPPED_SPEED_THRESHOLD || !frame.ignition_on;
        let now = DateTime::<Utc>::from_naive_utc_and_offset(frame.recorded_at, Utc);

        let mut anchors = self.anchors.write().await;

        if is_moving {
            // Vehicle is moving - check if we should clear anchor
            if let Some(anchor) = anchors.get(&device_id) {
                let distance = haversine_distance(
                    anchor.latitude, anchor.longitude,
                    frame.latitude, frame.longitude,
                );
                
                // Only clear anchor if vehicle has moved significantly
                if distance > MIN_MOVEMENT_DISTANCE_METERS {
                    debug!(
                        device_id,
                        distance_m = distance,
                        "Vehicle started moving, clearing GPS anchor"
                    );
                    anchors.remove(&device_id);
                }
            }
            
            // Use actual position when moving
            StabilizedFrame {
                latitude: frame.latitude,
                longitude: frame.longitude,
                was_stabilized: false,
                drift_distance_meters: None,
            }
        } else if is_stopped {
            // Vehicle is stopped
            if let Some(anchor) = anchors.get(&device_id) {
                // Already have an anchor - check drift distance
                let drift = haversine_distance(
                    anchor.latitude, anchor.longitude,
                    frame.latitude, frame.longitude,
                );

                if drift <= MAX_DRIFT_DISTANCE_METERS {
                    // Within drift threshold - use stable anchor position
                    debug!(
                        device_id,
                        drift_m = drift,
                        anchor_lat = anchor.latitude,
                        anchor_lon = anchor.longitude,
                        "GPS drift detected, using stable anchor position"
                    );
                    
                    StabilizedFrame {
                        latitude: anchor.latitude,
                        longitude: anchor.longitude,
                        was_stabilized: true,
                        drift_distance_meters: Some(drift),
                    }
                } else {
                    // Drift too large - might be a real position change or GPS jump
                    // Update anchor to new position
                    info!(
                        device_id,
                        drift_m = drift,
                        "Large position change while stopped, updating anchor"
                    );
                    
                    anchors.insert(device_id, StablePosition {
                        latitude: frame.latitude,
                        longitude: frame.longitude,
                        timestamp: now,
                        ignition_was_off: !frame.ignition_on,
                    });
                    
                    StabilizedFrame {
                        latitude: frame.latitude,
                        longitude: frame.longitude,
                        was_stabilized: false,
                        drift_distance_meters: Some(drift),
                    }
                }
            } else {
                // No anchor yet - set it now (vehicle just stopped)
                info!(
                    device_id,
                    lat = frame.latitude,
                    lon = frame.longitude,
                    speed = frame.speed_kph,
                    ignition = frame.ignition_on,
                    "Vehicle stopped, setting GPS anchor"
                );
                
                anchors.insert(device_id, StablePosition {
                    latitude: frame.latitude,
                    longitude: frame.longitude,
                    timestamp: now,
                    ignition_was_off: !frame.ignition_on,
                });
                
                StabilizedFrame {
                    latitude: frame.latitude,
                    longitude: frame.longitude,
                    was_stabilized: false,
                    drift_distance_meters: None,
                }
            }
        } else {
            // Edge case - use actual position
            StabilizedFrame {
                latitude: frame.latitude,
                longitude: frame.longitude,
                was_stabilized: false,
                drift_distance_meters: None,
            }
        }
    }

    /// Get current anchor for a device (for debugging)
    pub async fn get_anchor(&self, device_id: i32) -> Option<StablePosition> {
        self.anchors.read().await.get(&device_id).cloned()
    }

    /// Clear anchor for a device (e.g., on disconnect)
    pub async fn clear_anchor(&self, device_id: i32) {
        self.anchors.write().await.remove(&device_id);
    }

    /// Get number of active anchors
    pub async fn anchor_count(&self) -> usize {
        self.anchors.read().await.len()
    }
}

/// Calculate distance between two GPS points using Haversine formula
/// Returns distance in meters
fn haversine_distance(lat1: f64, lon1: f64, lat2: f64, lon2: f64) -> f64 {
    const EARTH_RADIUS_M: f64 = 6_371_000.0; // Earth radius in meters

    let lat1_rad = lat1.to_radians();
    let lat2_rad = lat2.to_radians();
    let delta_lat = (lat2 - lat1).to_radians();
    let delta_lon = (lon2 - lon1).to_radians();

    let a = (delta_lat / 2.0).sin().powi(2)
        + lat1_rad.cos() * lat2_rad.cos() * (delta_lon / 2.0).sin().powi(2);
    let c = 2.0 * a.sqrt().asin();

    EARTH_RADIUS_M * c
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::telemetry::model::{FrameKind, FrameVersion};
    use chrono::NaiveDateTime;

    fn create_test_frame(lat: f64, lon: f64, speed: f64, ignition: bool) -> HhFrame {
        HhFrame {
            kind: FrameKind::RealTime,
            version: FrameVersion::V1,
            recorded_at: NaiveDateTime::parse_from_str("2024-01-01 12:00:00", "%Y-%m-%d %H:%M:%S").unwrap(),
            latitude: lat,
            longitude: lon,
            speed_kph: speed,
            heading_deg: 0.0,
            power_voltage: 12,
            power_source_rescue: false,
            fuel_raw: 50,
            ignition_on: ignition,
            mems_x: 0,
            mems_y: 0,
            mems_z: 0,
            temperature_raw: 25,
            odometer_km: 1000,
            send_flag: 0,
            added_info: 0,
            signal_quality: None,
            satellites_in_view: None,
            rpm: None,
            is_valid: true,
            is_real_time: true,
            flags_raw: 0,
            raw_payload: String::new(),
            remaining_payload: None,
            address: None,
        }
    }

    #[tokio::test]
    async fn test_gps_stabilization_when_stopped() {
        let stabilizer = GpsStabilizer::new();
        let device_id = 1;

        // First frame - vehicle stops
        let frame1 = create_test_frame(36.8, 10.1, 0.0, false);
        let result1 = stabilizer.stabilize(device_id, &frame1).await;
        assert!(!result1.was_stabilized); // First frame sets anchor
        assert_eq!(result1.latitude, 36.8);

        // Second frame - small drift (1 meter)
        let frame2 = create_test_frame(36.80001, 10.10001, 0.0, false);
        let result2 = stabilizer.stabilize(device_id, &frame2).await;
        assert!(result2.was_stabilized); // Should use anchor
        assert_eq!(result2.latitude, 36.8); // Original anchor position
        assert_eq!(result2.longitude, 10.1);
    }

    #[tokio::test]
    async fn test_anchor_cleared_on_movement() {
        let stabilizer = GpsStabilizer::new();
        let device_id = 1;

        // Vehicle stops
        let frame1 = create_test_frame(36.8, 10.1, 0.0, false);
        stabilizer.stabilize(device_id, &frame1).await;
        
        // Vehicle moves significantly
        let frame2 = create_test_frame(36.81, 10.11, 50.0, true); // ~1.4km away
        let result = stabilizer.stabilize(device_id, &frame2).await;
        
        assert!(!result.was_stabilized);
        assert_eq!(result.latitude, 36.81); // New position
        
        // Anchor should be cleared
        assert!(stabilizer.get_anchor(device_id).await.is_none());
    }

    #[tokio::test]
    async fn test_haversine_distance() {
        // Paris to nearby point (~111m per 0.001 degree at equator)
        let distance = haversine_distance(48.8566, 2.3522, 48.8567, 2.3522);
        assert!(distance > 10.0 && distance < 20.0); // Should be about 11m
        
        // Same point
        let distance_zero = haversine_distance(36.8, 10.1, 36.8, 10.1);
        assert!(distance_zero < 0.001);
    }
}
