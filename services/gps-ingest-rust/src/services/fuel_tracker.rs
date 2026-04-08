//! Fuel Tracking Service
//! 
//! Tracks fuel consumption, detects refueling events and anomalies.

use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;
use chrono::{DateTime, Utc};
use tracing::{info, warn, debug};

use crate::telemetry::model::HhFrame;

/// Minimum fuel change to detect refuel (%) — when ignition is ON or vehicle recently moved
const REFUEL_THRESHOLD_PERCENT: i16 = 10;

/// Minimum fuel change to detect refuel/theft when parked with ignition OFF
/// Higher threshold to filter capacitive sensor noise from temperature changes
const PARKED_THRESHOLD_PERCENT: i16 = 30;

/// Maximum normal consumption per reading (%)
const MAX_NORMAL_CONSUMPTION_PERCENT: i16 = 5;

/// Minimum number of consecutive readings at a new level to confirm a real change
const SPIKE_CONFIRM_COUNT: u8 = 2;

/// Maximum fuel change considered a potential spike (%)
const SPIKE_THRESHOLD_PERCENT: i16 = 10;

/// Fuel state for a device
#[derive(Debug, Clone)]
pub struct FuelState {
    pub device_id: i32,
    pub last_fuel_percent: i16,
    pub last_odometer_km: u32,
    pub last_timestamp: DateTime<Utc>,
    /// Pending fuel level that hasn't been confirmed yet (spike buffer)
    pub pending_fuel: Option<PendingFuelReading>,
}

/// A fuel reading that needs confirmation (might be a spike)
#[derive(Debug, Clone)]
pub struct PendingFuelReading {
    pub fuel_percent: i16,
    pub frame_snapshot: FuelFrameSnapshot,
    pub consecutive_count: u8,
}

/// Snapshot of frame data for deferred event emission
#[derive(Debug, Clone)]
pub struct FuelFrameSnapshot {
    pub recorded_at: DateTime<Utc>,
    pub odometer_km: u32,
    pub latitude: f64,
    pub longitude: f64,
    pub speed_kph: f64,
    pub rpm: Option<u16>,
    pub ignition_on: bool,
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

    /// Check if a device already has a known fuel state in memory
    pub async fn has_state(&self, device_id: i32) -> bool {
        self.fuel_states.read().await.contains_key(&device_id)
    }

    /// Seed fuel state from database (used to restore state after service restart)
    pub async fn seed_state(&self, device_id: i32, fuel_percent: i16, odometer_km: u32, timestamp: DateTime<Utc>) {
        let state = FuelState {
            device_id,
            last_fuel_percent: fuel_percent,
            last_odometer_km: odometer_km,
            last_timestamp: timestamp,
            pending_fuel: None,
        };
        self.fuel_states.write().await.insert(device_id, state);
        debug!(device_id, fuel_percent, odometer_km, "Fuel state restored from DB");
    }

