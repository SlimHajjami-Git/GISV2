use std::env;
use anyhow::{Context, Result};
use redis::{AsyncCommands, Client};
use serde_json::json;
use tracing::{info, warn};

use crate::telemetry::model::HhFrame;

const POSITION_TTL_SECONDS: u64 = 300; // 5 minutes TTL

pub struct RedisCache {
    client: Client,
}

impl RedisCache {
    pub async fn from_env() -> Result<Option<Self>> {
        let redis_url = match env::var("REDIS_URL").ok() {
            Some(url) if !url.is_empty() => url,
            _ => return Ok(None),
        };

        let client = Client::open(redis_url.as_str())
            .with_context(|| format!("Failed to create Redis client with URL: {}", redis_url))?;

        // Test connection
        let mut conn = client.get_multiplexed_async_connection().await
            .with_context(|| "Failed to connect to Redis")?;
        
        let _: String = redis::cmd("PING").query_async(&mut conn).await
            .with_context(|| "Redis PING failed")?;

        info!("Connected to Redis at {}", redis_url);
        Ok(Some(Self { client }))
    }

    pub async fn cache_position(
        &self,
        device_uid: &str,
        vehicle_id: Option<i32>,
        company_id: i32,
        frame: &HhFrame,
    ) -> Result<()> {
        let mut conn = self.client.get_multiplexed_async_connection().await?;

        let position_data = json!({
            "device_uid": device_uid,
            "vehicle_id": vehicle_id,
            "company_id": company_id,
            "latitude": frame.latitude,
            "longitude": frame.longitude,
            "speed_kph": frame.speed_kph,
            "heading_deg": frame.heading_deg,
            "ignition_on": frame.ignition_on,
            "is_valid": frame.is_valid,
            "fuel_raw": frame.fuel_raw,
            "power_voltage": frame.power_voltage,
            "recorded_at": frame.recorded_at.and_utc().to_rfc3339(),
            "cached_at": chrono::Utc::now().to_rfc3339(),
        });

        let position_json = serde_json::to_string(&position_data)?;

        // Store position by device
        let device_key = format!("vehicle:position:{}", device_uid);
        conn.set_ex::<_, _, ()>(&device_key, &position_json, POSITION_TTL_SECONDS).await?;

        // Add to company's active vehicles set
        if let Some(vid) = vehicle_id {
            let company_key = format!("company:{}:active_vehicles", company_id);
            conn.sadd::<_, _, ()>(&company_key, vid).await?;
            conn.expire::<_, ()>(&company_key, POSITION_TTL_SECONDS as i64).await?;
        }

        // Index device_uid by company for fast lookup (avoids KEYS pattern scan)
        let devices_key = format!("company:{}:devices", company_id);
        conn.sadd::<_, _, ()>(&devices_key, device_uid).await?;
        conn.expire::<_, ()>(&devices_key, POSITION_TTL_SECONDS as i64).await?;

        // Publish update event for real-time subscribers
        let channel = format!("vehicle:updates:{}", company_id);
        conn.publish::<_, _, ()>(&channel, &position_json).await?;

        Ok(())
    }

    pub async fn get_position(&self, device_uid: &str) -> Result<Option<String>> {
        let mut conn = self.client.get_multiplexed_async_connection().await?;
        let key = format!("vehicle:position:{}", device_uid);
        let result: Option<String> = conn.get(&key).await?;
        Ok(result)
    }

    pub async fn get_company_vehicles(&self, company_id: i32) -> Result<Vec<i32>> {
        let mut conn = self.client.get_multiplexed_async_connection().await?;
        let key = format!("company:{}:active_vehicles", company_id);
        let result: Vec<i32> = conn.smembers(&key).await?;
        Ok(result)
    }

    /// OPTIMIZED: Uses company device index + MGET instead of KEYS pattern scan
    /// Complexity: O(n) where n = devices in company, instead of O(N) where N = all keys in Redis
    pub async fn get_all_positions_for_company(&self, company_id: i32) -> Result<Vec<String>> {
        let mut conn = self.client.get_multiplexed_async_connection().await?;
        
        // Get device UIDs indexed by company (O(n) where n = company devices)
        let devices_key = format!("company:{}:devices", company_id);
        let device_uids: Vec<String> = conn.smembers(&devices_key).await?;

        if device_uids.is_empty() {
            return Ok(vec![]);
        }

        // Build position keys and use MGET for batch retrieval (single round-trip)
        let position_keys: Vec<String> = device_uids
            .iter()
            .map(|uid| format!("vehicle:position:{}", uid))
            .collect();

        // MGET retrieves all positions in a single Redis command
        let positions: Vec<Option<String>> = conn.mget(&position_keys).await?;

        // Filter out None values and return
        Ok(positions.into_iter().flatten().collect())
    }
}
