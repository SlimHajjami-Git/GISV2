use async_trait::async_trait;

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
}

#[async_trait]
pub trait TelemetryEventPublisher: Send + Sync {
    async fn publish_hh_frame(&self, device_uid: &str, protocol: &str, frame: &HhFrame) -> anyhow::Result<()>;
}
