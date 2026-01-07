use std::env;

use amqprs::{
    channel::{BasicPublishArguments, Channel, ExchangeDeclareArguments},
    connection::{Connection, OpenConnectionArguments},
    BasicProperties,
};
use anyhow::{Context, Result};
use serde_json::json;

use crate::{
    ports::TelemetryEventPublisher,
    telemetry::model::HhFrame,
};

pub struct TelemetryPublisher {
    _connection: Connection,
    channel: Channel,
    exchange: String,
    routing_key: String,
}

#[async_trait::async_trait]
impl TelemetryEventPublisher for TelemetryPublisher {
    async fn publish_hh_frame(&self, device_uid: &str, protocol: &str, frame: &HhFrame) -> Result<()> {
        let payload = json!({
            "device_uid": device_uid,
            "protocol": protocol,
            "recorded_at": frame.recorded_at.and_utc().to_rfc3339(),
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
        let args = BasicPublishArguments::new(&self.exchange, &self.routing_key);
        self.channel
            .basic_publish(BasicProperties::default(), body, args)
            .await
            .with_context(|| "failed to publish telemetry event")?;
        Ok(())
    }
}

impl TelemetryPublisher {
    pub async fn from_env() -> Result<Option<Self>> {
        let host = match env::var("RABBITMQ_HOST").ok() {
            Some(h) if !h.is_empty() => h,
            _ => return Ok(None),
        };

        let port = env::var("RABBITMQ_PORT")
            .ok()
            .and_then(|p| p.parse::<u16>().ok())
            .unwrap_or(5672);
        let username = env::var("RABBITMQ_USERNAME").unwrap_or_else(|_| "guest".to_string());
        let password = env::var("RABBITMQ_PASSWORD").unwrap_or_else(|_| "guest".to_string());
        let exchange = env::var("RABBITMQ_EXCHANGE").unwrap_or_else(|_| "telemetry.raw".to_string());
        let routing_key = env::var("RABBITMQ_ROUTING_KEY").unwrap_or_else(|_| "hh".to_string());

        let connection = Connection::open(&OpenConnectionArguments::new(&host, port, &username, &password))
            .await
            .with_context(|| "failed to connect to RabbitMQ")?;
        let channel = connection
            .open_channel(None)
            .await
            .with_context(|| "failed to open RabbitMQ channel")?;

        channel
            .exchange_declare(
                ExchangeDeclareArguments::new(&exchange, "topic")
                    .durable(true)
                    .finish(),
            )
            .await
            .with_context(|| "failed to declare exchange")?;

        Ok(Some(Self {
            _connection: connection,
            channel,
            exchange,
            routing_key,
        }))
    }

}
