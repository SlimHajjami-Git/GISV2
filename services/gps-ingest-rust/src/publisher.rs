use std::env;

use amqprs::{
    channel::{BasicPublishArguments, Channel, ExchangeDeclareArguments},
    connection::{Connection, OpenConnectionArguments},
    BasicProperties,
};
use anyhow::{Context, Result};
use serde_json::json;
use tokio::sync::Mutex;
use tracing::{info, warn};

use crate::{
    ports::TelemetryEventPublisher,
    services::device_event::DeviceEventRecord,
    telemetry::model::HhFrame,
};

struct RmqConfig {
    host: String,
    port: u16,
    username: String,
    password: String,
    exchange: String,
    routing_key: String,
}

pub struct TelemetryPublisher {
    config: RmqConfig,
    state: Mutex<Option<(Connection, Channel)>>,
}

impl TelemetryPublisher {
    async fn create_connection(config: &RmqConfig) -> Result<(Connection, Channel)> {
        let conn = Connection::open(
            &OpenConnectionArguments::new(&config.host, config.port, &config.username, &config.password),
        )
        .await
        .with_context(|| "failed to connect to RabbitMQ")?;

        let ch = conn
            .open_channel(None)
            .await
            .with_context(|| "failed to open RabbitMQ channel")?;

        ch.exchange_declare(
            ExchangeDeclareArguments::new(&config.exchange, "topic")
                .durable(true)
                .finish(),
        )
        .await
        .with_context(|| "failed to declare exchange")?;

        info!("RabbitMQ publisher connected to {}:{}", config.host, config.port);
        Ok((conn, ch))
    }

    async fn ensure_connected(&self) -> Result<()> {
        let mut guard = self.state.lock().await;
        if guard.is_none() {
            let (conn, ch) = Self::create_connection(&self.config).await?;
            *guard = Some((conn, ch));
        }
        Ok(())
    }

    async fn invalidate(&self) {
        let mut guard = self.state.lock().await;
        *guard = None;
    }
}

#[async_trait::async_trait]
impl TelemetryEventPublisher for TelemetryPublisher {
    async fn publish_hh_frame(&self, device_uid: &str, protocol: &str, frame: &HhFrame) -> Result<()> {
        let payload = json!({
            "device_uid": device_uid,
            "protocol": protocol,
            "recorded_at": frame.recorded_at.and_utc().to_rfc3339(),
            "ingested_at": chrono::Utc::now().to_rfc3339(),
            "latitude": frame.latitude,
            "longitude": frame.longitude,
            "speed_kph": frame.speed_kph,
            "heading_deg": frame.heading_deg,
            "ignition_on": frame.ignition_on,
            "fuel_raw": frame.fuel_raw,
            "power_voltage": frame.power_voltage,
            "raw_payload": frame.raw_payload,
        });

        let body = serde_json::to_vec(&payload)?;

        // Retry up to 3 times with reconnection on failure
        for attempt in 1u32..=3 {
            self.ensure_connected().await?;

            let result = {
                let guard = self.state.lock().await;
                if let Some((_, ch)) = guard.as_ref() {
                    let mut props = BasicProperties::default();
                    props.with_delivery_mode(2); // persistent
                    let args = BasicPublishArguments::new(&self.config.exchange, &self.config.routing_key);
                    ch.basic_publish(props, body.clone(), args).await
                } else {
                    Err(amqprs::error::Error::ChannelUseError("no channel".to_string()))
                }
            };

            match result {
                Ok(()) => return Ok(()),
                Err(e) => {
                    warn!("RabbitMQ publish failed (attempt {}/3): {}", attempt, e);
                    self.invalidate().await;
                    if attempt < 3 {
                        tokio::time::sleep(std::time::Duration::from_secs(attempt as u64)).await;
                    } else {
                        return Err(anyhow::anyhow!("RabbitMQ publish failed after 3 attempts: {}", e));
                    }
                }
            }
        }

        unreachable!()
    }

    async fn publish_device_event(&self, event: &DeviceEventRecord) -> Result<()> {
        let payload = json!({
            "message_type": "device_event",
            "device_id": event.device_id,
            "vehicle_id": event.vehicle_id,
            "company_id": event.company_id,
            "event_type": event.event_type.as_str(),
            "event_at": event.event_at.to_rfc3339(),
            "offline_duration_secs": event.offline_duration_secs,
            "last_known_lat": event.last_known_lat,
            "last_known_lon": event.last_known_lon,
            "was_moving": event.was_moving,
        });

        let body = serde_json::to_vec(&payload)?;

        for attempt in 1u32..=3 {
            self.ensure_connected().await?;

            let result = {
                let guard = self.state.lock().await;
                if let Some((_, ch)) = guard.as_ref() {
                    let mut props = BasicProperties::default();
                    props.with_delivery_mode(2);
                    let args = BasicPublishArguments::new(&self.config.exchange, "device.event");
                    ch.basic_publish(props, body.clone(), args).await
                } else {
                    Err(amqprs::error::Error::ChannelUseError("no channel".to_string()))
                }
            };

            match result {
                Ok(()) => {
                    info!(device_id = event.device_id, event_type = event.event_type.as_str(),
                        "Published device event to RabbitMQ");
                    return Ok(());
                }
                Err(e) => {
                    warn!("RabbitMQ device event publish failed (attempt {}/3): {}", attempt, e);
                    self.invalidate().await;
                    if attempt < 3 {
                        tokio::time::sleep(std::time::Duration::from_secs(attempt as u64)).await;
                    } else {
                        return Err(anyhow::anyhow!("RabbitMQ device event publish failed after 3 attempts: {}", e));
                    }
                }
            }
        }

        unreachable!()
    }
}

impl TelemetryPublisher {
    pub async fn from_env() -> Result<Option<Self>> {
        let host = match env::var("RABBITMQ_HOST").ok() {
            Some(h) if !h.is_empty() => h,
            _ => return Ok(None),
        };

        let config = RmqConfig {
            host,
            port: env::var("RABBITMQ_PORT")
                .ok()
                .and_then(|p| p.parse::<u16>().ok())
                .unwrap_or(5672),
            username: env::var("RABBITMQ_USERNAME").unwrap_or_else(|_| "guest".to_string()),
            password: env::var("RABBITMQ_PASSWORD").unwrap_or_else(|_| "guest".to_string()),
            exchange: env::var("RABBITMQ_EXCHANGE").unwrap_or_else(|_| "telemetry.raw".to_string()),
            routing_key: env::var("RABBITMQ_ROUTING_KEY").unwrap_or_else(|_| "hh".to_string()),
        };

        // Initial connection attempt
        let (conn, ch) = Self::create_connection(&config).await?;

        Ok(Some(Self {
            config,
            state: Mutex::new(Some((conn, ch))),
        }))
    }
}
