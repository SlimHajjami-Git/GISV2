//! Driving Events Detection Service
//!
//! Detects driving behavior events from GPS and MEMS data:
//! - Harsh braking (speed-based and MEMS X-axis)
//! - Harsh acceleration (speed-based and MEMS X-axis)
//! - Sharp turns/cornering (MEMS Y-axis)
//! - Overspeeding
//! - Speed bumps (MEMS Z-axis)
//! - Potholes (MEMS Z-axis)

use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;
use chrono::{DateTime, Utc};
use tracing::{info, debug};

use crate::telemetry::model::HhFrame;

/// Thresholds for event detection (based on GISV1 values)
const HARSH_BRAKING_ACCEL_MS2: f64 = -4.0;       // m/s² (negative for deceleration)
const HARSH_ACCEL_ACCEL_MS2: f64 = 3.5;          // m/s²
const SPEED_LIMIT_KPH: f64 = 120.0;              // km/h
const MIN_SPEED_FOR_EVENTS: f64 = 5.0;           // km/h (ignore events when nearly stopped)

/// MEMS thresholds (G-force) - from GISV1 AAP.cs
const MEMS_HARSH_BRAKING_G: f64 = -0.4;          // AccelX < -0.4G
const MEMS_HARSH_ACCEL_G: f64 = 0.4;             // AccelX > 0.4G
const MEMS_HARSH_CORNERING_G: f64 = 0.4;         // |AccelY| > 0.4G
const MEMS_SPEED_BUMP_G: f64 = 0.5;              // AccelZ > 0.5G
const MEMS_POTHOLE_G: f64 = -0.6;                // AccelZ < -0.6G
const SPEED_BUMP_MIN_SPEED: f64 = 30.0;          // km/h minimum for speed bump detection

/// Cooldown between events of the same type (seconds)
const EVENT_COOLDOWN_SECS: i64 = 10;

/// Detected driving event
#[derive(Debug, Clone)]
pub struct DrivingEventRecord {
    pub vehicle_id: i32,
    pub device_id: i32,
    pub company_id: i32,
    pub event_type: DrivingEventType,
    pub severity: EventSeverity,
    pub g_force: f64,
    pub speed_kph: f64,
    pub latitude: f64,
    pub longitude: f64,
    pub timestamp: DateTime<Utc>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DrivingEventType {
    HarshBraking,
    HarshAcceleration,
    SharpTurn,
    Cornering,
    Overspeeding,
    SpeedBump,
    Pothole,
}

impl DrivingEventType {
    pub fn as_str(&self) -> &'static str {
        match self {
            DrivingEventType::HarshBraking => "harsh_braking",
            DrivingEventType::HarshAcceleration => "harsh_acceleration",
            DrivingEventType::SharpTurn => "sharp_turn",
            DrivingEventType::Cornering => "cornering",
            DrivingEventType::Overspeeding => "overspeeding",
            DrivingEventType::SpeedBump => "speed_bump",
            DrivingEventType::Pothole => "pothole",
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EventSeverity {
    Low,
    Medium,
    High,
    Critical,
}

impl EventSeverity {
    pub fn as_str(&self) -> &'static str {
        match self {
            EventSeverity::Low => "low",
            EventSeverity::Medium => "medium",
            EventSeverity::High => "high",
            EventSeverity::Critical => "critical",
        }
    }
}

/// Previous frame data for speed-based calculations
#[derive(Debug, Clone)]
struct PreviousFrame {
    speed_kph: f64,
    heading_deg: f64,
    timestamp: DateTime<Utc>,
}

/// Event cooldown tracking
#[derive(Debug, Clone, Default)]
struct DeviceEventState {
    last_events: HashMap<String, DateTime<Utc>>,
}

/// Service for detecting driving behavior events
pub struct DrivingEventsDetector {
    /// Previous frame data by device_id
    previous_frames: Arc<RwLock<HashMap<i32, PreviousFrame>>>,
    /// Event cooldown tracking by device_id
    event_states: Arc<RwLock<HashMap<i32, DeviceEventState>>>,
}

impl DrivingEventsDetector {
    pub fn new() -> Self {
        Self {
            previous_frames: Arc::new(RwLock::new(HashMap::new())),
            event_states: Arc::new(RwLock::new(HashMap::new())),
        }
    }

