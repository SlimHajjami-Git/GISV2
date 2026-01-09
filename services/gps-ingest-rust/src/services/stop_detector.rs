//! Vehicle Stop Detection Service
//! 
//! Detects when a vehicle stops (ignition off or stationary) and tracks stop duration.

use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;
use chrono::{DateTime, Utc, Duration};
use tracing::{info, debug};

use crate::telemetry::model::HhFrame;

/// Minimum stop duration to be recorded (in seconds)
const MIN_STOP_DURATION_SECS: i64 = 60;

/// Speed threshold to consider vehicle stopped (km/h)
const STOP_SPEED_THRESHOLD: f64 = 2.0;

/// Stop state for a device
#[derive(Debug, Clone)]
pub struct StopState {
    pub device_id: i32,
    pub start_time: DateTime<Utc>,
    pub latitude: f64,
    pub longitude: f64,
    pub ignition_off: bool,
    pub fuel_level_start: Option<i32>,
    pub start_mileage: Option<u32>,
}

/// Completed stop record ready for database insertion
#[derive(Debug, Clone)]
pub struct CompletedStop {
    pub device_id: i32,
    pub start_time: DateTime<Utc>,
    pub end_time: DateTime<Utc>,
    pub duration_seconds: i64,
    pub latitude: f64,
    pub longitude: f64,
    pub stop_type: StopType,
    pub ignition_off: bool,
    pub fuel_level_start: Option<i32>,
    pub fuel_level_end: Option<i32>,
    pub start_mileage: Option<u32>,
    pub end_mileage: Option<u32>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StopType {
    Parking,    // Ignition off, long duration
    Traffic,    // Ignition on, short duration
    Delivery,   // Medium duration, specific location
    Unknown,
}

impl StopType {
    pub fn as_str(&self) -> &'static str {
        match self {
            StopType::Parking => "parking",
            StopType::Traffic => "traffic",
            StopType::Delivery => "delivery",
            StopType::Unknown => "unknown",
        }
    }
}

/// Service for detecting vehicle stops
pub struct StopDetector {
    /// Current stop states by device_id
    active_stops: Arc<RwLock<HashMap<i32, StopState>>>,
}

impl StopDetector {
    pub fn new() -> Self {
        Self {
            active_stops: Arc::new(RwLock::new(HashMap::new())),
        }
    }

    /// Process a frame and detect stop start/end
    /// Returns Some(CompletedStop) if a stop just ended
    pub async fn process_frame(
        &self,
        device_id: i32,
        frame: &HhFrame,
    ) -> Option<CompletedStop> {
        let is_stopped = frame.speed_kph < STOP_SPEED_THRESHOLD;
        let ignition_off = !frame.ignition_on;
        let now = DateTime::<Utc>::from_naive_utc_and_offset(frame.recorded_at, Utc);

        let mut stops = self.active_stops.write().await;

        if is_stopped || ignition_off {
            // Vehicle is stopped or ignition off
            if let Some(existing) = stops.get(&device_id) {
                // Already tracking a stop, update if ignition changed
                debug!(device_id, "Vehicle still stopped");
                None
            } else {
                // Start tracking new stop
                let state = StopState {
                    device_id,
                    start_time: now,
                    latitude: frame.latitude,
                    longitude: frame.longitude,
                    ignition_off,
                    fuel_level_start: Some(frame.fuel_raw as i32),
                    start_mileage: Some(frame.odometer_km),
                };
                stops.insert(device_id, state);
                info!(device_id, ignition_off, "Vehicle stop started");
                None
            }
        } else {
            // Vehicle is moving
            if let Some(stop_state) = stops.remove(&device_id) {
                // Stop just ended
                let duration = now.signed_duration_since(stop_state.start_time);
                let duration_secs = duration.num_seconds();

                if duration_secs >= MIN_STOP_DURATION_SECS {
                    let stop_type = Self::classify_stop(
                        duration_secs,
                        stop_state.ignition_off,
                    );

                    let completed = CompletedStop {
                        device_id,
                        start_time: stop_state.start_time,
                        end_time: now,
                        duration_seconds: duration_secs,
                        latitude: stop_state.latitude,
                        longitude: stop_state.longitude,
                        stop_type,
                        ignition_off: stop_state.ignition_off,
                        fuel_level_start: stop_state.fuel_level_start,
                        fuel_level_end: Some(frame.fuel_raw as i32),
                        start_mileage: stop_state.start_mileage,
                        end_mileage: Some(frame.odometer_km),
                    };

                    info!(
                        device_id,
                        duration_secs,
                        stop_type = stop_type.as_str(),
                        "Vehicle stop completed"
                    );

                    return Some(completed);
                } else {
                    debug!(device_id, duration_secs, "Stop too short, ignoring");
                }
            }
            None
        }
    }

    /// Classify the type of stop based on duration and ignition state
    fn classify_stop(duration_secs: i64, ignition_off: bool) -> StopType {
        if ignition_off {
            if duration_secs >= 300 {
                // >= 5 minutes with ignition off = parking
                StopType::Parking
            } else if duration_secs >= 60 {
                // 1-5 minutes with ignition off = short parking
                StopType::Parking
            } else {
                StopType::Unknown
            }
        } else {
            if duration_secs < 180 {
                // < 3 minutes with ignition on = traffic
                StopType::Traffic
            } else if duration_secs < 900 {
                // 3-15 minutes = delivery
                StopType::Delivery
            } else {
                StopType::Parking
            }
        }
    }

    /// Get count of active stops being tracked
    pub async fn active_stop_count(&self) -> usize {
        self.active_stops.read().await.len()
    }
}

impl Default for StopDetector {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::NaiveDateTime;
    use crate::telemetry::model::{FrameKind, FrameVersion};

    fn make_frame(speed: f64, ignition: bool, time_str: &str) -> HhFrame {
        let recorded_at = NaiveDateTime::parse_from_str(time_str, "%Y-%m-%d %H:%M:%S").unwrap();
        HhFrame {
            kind: FrameKind::RealTime,
            version: FrameVersion::V3,
            recorded_at,
            latitude: 36.8,
            longitude: 10.1,
            speed_kph: speed,
            heading_deg: 0.0,
            power_voltage: 12,
            power_source_rescue: false,
            fuel_raw: 50,
            ignition_on: ignition,
            mems_x: 0,
            mems_y: 0,
            mems_z: 0,
            temperature_raw: 80,
            odometer_km: 10000,
            send_flag: 0,
            added_info: 0,
            signal_quality: Some(20),
            satellites_in_view: Some(10),
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
    async fn test_stop_detection() {
        let detector = StopDetector::new();

        // Vehicle stops
        let frame1 = make_frame(0.0, false, "2025-01-01 10:00:00");
        let result = detector.process_frame(1, &frame1).await;
        assert!(result.is_none()); // Stop started, not completed

        // Vehicle still stopped after 2 minutes
        let frame2 = make_frame(0.0, false, "2025-01-01 10:02:00");
        let result = detector.process_frame(1, &frame2).await;
        assert!(result.is_none());

        // Vehicle starts moving
        let frame3 = make_frame(50.0, true, "2025-01-01 10:05:00");
        let result = detector.process_frame(1, &frame3).await;
        assert!(result.is_some());

        let stop = result.unwrap();
        assert_eq!(stop.duration_seconds, 300); // 5 minutes
        assert_eq!(stop.stop_type, StopType::Parking);
    }
}