    /// Process a frame and detect fuel events.
    /// `fuel_percent` must be pre-converted to 0-100% by the caller (based on fuel_sensor_mode).
    /// Returns Some(FuelEvent) if a significant event occurred.
    ///
    /// **Spike filter**: When a large fuel change (>10%) is detected, the reading is held
    /// in a buffer until confirmed by the next reading. If the next reading returns to
    /// near the previous level, the spike is discarded (sensor glitch). If the next reading
    /// stays at the new level, the change is confirmed and emitted.
    pub async fn process_frame(
        &self,
        device_id: i32,
        frame: &HhFrame,
        fuel_percent: i16,
    ) -> Option<FuelEvent> {
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

        if let Some(prev_state) = states.get(&device_id).cloned() {
            let confirmed_level = prev_state.last_fuel_percent;
            let fuel_change_from_confirmed = fuel_percent - confirmed_level;
            let is_large_change = fuel_change_from_confirmed.abs() >= SPIKE_THRESHOLD_PERCENT;

            // === SPIKE FILTER LOGIC ===
            if let Some(pending) = &prev_state.pending_fuel {
                // We have a pending (unconfirmed) reading
                let recovery_to_confirmed = (fuel_percent - confirmed_level).abs();
                let stays_at_pending = (fuel_percent - pending.fuel_percent).abs() <= 5;

                if recovery_to_confirmed <= 5 {
                    // Current reading is back near the confirmed level → the pending was a SPIKE
                    warn!(
                        device_id,
                        confirmed_level,
                        spike_value = pending.fuel_percent,
                        recovered_to = fuel_percent,
                        "Fuel spike filtered (sensor glitch)"
                    );
                    // Discard pending, update state WITHOUT changing confirmed level
                    let new_state = FuelState {
                        device_id,
                        last_fuel_percent: confirmed_level, // Keep the confirmed level
                        last_odometer_km: frame.odometer_km,
                        last_timestamp: now,
                        pending_fuel: None,
                    };
                    states.insert(device_id, new_state);
                    return None; // Spike discarded
                } else if stays_at_pending || pending.consecutive_count >= SPIKE_CONFIRM_COUNT {
                    // Current reading confirms the pending level → it's a REAL change
                    // Emit the deferred event now
                    let snap = &pending.frame_snapshot;
                    let pending_change = pending.fuel_percent - confirmed_level;
                    let pending_distance = snap.odometer_km.saturating_sub(prev_state.last_odometer_km);

                    let (event_type, is_anomaly, anomaly_reason) = self.classify_fuel_event(
                        pending_change,
                        pending.fuel_percent,
                        pending_distance,
                        snap.ignition_on,
                        snap.speed_kph,
                    );

                    // Update state to the confirmed new level
                    let new_state = FuelState {
                        device_id,
                        last_fuel_percent: fuel_percent,
                        last_odometer_km: frame.odometer_km,
                        last_timestamp: now,
                        pending_fuel: None,
                    };
                    states.insert(device_id, new_state);

                    info!(
                        device_id,
                        pending_change,
                        fuel_percent = pending.fuel_percent,
                        "Fuel change confirmed after spike check"
                    );

                    let event_type_final = if pending.fuel_percent <= self.low_fuel_threshold && event_type == FuelEventType::Reading {
                        FuelEventType::LowFuel
                    } else {
                        event_type
                    };

                    return Some(FuelEvent {
                        device_id,
                        recorded_at: snap.recorded_at,
                        fuel_percent: pending.fuel_percent,
                        fuel_change: pending_change,
                        odometer_km: snap.odometer_km,
                        latitude: snap.latitude,
                        longitude: snap.longitude,
                        speed_kph: snap.speed_kph,
                        rpm: snap.rpm,
                        ignition_on: snap.ignition_on,
                        event_type: event_type_final,
                        is_anomaly,
                        anomaly_reason,
                    });
                } else {
                    // Still uncertain — increment counter, keep waiting
                    let mut updated_state = prev_state.clone();
                    updated_state.last_timestamp = now;
                    if let Some(ref mut p) = updated_state.pending_fuel {
                        p.consecutive_count += 1;
                    }
                    states.insert(device_id, updated_state);
                    return None;
                }
            }

            // No pending reading — check if this is a potential spike
            if is_large_change {
                // Large change detected — hold it as pending, don't emit yet
                debug!(
                    device_id,
                    confirmed_level,
                    new_reading = fuel_percent,
                    change = fuel_change_from_confirmed,
                    "Large fuel change detected, buffering for confirmation"
                );
                let new_state = FuelState {
                    device_id,
                    last_fuel_percent: confirmed_level, // Keep confirmed level unchanged
                    last_odometer_km: prev_state.last_odometer_km,
                    last_timestamp: now,
                    pending_fuel: Some(PendingFuelReading {
                        fuel_percent,
                        frame_snapshot: FuelFrameSnapshot {
                            recorded_at: now,
                            odometer_km: frame.odometer_km,
                            latitude: frame.latitude,
                            longitude: frame.longitude,
                            speed_kph: frame.speed_kph,
                            rpm: frame.rpm,
                            ignition_on: frame.ignition_on,
                        },
                        consecutive_count: 1,
                    }),
                };
                states.insert(device_id, new_state);
                return None; // Wait for confirmation
            }

            // Small change — process normally (no spike risk)
            let distance_km = frame.odometer_km.saturating_sub(prev_state.last_odometer_km);
            let new_state = FuelState {
                device_id,
                last_fuel_percent: fuel_percent,
                last_odometer_km: frame.odometer_km,
                last_timestamp: now,
                pending_fuel: None,
            };
            states.insert(device_id, new_state);

            let (event_type, is_anomaly, anomaly_reason) = self.classify_fuel_event(
                fuel_change_from_confirmed,
                fuel_percent,
                distance_km,
                frame.ignition_on,
                frame.speed_kph,
            );

            if event_type != FuelEventType::Reading || is_anomaly || fuel_percent <= self.low_fuel_threshold {
                let event = FuelEvent {
                    device_id,
                    recorded_at: now,
                    fuel_percent,
                    fuel_change: fuel_change_from_confirmed,
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
                    FuelEventType::Refuel => info!(device_id, fuel_change_from_confirmed, fuel_percent, "Refuel detected"),
                    FuelEventType::TheftAlert => warn!(device_id, fuel_change_from_confirmed, "Possible fuel theft detected!"),
                    FuelEventType::ConsumptionSpike => warn!(device_id, fuel_change_from_confirmed, distance_km, "Abnormal fuel consumption"),
                    FuelEventType::LowFuel => info!(device_id, fuel_percent, "Low fuel warning"),
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
                pending_fuel: None,
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
        // When parked (ignition OFF, no movement), use higher threshold
        // to filter capacitive sensor noise from temperature changes
        let is_parked = !ignition_on && speed_kph < 1.0;
        let threshold = if is_parked { PARKED_THRESHOLD_PERCENT } else { REFUEL_THRESHOLD_PERCENT };

        // Refuel detection (significant increase)
        if fuel_change >= threshold {
            return (FuelEventType::Refuel, false, None);
        }

        // Theft detection (significant drop without movement)
        if fuel_change <= -threshold && distance_km < 5 && !ignition_on {
            return (
                FuelEventType::TheftAlert,
                true,
                Some(format!(
                    "Fuel dropped {}% without significant movement ({}km)",
                    -fuel_change, distance_km
                )),
            );
        }

        // Consumption spike detection (only when moving)
        if !is_parked && fuel_change < -MAX_NORMAL_CONSUMPTION_PERCENT {
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
            fuel_rate_l_per_100km: None,
            fms_temperature_c: None,
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
        let result = tracker.process_frame(1, &frame1, 30).await;
        assert!(result.is_none()); // First reading

        // Large increase → buffered (not emitted yet)
        let frame2 = make_frame(80, 10000, false, "2025-01-01 10:30:00");
        let result = tracker.process_frame(1, &frame2, 80).await;
        assert!(result.is_none()); // Buffered, waiting for confirmation

        // Confirm: stays at 80 → refuel confirmed
        let frame3 = make_frame(80, 10000, false, "2025-01-01 10:31:00");
        let result = tracker.process_frame(1, &frame3, 80).await;
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
        tracker.process_frame(1, &frame1, 80).await;

        // Sudden drop → buffered
        let frame2 = make_frame(50, 10002, false, "2025-01-01 12:00:00");
        let result = tracker.process_frame(1, &frame2, 50).await;
        assert!(result.is_none()); // Buffered

        // Confirm: stays at 50 → theft confirmed
        let frame3 = make_frame(50, 10002, false, "2025-01-01 12:01:00");
        let result = tracker.process_frame(1, &frame3, 50).await;
        assert!(result.is_some());
        let event = result.unwrap();
        assert_eq!(event.event_type, FuelEventType::TheftAlert);
        assert!(event.is_anomaly);
    }

    #[tokio::test]
    async fn test_spike_filtered() {
        let tracker = FuelTracker::new();

        // Initial reading: 40%
        let frame1 = make_frame(40, 10000, true, "2025-01-01 10:00:00");
        tracker.process_frame(1, &frame1, 40).await;

        // Spike down to 20% → buffered (not emitted)
        let frame2 = make_frame(20, 10001, true, "2025-01-01 10:01:00");
        let result = tracker.process_frame(1, &frame2, 20).await;
        assert!(result.is_none()); // Buffered

        // Recovery back to 40% → spike discarded!
        let frame3 = make_frame(40, 10002, true, "2025-01-01 10:02:00");
        let result = tracker.process_frame(1, &frame3, 40).await;
        assert!(result.is_none()); // Spike discarded, no event

        // Verify state is still at 40% (not polluted by spike)
        let level = tracker.get_fuel_level(1).await;
        assert_eq!(level, Some(40));
    }

    #[tokio::test]
    async fn test_small_change_immediate() {
        // Small changes (<10%) should pass through immediately without buffering
        let tracker = FuelTracker::new();

        let frame1 = make_frame(50, 10000, true, "2025-01-01 10:00:00");
        tracker.process_frame(1, &frame1, 50).await;

        // Small drop: 50 → 45 (5% change, < 10% threshold)
        let frame2 = make_frame(45, 10050, true, "2025-01-01 11:00:00");
        let result = tracker.process_frame(1, &frame2, 45).await;
        // Small change = Reading type, no event emitted unless special
        assert!(result.is_none());

        // Verify state updated to 45
        let level = tracker.get_fuel_level(1).await;
        assert_eq!(level, Some(45));
    }

    #[tokio::test]
    async fn test_low_fuel_warning() {
        let tracker = FuelTracker::new().with_low_fuel_threshold(20);

        // Initial reading
        let frame1 = make_frame(25, 10000, true, "2025-01-01 10:00:00");
        tracker.process_frame(1, &frame1, 25).await;

        // Small drop to low fuel (25→18 = -7%, below threshold so immediate)
        let frame2 = make_frame(18, 10050, true, "2025-01-01 11:00:00");
        let result = tracker.process_frame(1, &frame2, 18).await;
        assert!(result.is_some());
        let event = result.unwrap();
        assert_eq!(event.event_type, FuelEventType::LowFuel);
    }
}
