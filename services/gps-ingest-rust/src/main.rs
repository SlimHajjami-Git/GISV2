mod config;
mod db;
mod ports;
mod publisher;
mod services;
mod telemetry;
mod transport;

use anyhow::Result;
use chrono::NaiveDate;
use dotenvy::dotenv;
use sqlx::postgres::PgPoolOptions;
use std::sync::Arc;
use tracing::{info, warn, error, Level};

use crate::ports::{TelemetryEventPublisher, TelemetryStore};
use crate::services::daily_statistics::DailyStatisticsCalculator;

/// CLI arguments
#[derive(Debug)]
enum Command {
    /// Run GPS ingestion service (default)
    Ingest,
    /// Calculate daily statistics for yesterday
    CalculateDaily,
    /// Calculate daily statistics for a specific date
    CalculateDailyFor(NaiveDate),
    /// Show help
    Help,
}

fn parse_args() -> Command {
    let args: Vec<String> = std::env::args().collect();
    
    if args.len() < 2 {
        return Command::Ingest;
    }

    match args[1].as_str() {
        "--calculate-daily" | "-d" => {
            if args.len() >= 3 {
                // Parse date argument (YYYY-MM-DD)
                match NaiveDate::parse_from_str(&args[2], "%Y-%m-%d") {
                    Ok(date) => Command::CalculateDailyFor(date),
                    Err(_) => {
                        eprintln!("Invalid date format. Use YYYY-MM-DD (e.g., 2026-01-20)");
                        std::process::exit(1);
                    }
                }
            } else {
                Command::CalculateDaily
            }
        }
        "--help" | "-h" => Command::Help,
        _ => Command::Ingest,
    }
}

fn print_help() {
    println!("GPS Ingest Rust Service");
    println!();
    println!("USAGE:");
    println!("    gps-ingest-rust [COMMAND]");
    println!();
    println!("COMMANDS:");
    println!("    (none)                    Run GPS ingestion service (default)");
    println!("    --calculate-daily, -d     Calculate daily statistics for yesterday");
    println!("    --calculate-daily DATE    Calculate daily statistics for specific date (YYYY-MM-DD)");
    println!("    --help, -h                Show this help message");
    println!();
    println!("EXAMPLES:");
    println!("    gps-ingest-rust                           # Run ingestion service");
    println!("    gps-ingest-rust --calculate-daily         # Calculate stats for yesterday");
    println!("    gps-ingest-rust -d 2026-01-20             # Calculate stats for Jan 20, 2026");
}

#[tokio::main]
async fn main() -> Result<()> {
    tracing_subscriber::fmt()
        .with_max_level(Level::INFO)
        .with_env_filter(tracing_subscriber::EnvFilter::from_default_env())
        .init();

    let command = parse_args();

    // Handle help first (no DB needed)
    if matches!(command, Command::Help) {
        print_help();
        return Ok(());
    }

    info!("gps-ingest-rust bootstrap starting");

    dotenv().ok();
    let database_url = std::env::var("DATABASE_URL")
        .expect("DATABASE_URL must be set");

    let db_pool = PgPoolOptions::new()
        .max_connections(10)
        .connect(&database_url)
        .await?;
    info!("Connected to PostgreSQL successfully");

    match command {
        Command::CalculateDaily => {
            // Calculate for yesterday
            let calculator = DailyStatisticsCalculator::new(db_pool);
            info!("Running daily statistics calculation for yesterday...");
            match calculator.run_daily_job().await {
                Ok(count) => {
                    info!(count, "Daily statistics calculation completed successfully");
                    println!("✅ Calculated daily statistics for {} vehicles", count);
                }
                Err(e) => {
                    error!(?e, "Daily statistics calculation failed");
                    eprintln!("❌ Error: {}", e);
                    std::process::exit(1);
                }
            }
        }
        Command::CalculateDailyFor(date) => {
            // Calculate for specific date
            let calculator = DailyStatisticsCalculator::new(db_pool);
            info!(%date, "Running daily statistics calculation for specific date...");
            match calculator.calculate_for_date(date).await {
                Ok(stats) => {
                    let mut saved_count = 0;
                    for stat in &stats {
                        if let Err(e) = calculator.save_statistics(stat).await {
                            warn!(?e, vehicle_id = stat.vehicle_id, "Failed to save statistics");
                        } else {
                            saved_count += 1;
                        }
                    }
                    info!(saved_count, total = stats.len(), "Daily statistics calculation completed");
                    println!("✅ Calculated daily statistics for {} vehicles (date: {})", saved_count, date);
                }
                Err(e) => {
                    error!(?e, "Daily statistics calculation failed");
                    eprintln!("❌ Error: {}", e);
                    std::process::exit(1);
                }
            }
        }
        Command::Ingest | Command::Help => {
            // Run normal ingestion service
            let osrm_base_url = std::env::var("OSRM_URL").unwrap_or_else(|_| "http://osrm:5000".to_string());
            let database: Arc<dyn TelemetryStore> = Arc::new(db::Database::new(db_pool, osrm_base_url));

            let publisher: Option<Arc<dyn TelemetryEventPublisher>> = match publisher::TelemetryPublisher::from_env().await? {
                Some(p) => {
                    info!("Telemetry publisher connected to RabbitMQ");
                    Some(Arc::new(p))
                }
                None => {
                    warn!("Telemetry publisher disabled (RABBITMQ_HOST not set)");
                    None
                }
            };

            info!("GPS Ingest service running in WRITE-ONLY mode (API served by .NET)");

            let app_config = config::load_config("config/listeners.yaml")?;
            if app_config.listeners.is_empty() {
                warn!("No listeners configured; service will idle");
            }

            transport::run_listeners(&app_config, database, publisher).await?;
        }
    }

    Ok(())
}

