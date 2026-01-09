//! Fuel Tracking Service
//! 
//! Tracks fuel consumption, detects refueling events and anomalies.

use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;
use chrono::{DateTime, Utc};
use tracing::{info, warn, debug};

use crate::telemetry::model::HhFrame;

/// Minimum fuel change to detect refuel (%)
const REFUEL_THRESHOLD_PERCENT: i16 = 10;

/// Maximum normal consumption per reading (%)
const MAX_NORMAL_CONSUMPTION_PERCENT: i16 = 5;

/// Fuel state for a device
#[derive(Debug, Clone)]
pub struct FuelState {
    pub device_id: i32,
    pub last_fuel_percent: i16,
    pub last_odometer_km: u32,
    pub last_timestamp: DateTime<Utc>,
}

/// Fuel event to be recorded
#[derive(Debug, Clone)]
pub struct FuelEvent {
    pub device_id: i32,
    pub recorded_at: DateTime<Utc>,
    pub fuel_percent: i16,
    pub fuel_change: i16,
    pub odometer_km: u32,
    pub latitude: f64,
    pub longitude: f64,
    pub speed_kph: f64,
    pub rpm: Option<u16>,
    pub ignition_on: bool,
    pub event_type: FuelEventType,
    pub is_anomaly: bool,
    pub anomaly_reason: Option<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FuelEventType {
    Reading,          // Normal reading
    Refuel,           // Fuel level increased significantly
    ConsumptionSpike, // Abnormal consumption
    TheftAlert,       // Sudden drop without movement
    SensorError,      // Invalid reading
    LowFuel,          // Fuel below threshold
}

impl FuelEventType {
    pub fn as_str(&self) -> &'static str {
        match self {
            FuelEventType::Reading => "reading",
            FuelEventType::Refuel => "refuel",
            FuelEventType::ConsumptionSpike => "consumption_spike",
            FuelEventType::TheftAlert => "theft_alert",
            FuelEventType::SensorError => "sensor_error",
            FuelEventType::LowFuel => "low_fuel",
        }
    }
}

/// Service for tracking fuel consumption and detecting anomalies
pub struct FuelTracker {
    /// Last known fuel state by device_id
    fuel_states: Arc<RwLock<HashMap<i32, FuelState>>>,
    /// Low fuel threshold (%)
    low_fuel_threshold: i16,
}

impl FuelTracker {
    pub fn new() -> Self {
        Self {
            fuel_states: Arc::new(RwLock::new(HashMap::new())),
            low_fuel_threshold: 15,
        }
    }

    pub fn with_low_fuel_threshold(mut self, threshold: i16) -> Self {
        self.low_fuel_threshold = threshold;
        self
    }

    /// Process a frame and detect fuel events
    /// Returns Some(FuelEvent) if a significant event occurred
    pub async fn process_frame(
        &self,
        device_id: i32,
        frame: &HhFrame,
    ) -> Option<FuelEvent> {
        let fuel_percent = frame.fuel_raw as i16;
        let now = DateTime::<Utc>::from_naive_utc_and_offset(frame.recorded_at, Utc);

        // Skip invalid fuel readings
        if fuel_percent < 0 || fuel_percent > 100 {
            return Some(FuelEvent {
                device_id,
                recorded_at: now,
                fuel_percent,
                fuel_change: 0,
                odometer_km: frame.odometer_km,
                latitude: frame.latitude,
                longitude: frame.longitude,
                speed_kph: frame.speed_kph,
                rpm: frame.rpm,
                ignition_on: frame.ignition_on,
                event_type: FuelEventType::SensorError,
                is_anomaly: true,
                anomaly_reason: Some(format!("Invalid fuel reading: {}%", fuel_percent)),
            });
        }

        let mut states = self.fuel_states.write().await;

        if let Some(prev_state) = states.get(&device_id) {
            let fuel_change = fuel_percent - prev_state.last_fuel_percent;
            let distance_km = frame.odometer_km.saturating_sub(prev_state.last_odometer_km);

            // Update state
            let new_state = FuelState {
                device_id,
                last_fuel_percent: fuel_percent,
                last_odometer_km: frame.odometer_km,
                last_timestamp: now,
            };
            states.insert(device_id, new_state);

            // Detect events
            let (event_type, is_anomaly, anomaly_reason) = self.classify_fuel_event(
                fuel_change,
                fuel_percent,
                distance_km,
                frame.ignition_on,
                frame.speed_kph,
            );

            // Only return significant events
            if event_type != FuelEventType::Reading || is_anomaly || fuel_percent <= self.low_fuel_threshold {
                let event = FuelEvent {
                    device_id,
                    recorded_at: now,
                    fuel_percent,
                    fuel_change,
                    odometer_km: frame.odometer_km,
                    latitude: frame.latitude,
                    longitude: frame.longitude,
                    speed_kph: frame.speed_kph,
                    rpm: frame.rpm,
                    ignition_on: frame.ignition_on,
                    event_type: if fuel_percent <= self.low_fuel_threshold && event_type == FuelEventType::Reading {
                        FuelEventType::LowFuel
                    } else {
                        event_type
                    },
                    is_anomaly,
                    anomaly_reason,
                };

                match event.event_type {
                    FuelEventType::Refuel => {
                        info!(device_id, fuel_change, fuel_percent, "Refuel detected");
                    }
                    FuelEventType::TheftAlert => {
                        warn!(device_id, fuel_change, "Possible fuel theft detected!");
                    }
                    FuelEventType::ConsumptionSpike => {
                        warn!(device_id, fuel_change, distance_km, "Abnormal fuel consumption");
                    }
                    FuelEventType::LowFuel => {
                        info!(device_id, fuel_percent, "Low fuel warning");
                    }
                    _ => {}
                }

                return Some(event);
            }

            None
        } else {
            // First reading for this device
            let state = FuelState {
                device_id,
                last_fuel_percent: fuel_percent,
                last_odometer_km: frame.odometer_km,
                last_timestamp: now,
            };
            states.insert(device_id, state);
            debug!(device_id, fuel_percent, "Initial fuel reading recorded");
            None
        }
    }

