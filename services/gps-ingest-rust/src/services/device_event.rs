use chrono::{DateTime, Utc};
use serde_json::Value as JsonValue;

/// Types of device lifecycle events
#[derive(Debug, Clone, PartialEq)]
pub enum DeviceEventType {
    /// Software reset (AA02/HH02) — power was cut and restored
    Restart,
    /// GSM reset (AA03/HH03) — GSM module restarted
    GsmReset,
    /// Suspected tamper — restart after suspicious offline period while vehicle was moving
    TamperSuspected,
}

impl DeviceEventType {
    pub fn as_str(&self) -> &'static str {
        match self {
            DeviceEventType::Restart => "restart",
            DeviceEventType::GsmReset => "gsm_reset",
            DeviceEventType::TamperSuspected => "tamper_suspected",
        }
    }
}

/// Record to insert into device_events table
#[derive(Debug, Clone)]
pub struct DeviceEventRecord {
    pub device_id: i32,
    pub vehicle_id: Option<i32>,
    pub company_id: i32,
    pub event_type: DeviceEventType,
    pub event_at: DateTime<Utc>,
    pub offline_duration_secs: Option<i32>,
    pub last_known_lat: Option<f64>,
    pub last_known_lon: Option<f64>,
    pub last_known_address: Option<String>,
    pub was_moving: bool,
    pub details: Option<JsonValue>,
}

/// Thresholds for tamper detection
/// If the device was offline for less than this AND the vehicle was moving → tamper suspected
pub const TAMPER_MIN_OFFLINE_SECS: i64 = 5 * 60;       // 5 minutes
pub const TAMPER_MAX_OFFLINE_SECS: i64 = 2 * 60 * 60;  // 2 hours

/// Speed threshold to consider vehicle "was moving" before disconnect
pub const MOVING_SPEED_THRESHOLD: f64 = 5.0; // km/h

/// Minimum offline duration to trigger a power-cut notification (6 hours)
pub const POWER_CUT_MIN_OFFLINE_SECS: i64 = 6 * 3600; // 21600s

/// Determine if a restart event should be classified as tamper_suspected
pub fn classify_restart(
    offline_duration_secs: Option<i64>,
    was_moving: bool,
    ignition_was_on: bool,
) -> DeviceEventType {
    if let Some(duration) = offline_duration_secs {
        // Suspicious: vehicle was moving OR ignition was on, AND offline for 5min-2h
        // Very short (<5min) could be normal power fluctuation
        // Very long (>2h) could be overnight parking
        if (was_moving || ignition_was_on)
            && duration >= TAMPER_MIN_OFFLINE_SECS
            && duration <= TAMPER_MAX_OFFLINE_SECS
        {
            return DeviceEventType::TamperSuspected;
        }
    }
    DeviceEventType::Restart
}
