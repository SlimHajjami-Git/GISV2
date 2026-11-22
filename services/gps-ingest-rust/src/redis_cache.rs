use std::env;
use anyhow::{Context, Result};
use deadpool_redis::{Config, Pool, Runtime};
use redis::AsyncCommands;
use serde_json::json;
use tracing::info;

use crate::telemetry::model::HhFrame;

const POSITION_TTL_SECONDS: u64 = 2100; // 35 minutes TTL (covers 30-min ignition-off throttle)
const POOL_SIZE: usize = 16; // Connection pool size

/// Moteur tournant, une valeur de carburant n'est reprise d'une trame précédente que
/// pendant ce délai après sa lecture.
const FUEL_CARRY_MAX_MINUTES_RUNNING: i64 = 10;

/// Carburant à publier pour une trame, et l'instant jusqu'auquel il est tenu pour vrai.
///
/// Beaucoup de trames arrivent sans carburant (0) : le boîtier ne lit le CAN que moteur
/// tournant, et certaines trames n'en portent pas. On reprend alors la dernière valeur du
/// cache, mais plus sans limite : chaque écriture relançait l'expiration de 35 minutes, si
/// bien qu'une valeur pouvait survivre des semaines. Recette du 20/11/2026 : Caddy
/// 263 TU 6996 (PARENIN), boîtier qui ne lit pas le CAN, affichait 70 % le 18/11 alors que
/// sa dernière vraie valeur datait du 24/09.
///
/// - trame avec carburant : on la prend, lue à l'instant de la trame ;
/// - contact coupé : on reprend la dernière valeur, qui reste vraie (le niveau ne bouge pas
///   à l'arrêt) — c'est le cas de la plupart des trames sans carburant mesurées le 20/11 :
///   un véhicule garé envoie une trame toutes les 30 minutes, sans CAN ;
/// - moteur tournant : on la reprend au plus FUEL_CARRY_MAX_MINUTES_RUNNING minutes après
///   la dernière confirmation ; au-delà on publie 0 (« pas de valeur »), que le serveur
///   affiche comme absent au lieu d'un niveau figé.
///
/// Les entrées écrites avant ce changement n'ont pas de « fuelAt » : leur « recordedAt »
/// en tient lieu.
fn fuel_to_publish(
    frame_fuel: u8,
    ignition_on: bool,
    recorded_at: chrono::DateTime<chrono::Utc>,
    prev: Option<&serde_json::Value>,
) -> (u8, Option<chrono::DateTime<chrono::Utc>>) {
    if frame_fuel > 0 {
        return (frame_fuel, Some(recorded_at));
    }

    let Some(prev) = prev else { return (0, None) };
    let prev_fuel = prev["fuelRaw"].as_u64().unwrap_or(0);
    if prev_fuel == 0 || prev_fuel > u8::MAX as u64 {
        return (0, None);
    }
    let prev_at = prev["fuelAt"]
        .as_str()
        .or_else(|| prev["recordedAt"].as_str())
        .and_then(|s| chrono::DateTime::parse_from_rfc3339(s).ok())
        .map(|t| t.with_timezone(&chrono::Utc));
    let Some(prev_at) = prev_at else { return (0, None) };

    if !ignition_on {
        // À l'arrêt, la valeur est confirmée à cette trame.
        return (prev_fuel as u8, Some(recorded_at.max(prev_at)));
    }
    if recorded_at - prev_at <= chrono::Duration::minutes(FUEL_CARRY_MAX_MINUTES_RUNNING) {
        return (prev_fuel as u8, Some(prev_at));
    }
    (0, None)
}

pub struct RedisCache {
    pool: Pool,
}

impl RedisCache {
    pub async fn from_env() -> Result<Option<Self>> {
        let redis_url = match env::var("REDIS_URL").ok() {
            Some(url) if !url.is_empty() => url,
            _ => return Ok(None),
        };

        // Create connection pool configuration
        let cfg = Config::from_url(redis_url.clone());
        let pool = cfg.builder()
            .map_err(|e| anyhow::anyhow!("Failed to create pool builder: {}", e))?
            .max_size(POOL_SIZE)
            .runtime(Runtime::Tokio1)
            .build()
            .map_err(|e| anyhow::anyhow!("Failed to build pool: {}", e))?;

        // Test connection
        let mut conn = pool.get().await
            .with_context(|| "Failed to get connection from pool")?;
        
        let _: String = redis::cmd("PING").query_async(&mut conn).await
            .with_context(|| "Redis PING failed")?;

        info!("Connected to Redis at {} with pool size {}", redis_url, POOL_SIZE);
        Ok(Some(Self { pool }))
    }