    /// Process a GPS frame and detect driving events
    /// Returns a list of detected events
    pub async fn process_frame(
        &self,
        device_id: i32,
        vehicle_id: Option<i32>,
        company_id: i32,
        frame: &HhFrame,
    ) -> Vec<DrivingEventRecord> {
        let mut events = Vec::new();
        let now = DateTime::<Utc>::from_naive_utc_and_offset(frame.recorded_at, Utc);

        // Skip if vehicle is nearly stopped
        if frame.speed_kph < MIN_SPEED_FOR_EVENTS {
            // Still update previous frame for next iteration
            let mut prev_frames = self.previous_frames.write().await;
            prev_frames.insert(device_id, PreviousFrame {
                speed_kph: frame.speed_kph,
                heading_deg: frame.heading_deg,
                timestamp: now,
            });
            return events;
        }

        let vid = vehicle_id.unwrap_or(0);

        // Get previous frame for speed-based detection
        let prev_frames = self.previous_frames.read().await;
        let prev_frame = prev_frames.get(&device_id).cloned();
        drop(prev_frames);

        // Speed-based event detection
        if let Some(prev) = &prev_frame {
            let time_delta = (now - prev.timestamp).num_seconds() as f64;
            
            if time_delta > 0.0 && time_delta < 60.0 {
                // Calculate acceleration in m/s²
                let speed_delta_ms = (frame.speed_kph - prev.speed_kph) / 3.6;
                let accel = speed_delta_ms / time_delta;

                // Harsh braking (speed-based)
                if accel < HARSH_BRAKING_ACCEL_MS2 {
                    if self.can_emit_event(device_id, "harsh_braking", now).await {
                        let severity = Self::severity_from_accel(accel.abs());
                        events.push(DrivingEventRecord {
                            vehicle_id: vid,
                            device_id,
                            company_id,
                            event_type: DrivingEventType::HarshBraking,
                            severity,
                            g_force: accel / 9.81,
                            speed_kph: frame.speed_kph,
                            latitude: frame.latitude,
                            longitude: frame.longitude,
                            timestamp: now,
                        });
                        debug!(device_id, accel, "Harsh braking detected (speed-based)");
                    }
                }
                // Harsh acceleration (speed-based)
                else if accel > HARSH_ACCEL_ACCEL_MS2 {
                    if self.can_emit_event(device_id, "harsh_acceleration", now).await {
                        let severity = Self::severity_from_accel(accel);
                        events.push(DrivingEventRecord {
                            vehicle_id: vid,
                            device_id,
                            company_id,
                            event_type: DrivingEventType::HarshAcceleration,
                            severity,
                            g_force: accel / 9.81,
                            speed_kph: frame.speed_kph,
                            latitude: frame.latitude,
                            longitude: frame.longitude,
                            timestamp: now,
                        });
                        debug!(device_id, accel, "Harsh acceleration detected (speed-based)");
                    }
                }

                // Sharp turn detection (heading change)
                let heading_change = Self::normalize_angle(frame.heading_deg - prev.heading_deg);
                if heading_change.abs() > 45.0 && time_delta < 10.0 {
                    if self.can_emit_event(device_id, "sharp_turn", now).await {
                        let severity = if heading_change.abs() > 90.0 {
                            EventSeverity::High
                        } else {
                            EventSeverity::Medium
                        };
                        events.push(DrivingEventRecord {
                            vehicle_id: vid,
                            device_id,
                            company_id,
                            event_type: DrivingEventType::SharpTurn,
                            severity,
                            g_force: heading_change.abs() / 90.0,
                            speed_kph: frame.speed_kph,
                            latitude: frame.latitude,
                            longitude: frame.longitude,
                            timestamp: now,
                        });
                        debug!(device_id, heading_change, "Sharp turn detected");
                    }
                }
            }
        }

        // MEMS-based event detection
        let accel_x = frame.mems_x as f64 / 256.0; // Convert raw to G
        let accel_y = frame.mems_y as f64 / 256.0;
        let accel_z = frame.mems_z as f64 / 256.0;

        // Harsh braking (MEMS X-axis)
        if accel_x < MEMS_HARSH_BRAKING_G {
            if self.can_emit_event(device_id, "harsh_braking_mems", now).await {
                let severity = Self::severity_from_g(accel_x.abs());
                events.push(DrivingEventRecord {
                    vehicle_id: vid,
                    device_id,
                    company_id,
                    event_type: DrivingEventType::HarshBraking,
                    severity,
                    g_force: accel_x.abs(),
                    speed_kph: frame.speed_kph,
                    latitude: frame.latitude,
                    longitude: frame.longitude,
                    timestamp: now,
                });
                debug!(device_id, accel_x, "Harsh braking detected (MEMS)");
            }
        }
        // Harsh acceleration (MEMS X-axis)
        else if accel_x > MEMS_HARSH_ACCEL_G {
            if self.can_emit_event(device_id, "harsh_accel_mems", now).await {
                let severity = Self::severity_from_g(accel_x);
                events.push(DrivingEventRecord {
                    vehicle_id: vid,
                    device_id,
                    company_id,
                    event_type: DrivingEventType::HarshAcceleration,
                    severity,
                    g_force: accel_x,
                    speed_kph: frame.speed_kph,
                    latitude: frame.latitude,
                    longitude: frame.longitude,
                    timestamp: now,
                });
                debug!(device_id, accel_x, "Harsh acceleration detected (MEMS)");
            }
        }

        // Harsh cornering (MEMS Y-axis)
        if accel_y.abs() > MEMS_HARSH_CORNERING_G {
            if self.can_emit_event(device_id, "cornering", now).await {
                let severity = Self::severity_from_g(accel_y.abs());
                events.push(DrivingEventRecord {
                    vehicle_id: vid,
                    device_id,
                    company_id,
                    event_type: DrivingEventType::Cornering,
                    severity,
                    g_force: accel_y.abs(),
                    speed_kph: frame.speed_kph,
                    latitude: frame.latitude,
                    longitude: frame.longitude,
                    timestamp: now,
                });
                debug!(device_id, accel_y, "Harsh cornering detected (MEMS)");
            }
        }

        // Speed bump (MEMS Z-axis, only at speed > 30 km/h)
        if accel_z > MEMS_SPEED_BUMP_G && frame.speed_kph > SPEED_BUMP_MIN_SPEED {
            if self.can_emit_event(device_id, "speed_bump", now).await {
                events.push(DrivingEventRecord {
                    vehicle_id: vid,
                    device_id,
                    company_id,
                    event_type: DrivingEventType::SpeedBump,
                    severity: EventSeverity::Low,
                    g_force: accel_z,
                    speed_kph: frame.speed_kph,
                    latitude: frame.latitude,
                    longitude: frame.longitude,
                    timestamp: now,
                });
                debug!(device_id, accel_z, "Speed bump detected");
            }
        }

        // Pothole (MEMS Z-axis negative)
        if accel_z < MEMS_POTHOLE_G {
            if self.can_emit_event(device_id, "pothole", now).await {
                events.push(DrivingEventRecord {
                    vehicle_id: vid,
                    device_id,
                    company_id,
                    event_type: DrivingEventType::Pothole,
                    severity: EventSeverity::Medium,
                    g_force: accel_z.abs(),
                    speed_kph: frame.speed_kph,
                    latitude: frame.latitude,
                    longitude: frame.longitude,
                    timestamp: now,
                });
                debug!(device_id, accel_z, "Pothole detected");
            }
        }

        // Overspeeding
        if frame.speed_kph > SPEED_LIMIT_KPH {
            if self.can_emit_event(device_id, "overspeeding", now).await {
                let severity = if frame.speed_kph > 150.0 {
                    EventSeverity::Critical
                } else if frame.speed_kph > 140.0 {
                    EventSeverity::High
                } else {
                    EventSeverity::Medium
                };
                events.push(DrivingEventRecord {
                    vehicle_id: vid,
                    device_id,
                    company_id,
                    event_type: DrivingEventType::Overspeeding,
                    severity,
                    g_force: 0.0,
                    speed_kph: frame.speed_kph,
                    latitude: frame.latitude,
                    longitude: frame.longitude,
                    timestamp: now,
                });
                debug!(device_id, speed = frame.speed_kph, "Overspeeding detected");
            }
        }

        // Update previous frame
        let mut prev_frames = self.previous_frames.write().await;
        prev_frames.insert(device_id, PreviousFrame {
            speed_kph: frame.speed_kph,
            heading_deg: frame.heading_deg,
            timestamp: now,
        });

        // Log if events were detected
        if !events.is_empty() {
            info!(
                device_id,
                event_count = events.len(),
                "Driving events detected"
            );
        }

        events
    }