#[cfg(test)]
mod real_pipeline_tests {
    use super::*;
    use amqprs::channel::{
        BasicGetArguments, Channel, QueueBindArguments, QueueDeclareArguments, QueueDeleteArguments,
    };
    use amqprs::connection::{Connection, OpenConnectionArguments};
    use std::time::{SystemTime, UNIX_EPOCH};

    const PROTOCOL: &str = "gps_type_1";
    const HH13_FRAME: &str = "HH130094F80228D3D20099CF4F00000A2926FC04FBE780FB00000000010000000016630B17";

    #[tokio::test]
    #[ignore]
    async fn hh_pipeline_persists_and_publishes_real() -> Result<()> {
        dotenv().ok();

        // Database setup with UTF-8 client encoding and English messages to avoid Utf8Error
        let database_url = std::env::var("DATABASE_URL")?;
        let url_with_opts = if database_url.contains('?') {
            format!("{}&options=-c%20client_encoding%3DUTF8%20-c%20lc_messages%3Den_US.UTF-8", database_url)
        } else {
            format!("{}?options=-c%20client_encoding%3DUTF8%20-c%20lc_messages%3Den_US.UTF-8", database_url)
        };
        let pool = PgPoolOptions::new().max_connections(1).connect(&url_with_opts).await?;
        let osrm_url = std::env::var("OSRM_URL").unwrap_or_else(|_| "http://localhost:5000".to_string());
        let database = db::Database::new(pool.clone(), osrm_url);
        ensure_schema(&pool).await?;

        // RabbitMQ connection + temp queue
        let host = std::env::var("RABBITMQ_HOST")?;
        let port = std::env::var("RABBITMQ_PORT").ok().and_then(|p| p.parse().ok()).unwrap_or(5672);
        let username = std::env::var("RABBITMQ_USERNAME").unwrap_or_else(|_| "guest".into());
        let password = std::env::var("RABBITMQ_PASSWORD").unwrap_or_else(|_| "guest".into());
        let exchange = std::env::var("RABBITMQ_EXCHANGE").unwrap_or_else(|_| "telemetry.raw".into());
        let routing_key = std::env::var("RABBITMQ_ROUTING_KEY").unwrap_or_else(|_| "hh".into());

        let connection = Connection::open(&OpenConnectionArguments::new(&host, port, &username, &password)).await?;
        let channel = connection.open_channel(None).await?;
        let queue_name = format!(
            "hh_pipeline_test_{}",
            SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_millis()
        );
        declare_temp_queue(&channel, &queue_name, &exchange, &routing_key).await?;

        // Info frame -> device learn
        let imei = format!(
            "861001{:010}",
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap()
                .as_millis() % 1_000_000_0000
        );
        let info_payload = format!(
            "HH011.0.103R10, ICC:8921602050440128136F, IMEI:{}",
            imei
        );
        let info = telemetry::hh::parse_info_frame(&info_payload)?;
        let imei = database.ingest_info_frame(PROTOCOL, &info).await?;
        assert!(imei.starts_with("861001"));

        // Telemetry frame -> DB insert
        let frame = telemetry::hh::parse_frame(HH13_FRAME)?;
        database.ingest_hh_frame(&imei, PROTOCOL, &frame).await?;

        // Publish + assert message arrives
        let publisher = publisher::TelemetryPublisher::from_env()
            .await?
            .expect("publisher should be enabled for real pipeline test");
        publisher.publish_hh_frame(&imei, PROTOCOL, &frame).await?;

        let payload = wait_for_message(&channel, &queue_name).await?;
        let body = String::from_utf8(payload).expect("payload utf8");
        assert!(body.contains(&imei));

        // DB check (at least one position stored for IMEI)
        let count: i64 = sqlx::query_scalar(
            r#"
            SELECT COUNT(*)
            FROM positions p
            JOIN devices d ON d.id = p.device_id
            WHERE d.device_uid = $1
            "#,
        )
        .bind(&imei)
        .fetch_one(&pool)
        .await?;
        assert!(count > 0, "position row should exist for device");

        // Cleanup queue
        channel
            .queue_delete(QueueDeleteArguments::new(&queue_name))
            .await
            .ok();
        connection.close().await.ok();

        Ok(())
    }

