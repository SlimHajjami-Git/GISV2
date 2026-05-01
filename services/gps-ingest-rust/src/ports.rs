use async_trait::async_trait;

use crate::db::LastKnownPosition;
use crate::services::driving_events::DrivingEventRecord;
use crate::services::fuel_tracker::FuelEvent;
use crate::services::geofence_detector::{Geofence, GeofenceEvent};
use crate::services::stop_detector::CompletedStop;
use crate::services::trip_detector::CompletedTrip;
use crate::telemetry::model::{HhFrame, HhInfoFrame};

#[async_trait]
pub trait TelemetryStore: Send + Sync {
    async fn ingest_info_frame(&self, protocol_type: &str, info: &HhInfoFrame) -> anyhow::Result<String>;

    async fn ingest_hh_frame(
        &self,
        device_uid: &str,
        protocol_type: &str,
        frame: &HhFrame,
        event_key: &str,
    ) -> anyhow::Result<()>;

    /// Get device_id from IMEI
    async fn get_device_id(&self, imei: &str) -> anyhow::Result<Option<i32>>;

    /// Get device mat (GPS identifier) from device_id
    async fn get_device_mat(&self, device_id: i32) -> anyhow::Result<Option<String>>;

    /// Get device_uid (IMEI) from MAT prefix
    async fn get_device_uid_by_mat(&self, mat: &str) -> anyhow::Result<Option<String>>;

    /// Insert a completed vehicle stop
    async fn insert_vehicle_stop(&self, stop: &CompletedStop, vehicle_id: Option<i32>, company_id: i32) -> anyhow::Result<i64>;

    /// Insert a fuel event record
    async fn insert_fuel_record(&self, event: &FuelEvent, vehicle_id: Option<i32>, company_id: i32) -> anyhow::Result<i64>;

    /// Get vehicle_id, company_id, and firmware_version for a device
    async fn get_device_vehicle_info(&self, device_id: i32) -> anyhow::Result<(Option<i32>, i32, Option<String>)>;

    /// Get fuel sensor configuration for a device
    /// Returns (fuel_sensor_mode, tank_capacity_liters)
    async fn get_fuel_config(&self, device_id: i32) -> anyhow::Result<(String, Option<i32>)>;

    /// Get the most recent position for a device (used for anti-spam filtering)
    async fn get_last_position(
        &self,
        device_id: i32,
    ) -> anyhow::Result<Option<LastKnownPosition>>;

    /// Load all active geofences from database
    async fn load_geofences(&self) -> anyhow::Result<Vec<Geofence>>;

    /// Insert a geofence event (entry/exit)
    async fn insert_geofence_event(&self, event: &GeofenceEvent, duration_seconds: Option<i32>) -> anyhow::Result<i32>;

    /// Insert a completed trip
    async fn insert_trip(&self, trip: &CompletedTrip) -> anyhow::Result<i64>;

    /// Insert a driving event
    async fn insert_driving_event(&self, event: &DrivingEventRecord) -> anyhow::Result<i64>;

    /// Load the last fuel record for a device (used to restore fuel_tracker state after restart)
    async fn get_last_fuel_record(&self, device_id: i32) -> anyhow::Result<Option<(i16, u32, chrono::DateTime<chrono::Utc>)>>;

    /// Insert a device lifecycle event (restart, disconnect, tamper)
    async fn insert_device_event(&self, event: &crate::services::device_event::DeviceEventRecord) -> anyhow::Result<i64>;

    /// Check immobilization state for a device.
    /// Returns (immobilization_active, command_go) from gps_devices table.
    /// If immobilization_active is true, auto-recovery should NOT be sent.
    /// command_go is the full command from DB (e.g. "AJ+GO#9999\n"), sent as-is.
    async fn get_immobilization_state(&self, device_id: i32) -> anyhow::Result<(bool, String)>;

    /// Get the oldest pending command for a device and mark it as 'sent'
    /// Returns (command_id, command_text) if a pending command exists
    async fn get_pending_command(&self, device_id: i32) -> anyhow::Result<Option<(i64, String)>>;

    /// Update a command's status after send attempt
    async fn update_command_status(&self, command_id: i64, status: &str, error: Option<&str>) -> anyhow::Result<()>;

    /// Insert an auto-recovery command log entry (flags_hex = the raw flags byte e.g. "C3", "67")
    async fn log_auto_recovery(&self, device_id: i32, command_text: &str, flags_hex: &str) -> anyhow::Result<()>;

    /// Update last_communication timestamp for a device (keeps vehicle "online" in monitoring)
    async fn update_device_last_communication(&self, device_id: i32) -> anyhow::Result<()>;

    /// Log a frame to frame_debug_log for audit (immobilization detection, non-standard frames, etc.)
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
    ) -> anyhow::Result<()>;
}

#[async_trait]
pub trait TelemetryEventPublisher: Send + Sync {
    async fn publish_hh_frame(&self, device_uid: &str, protocol: &str, frame: &HhFrame) -> anyhow::Result<()>;
    async fn publish_device_event(&self, event: &crate::services::device_event::DeviceEventRecord) -> anyhow::Result<()>;
}