    /// Check if we can emit an event (cooldown check)
    async fn can_emit_event(&self, device_id: i32, event_type: &str, now: DateTime<Utc>) -> bool {
        let mut states = self.event_states.write().await;
        let state = states.entry(device_id).or_default();

        if let Some(last_time) = state.last_events.get(event_type) {
            let elapsed = (now - *last_time).num_seconds();
            if elapsed < EVENT_COOLDOWN_SECS {
                return false;
            }
        }

        state.last_events.insert(event_type.to_string(), now);
        true
    }

    /// Determine severity from acceleration (m/s²)
    fn severity_from_accel(accel: f64) -> EventSeverity {
        if accel >= 6.0 {
            EventSeverity::Critical
        } else if accel >= 5.0 {
            EventSeverity::High
        } else if accel >= 4.0 {
            EventSeverity::Medium
        } else {
            EventSeverity::Low
        }
    }

    /// Determine severity from G-force
    fn severity_from_g(g_force: f64) -> EventSeverity {
        if g_force >= 0.7 {
            EventSeverity::Critical
        } else if g_force >= 0.5 {
            EventSeverity::High
        } else if g_force >= 0.4 {
            EventSeverity::Medium
        } else {
            EventSeverity::Low
        }
    }

    /// Normalize angle difference to -180..180
    fn normalize_angle(angle: f64) -> f64 {
        let mut a = angle % 360.0;
        if a > 180.0 {
            a -= 360.0;
        } else if a < -180.0 {
            a += 360.0;
        }
        a
    }
}

impl Default for DrivingEventsDetector {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::NaiveDateTime;
    use crate::telemetry::model::{FrameKind, FrameVersion};