    async fn declare_temp_queue(
        channel: &Channel,
        queue_name: &str,
        exchange: &str,
        routing_key: &str,
    ) -> Result<()> {
        channel
            .queue_declare(
                QueueDeclareArguments::new(queue_name)
                    .durable(false)
                    .exclusive(true)
                    .auto_delete(true)
                    .finish(),
            )
            .await?;
        channel
            .queue_bind(QueueBindArguments::new(queue_name, exchange, routing_key))
            .await?;
        Ok(())
    }

    async fn wait_for_message(channel: &Channel, queue_name: &str) -> Result<Vec<u8>> {
        for _ in 0..10 {
            let args = BasicGetArguments::new(queue_name).no_ack(true).finish();
            if let Some((_msg, _props, content)) = channel
                .basic_get(args)
                .await?
            {
                return Ok(content);
            }
            tokio::time::sleep(std::time::Duration::from_millis(200)).await;
        }
        anyhow::bail!("no message received from RabbitMQ queue {queue_name}")
    }

    async fn ensure_schema(pool: &sqlx::Pool<sqlx::Postgres>) -> Result<()> {
        sqlx::query(
            r#"
            CREATE TABLE IF NOT EXISTS devices (
                id BIGSERIAL PRIMARY KEY,
                device_uid TEXT UNIQUE NOT NULL,
                label TEXT,
                protocol_type TEXT,
                created_at TIMESTAMPTZ DEFAULT NOW(),
                updated_at TIMESTAMPTZ DEFAULT NOW()
            )
            "#,
        )
        .execute(pool)
        .await?;

        sqlx::query(
            r#"
            CREATE TABLE IF NOT EXISTS positions (
                id BIGSERIAL PRIMARY KEY,
                device_id BIGINT REFERENCES devices(id),
                recorded_at TIMESTAMPTZ NOT NULL,
                lat DOUBLE PRECISION NOT NULL,
                lng DOUBLE PRECISION NOT NULL,
                speed_kph DOUBLE PRECISION,
                course_deg DOUBLE PRECISION,
                altitude_m DOUBLE PRECISION,
                ignition_on BOOLEAN,
                fuel_raw SMALLINT,
                power_voltage SMALLINT,
                mems_x SMALLINT,
                mems_y SMALLINT,
                mems_z SMALLINT,
                rpm INTEGER,
                send_flag INTEGER,
                added_info BIGINT,
                signal_quality INTEGER,
                satellites INTEGER,
                is_valid BOOLEAN,
                is_real_time BOOLEAN,
                raw_payload TEXT,
                metadata JSONB,
                created_at TIMESTAMPTZ DEFAULT NOW()
            )
            "#,
        )
        .execute(pool)
        .await?;

        sqlx::query(
            r#"
            CREATE TABLE IF NOT EXISTS device_events (
                id BIGSERIAL PRIMARY KEY,
                device_id BIGINT REFERENCES devices(id),
                recorded_at TIMESTAMPTZ NOT NULL,
                event_type TEXT NOT NULL,
                payload JSONB,
                raw_flag SMALLINT,
                created_at TIMESTAMPTZ DEFAULT NOW()
            )
            "#,
        )
        .execute(pool)
        .await?;

        Ok(())
    }
}

#[cfg(test)]
mod db_connectivity_tests {
    use super::*;
    use anyhow::Context;

    #[tokio::test]
    #[ignore]
    async fn database_connectivity_smoke() -> Result<()> {
        dotenv().ok();
        let database_url = std::env::var("DATABASE_URL")?;
        // Force UTF-8 client encoding to avoid Utf8Error from non-UTF8 server messages
        let url_with_opts = if database_url.contains('?') {
            format!("{}&options=-c%20client_encoding%3DUTF8", database_url)
        } else {
            format!("{}?options=-c%20client_encoding%3DUTF8", database_url)
        };
        let pool = PgPoolOptions::new()
            .max_connections(1)
            .connect(&url_with_opts)
            .await?;

        let value: i32 = sqlx::query_scalar("SELECT 1")
            .fetch_one(&pool)
            .await
            .context("sqlx failed to execute SELECT 1")?;

        assert_eq!(value, 1, "database connectivity smoke test");
        Ok(())
    }
}