    pub async fn cache_position(
        &self,
        device_uid: &str,
        vehicle_id: Option<i32>,
        company_id: i32,
        frame: &HhFrame,
    ) -> Result<()> {
        let mut conn = self.pool.get().await
            .with_context(|| "Failed to get Redis connection from pool")?;

        // Preserve last known good values from previous cache entry
        // Use GETDEL-like pattern: read previous in same pipeline as write (below)
        let device_key = format!("vehicle:position:{}", device_uid);
        let needs_fallback = frame.fuel_raw == 0 || frame.battery_raw == 0 || frame.odometer_km == 1048574;
        let prev_cache: Option<serde_json::Value> = if needs_fallback {
            // Read previous cache when we need fallback values (fuel=0 or odometer artifact)
            conn.get::<_, Option<String>>(&device_key).await
                .ok()
                .flatten()
                .and_then(|s| serde_json::from_str(&s).ok())
        } else {
            None // No need to read previous — current frame has all data
        };

        // Carburant : la dernière valeur connue n'est reprise que tant qu'elle reste vraie
        // (voir fuel_to_publish).
        let (fuel_raw, fuel_at) = fuel_to_publish(
            frame.fuel_raw,
            frame.ignition_on,
            frame.recorded_at.and_utc(),
            prev_cache.as_ref(),
        );

        // Tension batterie = octet « Batterie » (34-36) × 0,156 V (doc constructeur,
        // 17/09/2026 : 40 V / 256). Le champ Power (32-34, ex-facteur 0,3) ne
        // mesure rien sur 88 % de la flotte et n'est plus utilisé pour l'affichage.
        // 0 = pas de mesure : on garde la dernière valeur connue du cache, sinon null
        // (le .NET masque de toute façon tant que l'audit n'a pas validé le capteur).
        const BATTERY_VOLTAGE_FACTOR: f64 = 0.156;
        const BATTERY_MIN_V: f64 = 11.0; // 12V system: 0%
        const BATTERY_MAX_V: f64 = 12.8; // 12V system: 100%

        let battery_voltage: Option<f64> = if frame.battery_raw > 0 {
            Some(frame.battery_raw as f64 * BATTERY_VOLTAGE_FACTOR)
        } else {
            prev_cache.as_ref().and_then(|c| c["batteryVoltage"].as_f64())
        };
        let battery_percent: Option<i32> = battery_voltage.map(|v| {
            if v <= BATTERY_MIN_V { 0 }
            else if v >= BATTERY_MAX_V { 100 }
            else { ((v - BATTERY_MIN_V) / (BATTERY_MAX_V - BATTERY_MIN_V) * 100.0) as i32 }
        });

        // Temperature: ONLY use FMS temperature from CAN bus (V3 frames, J1939 raw - 40)
        // The base frame temperature_raw is "80+Analogic1 or digital temp" (raw analog value),
        // NOT a J1939 temperature — applying -40 to it gives wrong readings.
        // Do NOT preserve previous cache value: devices without FMS temp would keep stale data forever.
        let temperature_c: Option<i16> = frame.fms_temperature_c
            .filter(|&t| t != 0 && t > -50 && t < 200);

        let position_data = json!({
            "deviceUid": device_uid,
            "vehicleId": vehicle_id,
            "companyId": company_id,
            "latitude": frame.latitude,
            "longitude": frame.longitude,
            "speedKph": frame.speed_kph,
            "headingDeg": frame.heading_deg,
            "ignitionOn": frame.ignition_on,
            "isValid": frame.is_valid,
            "fuelRaw": fuel_raw,
            "fuelAt": fuel_at.map(|t| t.to_rfc3339()),
            "powerVoltage": frame.power_voltage,
            "batteryRaw": frame.battery_raw,
            "batteryVoltage": battery_voltage.map(|v| (v * 10.0).round() / 10.0),
            "batteryPercent": battery_percent,
            "powerSourceRescue": frame.power_source_rescue,
            "temperatureC": temperature_c,
            "odometerKm": if frame.odometer_km != 1048574 {
                frame.odometer_km
            } else {
                prev_cache.as_ref()
                    .and_then(|c| c["odometerKm"].as_u64())
                    .unwrap_or(0) as u32
            },
            "rpm": frame.rpm,
            "memsX": frame.mems_x,
            "memsY": frame.mems_y,
            "memsZ": frame.mems_z,
            "recordedAt": frame.recorded_at.and_utc().to_rfc3339(),
            "cachedAt": chrono::Utc::now().to_rfc3339(),
        });

        let position_json = serde_json::to_string(&position_data)?;

        // Store position by device (reuse device_key from above)
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

    /// Refresh TTL on existing cached position without overwriting data.
    /// Used for throttled ignition-off frames: keeps the vehicle visible on the map
    /// without pushing unvalidated odometer/coordinate values.
    pub async fn refresh_position_ttl(
        &self,
        device_uid: &str,
        company_id: i32,
    ) -> Result<()> {
        let mut conn = self.pool.get().await
            .with_context(|| "Failed to get Redis connection from pool")?;

        let device_key = format!("vehicle:position:{}", device_uid);
        conn.expire::<_, ()>(&device_key, POSITION_TTL_SECONDS as i64).await?;

        let company_key = format!("company:{}:active_vehicles", company_id);
        conn.expire::<_, ()>(&company_key, POSITION_TTL_SECONDS as i64).await?;

        let devices_key = format!("company:{}:devices", company_id);
        conn.expire::<_, ()>(&devices_key, POSITION_TTL_SECONDS as i64).await?;

        Ok(())
    }

    pub async fn get_position(&self, device_uid: &str) -> Result<Option<String>> {
        let mut conn = self.pool.get().await
            .with_context(|| "Failed to get Redis connection from pool")?;
        let key = format!("vehicle:position:{}", device_uid);
        let result: Option<String> = conn.get(&key).await?;
        Ok(result)
    }

    pub async fn get_company_vehicles(&self, company_id: i32) -> Result<Vec<i32>> {
        let mut conn = self.pool.get().await
            .with_context(|| "Failed to get Redis connection from pool")?;
        let key = format!("company:{}:active_vehicles", company_id);
        let result: Vec<i32> = conn.smembers(&key).await?;
        Ok(result)
    }

    /// OPTIMIZED: Uses company device index + MGET instead of KEYS pattern scan
    /// Complexity: O(n) where n = devices in company, instead of O(N) where N = all keys in Redis
    pub async fn get_all_positions_for_company(&self, company_id: i32) -> Result<Vec<String>> {
        let mut conn = self.pool.get().await
            .with_context(|| "Failed to get Redis connection from pool")?;
        
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

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::{TimeZone, Utc};
    use serde_json::json;

    fn t(h: u32, m: u32) -> chrono::DateTime<Utc> {
        Utc.with_ymd_and_hms(2026, 11, 20, h, m, 0).unwrap()
    }

    #[test]
    fn trame_avec_carburant_est_prise_telle_quelle() {
        let prev = json!({ "fuelRaw": 70, "fuelAt": t(8, 0).to_rfc3339() });
        assert_eq!(fuel_to_publish(19, true, t(9, 0), Some(&prev)), (19, Some(t(9, 0))));
    }

    #[test]
    fn moteur_tournant_valeur_recente_reprise() {
        let prev = json!({ "fuelRaw": 19, "fuelAt": t(9, 0).to_rfc3339() });
        assert_eq!(fuel_to_publish(0, true, t(9, 10), Some(&prev)), (19, Some(t(9, 0))));
    }

    #[test]
    fn moteur_tournant_valeur_figee_abandonnee() {
        // Caddy 263 TU 6996 : boîtier sans CAN, dernière vraie valeur le 24/09.
        let prev = json!({ "fuelRaw": 70, "fuelAt": "2026-09-24T19:48:43+00:00" });
        assert_eq!(fuel_to_publish(0, true, t(9, 27), Some(&prev)), (0, None));
        // Au-delà de 10 minutes, même de peu.
        let prev = json!({ "fuelRaw": 19, "fuelAt": t(9, 0).to_rfc3339() });
        assert_eq!(fuel_to_publish(0, true, t(9, 11), Some(&prev)), (0, None));
    }

    #[test]
    fn contact_coupe_valeur_gardee_et_confirmee() {
        // Garé depuis 3 h : le niveau n'a pas bougé, la valeur reste vraie.
        let prev = json!({ "fuelRaw": 57, "fuelAt": t(7, 20).to_rfc3339() });
        assert_eq!(fuel_to_publish(0, false, t(10, 25), Some(&prev)), (57, Some(t(10, 25))));
    }

    #[test]
    fn redemarrage_peu_apres_une_trame_a_l_arret_garde_la_valeur() {
        // Dernière trame à l'arrêt à 10 h 25 a confirmé 57 ; démarrage à 10 h 30 sans CAN.
        let prev = json!({ "fuelRaw": 57, "fuelAt": t(10, 25).to_rfc3339() });
        assert_eq!(fuel_to_publish(0, true, t(10, 30), Some(&prev)), (57, Some(t(10, 25))));
    }

    #[test]
    fn ancienne_entree_sans_fuel_at_se_date_par_recorded_at() {
        let prev = json!({ "fuelRaw": 70, "recordedAt": t(9, 0).to_rfc3339() });
        assert_eq!(fuel_to_publish(0, true, t(9, 5), Some(&prev)), (70, Some(t(9, 0))));
        assert_eq!(fuel_to_publish(0, true, t(9, 30), Some(&prev)), (0, None));
    }

    #[test]
    fn rien_a_reprendre() {
        assert_eq!(fuel_to_publish(0, true, t(9, 0), None), (0, None));
        let vide = json!({ "fuelRaw": 0, "fuelAt": null, "recordedAt": t(8, 59).to_rfc3339() });
        assert_eq!(fuel_to_publish(0, false, t(9, 0), Some(&vide)), (0, None));
        let sans_date = json!({ "fuelRaw": 40 });
        assert_eq!(fuel_to_publish(0, false, t(9, 0), Some(&sans_date)), (0, None));
    }
}