    fn make_frame(speed: f64, heading: f64, mems_x: i16, mems_y: i16, mems_z: i16, time_str: &str) -> HhFrame {
        let recorded_at = NaiveDateTime::parse_from_str(time_str, "%Y-%m-%d %H:%M:%S").unwrap();
        HhFrame {
            kind: FrameKind::RealTime,
            version: FrameVersion::V3,
            recorded_at,
            latitude: 36.8,
            longitude: 10.1,
            speed_kph: speed,
            heading_deg: heading,
            power_voltage: 12,
            power_source_rescue: false,
            fuel_raw: 50,
            ignition_on: true,
            mems_x,
            mems_y,
            mems_z,
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
    async fn test_harsh_braking_detection() {
        let detector = DrivingEventsDetector::new();

        // First frame at 80 km/h
        let frame1 = make_frame(80.0, 0.0, 0, 0, 0, "2025-01-01 10:00:00");
        let events = detector.process_frame(1, Some(1), 1, &frame1).await;
        assert!(events.is_empty());

        // Second frame at 20 km/h (harsh braking)
        let frame2 = make_frame(20.0, 0.0, 0, 0, 0, "2025-01-01 10:00:05");
        let events = detector.process_frame(1, Some(1), 1, &frame2).await;
        assert!(!events.is_empty());
        assert!(events.iter().any(|e| e.event_type == DrivingEventType::HarshBraking));
    }

    #[tokio::test]
    async fn test_overspeeding_detection() {
        let detector = DrivingEventsDetector::new();

        let frame = make_frame(130.0, 0.0, 0, 0, 0, "2025-01-01 10:00:00");
        let events = detector.process_frame(1, Some(1), 1, &frame).await;
        assert!(events.iter().any(|e| e.event_type == DrivingEventType::Overspeeding));
    }
}
