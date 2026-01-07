use async_trait::async_trait;

use crate::services::fuel_tracker::FuelEvent;
use crate::services::geofence_detector::{Geofence, GeofenceEvent};
use crate::services::stop_detector::CompletedStop;
use crate::telemetry::model::{HhFrame, HhInfoFrame};

#[async_trait]
pub trait TelemetryStore: Send + Sync {
    async fn ingest_info_frame(&self, protocol_type: &str, info: &HhInfoFrame) -> anyhow::Result<String>;

    async fn ingest_hh_frame(
        &self,
        device_uid: &str,
        protocol_type: &str,
        frame: &HhFrame,
    ) -> anyhow::Result<()>;

    /// Get device_id from IMEI
    async fn get_device_id(&self, imei: &str) -> anyhow::Result<Option<i32>>;

    /// Insert a completed vehicle stop
    async fn insert_vehicle_stop(&self, stop: &CompletedStop, vehicle_id: Option<i32>, company_id: i32) -> anyhow::Result<i64>;

    /// Insert a fuel event record
    async fn insert_fuel_record(&self, event: &FuelEvent, vehicle_id: Option<i32>, company_id: i32) -> anyhow::Result<i64>;

    /// Get vehicle_id and company_id for a device
    async fn get_device_vehicle_info(&self, device_id: i32) -> anyhow::Result<(Option<i32>, i32)>;

    /// Load all active geofences from database
    async fn load_geofences(&self) -> anyhow::Result<Vec<Geofence>>;

    /// Insert a geofence event (entry/exit)
    async fn insert_geofence_event(&self, event: &GeofenceEvent, duration_seconds: Option<i32>) -> anyhow::Result<i32>;
}

#[async_trait]
pub trait TelemetryEventPublisher: Send + Sync {
    async fn publish_hh_frame(&self, device_uid: &str, protocol: &str, frame: &HhFrame) -> anyhow::Result<()>;
}
