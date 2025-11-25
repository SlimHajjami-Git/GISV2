mod telemetry;
mod transport;

use anyhow::Result;
use tracing::{info, Level};

#[tokio::main]
async fn main() -> Result<()> {
    tracing_subscriber::fmt()
        .with_max_level(Level::INFO)
        .with_env_filter(tracing_subscriber::EnvFilter::from_default_env())
        .init();

    info!("gps-ingest-rust bootstrap starting");

    // TODO: load configuration, initialize RabbitMQ connection, register decoders.

    Ok(())
}