    /// Classify the fuel event based on change and context
    fn classify_fuel_event(
        &self,
        fuel_change: i16,
        current_level: i16,
        distance_km: u32,
        ignition_on: bool,
        speed_kph: f64,
    ) -> (FuelEventType, bool, Option<String>) {
        // Refuel detection (significant increase)
        if fuel_change >= REFUEL_THRESHOLD_PERCENT {
            return (FuelEventType::Refuel, false, None);
        }

        // Theft detection (significant drop without movement)
        if fuel_change <= -REFUEL_THRESHOLD_PERCENT && distance_km < 5 && !ignition_on {
            return (
                FuelEventType::TheftAlert,
                true,
                Some(format!(
                    "Fuel dropped {}% without significant movement ({}km)",
                    -fuel_change, distance_km
                )),
            );
        }

        // Consumption spike detection
        if fuel_change < -MAX_NORMAL_CONSUMPTION_PERCENT {
            // Calculate expected consumption (rough estimate: 10L/100km for 50L tank = 2%/10km)
            let expected_drop = (distance_km as i16) / 5; // ~2% per 10km
            if -fuel_change > expected_drop + 5 {
                return (
                    FuelEventType::ConsumptionSpike,
                    true,
                    Some(format!(
                        "Consumption {}% over {}km exceeds expected {}%",
                        -fuel_change, distance_km, expected_drop
                    )),
                );
            }
        }

        (FuelEventType::Reading, false, None)
    }

    /// Get the last known fuel level for a device
    pub async fn get_fuel_level(&self, device_id: i32) -> Option<i16> {
        self.fuel_states
            .read()
            .await
            .get(&device_id)
            .map(|s| s.last_fuel_percent)
    }
}

impl Default for FuelTracker {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::NaiveDateTime;
    use crate::telemetry::model::{FrameKind, FrameVersion};

    fn make_frame(fuel: u8, odometer: u32, ignition: bool, time_str: &str) -> HhFrame {
        let recorded_at = NaiveDateTime::parse_from_str(time_str, "%Y-%m-%d %H:%M:%S").unwrap();
        HhFrame {
            kind: FrameKind::RealTime,
            version: FrameVersion::V3,
            recorded_at,
            latitude: 36.8,
            longitude: 10.1,
            speed_kph: if ignition { 50.0 } else { 0.0 },
            heading_deg: 0.0,
            power_voltage: 12,
            power_source_rescue: false,
            fuel_raw: fuel,
            ignition_on: ignition,
            mems_x: 0,
            mems_y: 0,
            mems_z: 0,
            temperature_raw: 80,
            odometer_km: odometer,
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
    async fn test_refuel_detection() {
        let tracker = FuelTracker::new();

        // Initial reading
        let frame1 = make_frame(30, 10000, true, "2025-01-01 10:00:00");
        let result = tracker.process_frame(1, &frame1).await;
        assert!(result.is_none()); // First reading

        // Refuel detected
        let frame2 = make_frame(80, 10000, false, "2025-01-01 10:30:00");
        let result = tracker.process_frame(1, &frame2).await;
        assert!(result.is_some());
        let event = result.unwrap();
        assert_eq!(event.event_type, FuelEventType::Refuel);
        assert_eq!(event.fuel_change, 50);
    }

    #[tokio::test]
    async fn test_theft_detection() {
        let tracker = FuelTracker::new();

        // Initial reading
        let frame1 = make_frame(80, 10000, false, "2025-01-01 10:00:00");
        tracker.process_frame(1, &frame1).await;

        // Sudden drop without movement
        let frame2 = make_frame(50, 10002, false, "2025-01-01 12:00:00");
        let result = tracker.process_frame(1, &frame2).await;
        assert!(result.is_some());
        let event = result.unwrap();
        assert_eq!(event.event_type, FuelEventType::TheftAlert);
        assert!(event.is_anomaly);
    }

    #[tokio::test]
    async fn test_low_fuel_warning() {
        let tracker = FuelTracker::new().with_low_fuel_threshold(20);

        // Initial reading
        let frame1 = make_frame(25, 10000, true, "2025-01-01 10:00:00");
        tracker.process_frame(1, &frame1).await;

        // Low fuel
        let frame2 = make_frame(15, 10050, true, "2025-01-01 11:00:00");
        let result = tracker.process_frame(1, &frame2).await;
        assert!(result.is_some());
        let event = result.unwrap();
        assert_eq!(event.event_type, FuelEventType::LowFuel);
    }
}
