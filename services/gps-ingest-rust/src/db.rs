use anyhow::{Context, Result};
use async_trait::async_trait;
use serde_json::json;
use sqlx::{postgres::PgPool, Row};

use crate::{
    ports::TelemetryStore,
    telemetry::model::{HhFrame, HhInfoFrame},
};

pub struct Database {
    pool: PgPool,
}

impl Database {
    async fn ingest_hh_frame_impl(
        &self,
        device_uid: &str,
        protocol_type: &str,
        frame: &HhFrame,
    ) -> Result<()> {
        let device_id = self.ensure_device(device_uid, protocol_type).await?;
        let _position_id = self.insert_position(device_id, frame).await?;

        if frame.send_flag != 0 {
            let _event_id = self.insert_event(device_id, frame).await?;
        }

        Ok(())
    }

    async fn ingest_info_frame_impl(&self, protocol_type: &str, info: &HhInfoFrame) -> Result<String> {
        let imei = info
            .imei
            .as_ref()
            .context("HH info frame missing IMEI")?
            .trim()
            .to_string();

        let label = info
            .firmware_version
            .trim()
            .to_string();

        sqlx::query(
            r#"
            INSERT INTO devices (device_uid, label, protocol_type, updated_at)
            VALUES ($1, $2, $3, NOW())
            ON CONFLICT (device_uid) DO UPDATE
                SET label = EXCLUDED.label,
                    protocol_type = EXCLUDED.protocol_type,
                    updated_at = NOW()
            "#,
        )
        .bind(&imei)
        .bind(label)
        .bind(protocol_type)
        .execute(&self.pool)
        .await?;

        Ok(imei)
    }
}

#[async_trait]
impl TelemetryStore for Database {
    async fn ingest_info_frame(&self, protocol_type: &str, info: &HhInfoFrame) -> Result<String> {
        self.ingest_info_frame_impl(protocol_type, info).await
    }

    async fn ingest_hh_frame(
        &self,
        device_uid: &str,
        protocol_type: &str,
        frame: &HhFrame,
    ) -> Result<()> {
        self.ingest_hh_frame_impl(device_uid, protocol_type, frame).await
    }
}

impl Database {
    pub fn new(pool: PgPool) -> Self {
        Self { pool }
    }

    async fn ensure_device(&self, device_uid: &str, protocol_type: &str) -> Result<i32> {
        let row = sqlx::query(
            r#"
            INSERT INTO devices (device_uid, protocol_type)
            VALUES ($1, $2)
            ON CONFLICT (device_uid) DO UPDATE
                SET protocol_type = EXCLUDED.protocol_type,
                    updated_at = NOW()
            RETURNING id
            "#,
        )
        .bind(device_uid)
        .bind(protocol_type)
        .fetch_one(&self.pool)
        .await?;

        Ok(row.get::<i32, _>("id"))
    }

    async fn insert_position(&self, device_id: i32, frame: &HhFrame) -> Result<i32> {
        let metadata = json!({
            "power_source_rescue": frame.power_source_rescue,
            "temperature_raw": frame.temperature_raw,
            "remaining_payload": frame.remaining_payload,
        });

        let row = sqlx::query(
            r#"
            INSERT INTO positions (
                device_id,
                recorded_at,
                lat,
                lng,
                speed_kph,
                course_deg,
                altitude_m,
                ignition_on,
                fuel_raw,
                power_voltage,
                mems_x,
                mems_y,
                mems_z,
                rpm,
                send_flag,
                added_info,
                signal_quality,
                satellites,
                is_valid,
                is_real_time,
                raw_payload,
                metadata
            ) VALUES (
                $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19, $20, $21, $22
            )
            RETURNING id
            "#,
        )
        .bind(device_id)
        .bind(frame.recorded_at)
        .bind(frame.latitude)
        .bind(frame.longitude)
        .bind(frame.speed_kph)
        .bind(frame.heading_deg)
        .bind(Option::<f64>::None)
        .bind(frame.ignition_on)
        .bind(i16::from(frame.fuel_raw))
        .bind(i16::from(frame.power_voltage))
        .bind(i16::from(frame.mems_x))
        .bind(i16::from(frame.mems_y))
        .bind(i16::from(frame.mems_z))
        .bind(frame.rpm.map(|v| i32::from(v)))
        .bind(i32::from(frame.send_flag))
        .bind(i64::from(frame.added_info))
        .bind(frame.signal_quality.map(|v| i32::from(v)))
        .bind(frame.satellites_in_view.map(|v| i32::from(v)))
        .bind(frame.is_valid)
        .bind(frame.is_real_time)
        .bind(&frame.raw_payload)
        .bind(metadata)
        .fetch_one(&self.pool)
        .await?;

        Ok(row.get::<i32, _>("id"))
    }

    async fn insert_event(&self, device_id: i32, frame: &HhFrame) -> Result<i32> {
        let event_type = map_send_flag(frame.send_flag);
        let payload = json!({
            "send_flag": frame.send_flag,
            "added_info": frame.added_info,
            "raw_payload": frame.raw_payload,
        });

        let row = sqlx::query(
            r#"
            INSERT INTO device_events (
                device_id,
                recorded_at,
                event_type,
                event_data
            ) VALUES ($1, $2, $3, $4)
            RETURNING id
            "#,
        )
        .bind(device_id)
        .bind(frame.recorded_at)
        .bind(event_type)
        .bind(payload)
        .fetch_one(&self.pool)
        .await?;

        Ok(row.get::<i32, _>("id"))
    }
}

fn map_send_flag(flag: u8) -> &'static str {
    match flag {
        0 => "CMDUSER",
        1 => "SENDP",
        2 => "GPSVAL",
        3 => "CAPDEV",
        4 => "IOCHANGE",
        5 => "OVERSPEED",
        6 => "JERCK",
        7 => "IBUTTON",
        8 => "I1_EVENT",
        9 => "I2_EVENT",
        10 => "I3_EVENT",
        11 => "ALERT",
        _ => "UNKNOWN",
    }
}
