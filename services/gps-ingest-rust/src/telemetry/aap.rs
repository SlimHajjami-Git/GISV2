//! AAP Protocol Decoder - Port from GISV1 AAP.cs
//! 
//! This module implements the AAP (ACI) protocol decoder for GPS trackers.
//! It supports protocol versions 1, 2, and 3 with FMS and MEMS data parsing.

use anyhow::{bail, Context, Result};
use chrono::{NaiveDate, NaiveDateTime, NaiveTime};

/// Supported protocol headers
const VALID_HEADERS: [&str; 2] = ["HH", "AA"];

/// Frame types based on header byte
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AapFrameType {
    Connect,           // AA00/HH00 - Connection frame
    Info,              // AA01/HH01 - Info frame (firmware, IMEI)
    SoftwareReset,     // AA02/HH02
    GsmReset,          // AA03/HH03
    TimeRequest,       // AA07/HH07
    RealTimeHistory,   // AA1X/HH1X - Real-time + history
    History,           // AA2X/HH2X - History only
    RealTime,          // AA3X/HH3X - Real-time only
    Unknown,
}

/// Protocol version (affects FMS data parsing)
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AapProtocolVersion {
    V1,  // Basic, 70 chars
    V2,  // Extended, 70-90 chars
    V3,  // Full FMS, >90 chars
}

/// FMS (Fleet Management System) data from CAN bus
#[derive(Debug, Clone, Default)]
pub struct FmsData {
    pub fuel_percent: Option<u8>,
    pub temperature_c: Option<i16>,      // Celsius (raw - 40)
    pub odometer_km: Option<u32>,
    pub speed_kph: Option<u8>,
    pub rpm: Option<u16>,
    pub fuel_rate_l_per_100km: Option<f64>,
    pub total_fuel_used_liters: Option<u32>,
    pub has_data: bool,
}

/// MEMS (Accelerometer) data
#[derive(Debug, Clone, Default)]
pub struct MemsData {
    pub accel_x: f64,  // G-force (-2 to +2)
    pub accel_y: f64,
    pub accel_z: f64,
    pub raw_x: i8,
    pub raw_y: i8,
    pub raw_z: i8,
    pub event: Option<DrivingEvent>,
}

/// Driving event detected from MEMS data
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DrivingEvent {
    HarshBraking,
    HarshAcceleration,
    SharpTurn,
    SpeedBump,
    Pothole,
    HighRpm,
    Overspeeding,
}

/// Complete parsed AAP frame
#[derive(Debug, Clone)]
pub struct AapFrame {
    pub frame_type: AapFrameType,
    pub protocol_version: AapProtocolVersion,
    pub recorded_at: NaiveDateTime,
    pub latitude: f64,
    pub longitude: f64,
    pub speed_kph: f64,
    pub heading_deg: f64,
    pub power_voltage: u8,
    pub power_source_rescue: bool,
    pub fuel_percent: u8,
    pub ignition_on: bool,
    pub is_valid: bool,
    pub is_real_time: bool,
    pub odometer_km: u32,
    pub temperature_raw: u16,
    pub send_flag: u8,
    pub added_info: u32,
    pub signal_quality: Option<u8>,
    pub satellites: Option<u8>,
    pub mems: MemsData,
    pub fms: FmsData,
    pub raw_payload: String,
}

/// AAP Info frame (connection/identification)
#[derive(Debug, Clone)]
pub struct AapInfoFrame {
    pub firmware_version: String,
    pub icc_id: Option<String>,
    pub imei: Option<String>,
    pub device_id: Option<String>,
    pub timestamp: Option<NaiveDateTime>,
}

/// Main AAP decoder
pub struct AapDecoder;

impl AapDecoder {
    /// Parse any AAP frame (auto-detect type)
    pub fn parse(payload: &str) -> Result<AapFrame> {
        let payload = payload.trim();
        
        if !VALID_HEADERS.iter().any(|h| payload.starts_with(h)) {
            bail!("Invalid header: must start with HH or AA");
        }

        let frame_type = Self::detect_frame_type(payload);
        
        match frame_type {
            AapFrameType::Connect | AapFrameType::Info => {
                bail!("Use parse_info_frame for connect/info frames");
            }
            AapFrameType::SoftwareReset | AapFrameType::GsmReset | AapFrameType::TimeRequest => {
                bail!("System frame, no data to parse");
            }
            _ => Self::parse_data_frame(payload),
        }
    }

    /// Parse info/connect frame
    pub fn parse_info_frame(payload: &str) -> Result<AapInfoFrame> {
        let payload = payload.trim();
        
        // Handle prefixed frames like "NR08G0663 AA00..."
        let effective_payload = if let Some(pos) = payload.find("AA00").or_else(|| payload.find("HH00")) {
            &payload[pos..]
        } else if let Some(pos) = payload.find("AA01").or_else(|| payload.find("HH01")) {
            &payload[pos..]
        } else {
            payload
        };

        let frame_type = Self::detect_frame_type(effective_payload);
        
        match frame_type {
            AapFrameType::Connect => Self::parse_connect_frame(effective_payload),
            AapFrameType::Info => Self::parse_info_frame_internal(effective_payload),
            _ => bail!("Not an info/connect frame"),
        }
    }

    /// Check if frame is a system frame (should be ignored)
    pub fn is_system_frame(payload: &str) -> bool {
        let payload = payload.trim();
        matches!(Self::detect_frame_type(payload), 
            AapFrameType::SoftwareReset | AapFrameType::GsmReset | AapFrameType::TimeRequest)
    }

    /// Detect frame type from header
    fn detect_frame_type(payload: &str) -> AapFrameType {
        if payload.len() < 4 {
            return AapFrameType::Unknown;
        }
        
        let header = &payload[2..4];
        match header {
            "00" => AapFrameType::Connect,
            "01" => AapFrameType::Info,
            "02" => AapFrameType::SoftwareReset,
            "03" => AapFrameType::GsmReset,
            "07" => AapFrameType::TimeRequest,
            _ => {
                // Parse XY where X = type, Y = version
                if let Ok(byte) = u8::from_str_radix(header, 16) {
                    let x = byte >> 4;
                    match x {
                        1 => AapFrameType::RealTimeHistory,
                        2 => AapFrameType::History,
                        3 => AapFrameType::RealTime,
                        _ => AapFrameType::Unknown,
                    }
                } else {
                    AapFrameType::Unknown
                }
            }
        }
    }

    /// Detect protocol version from frame
    fn detect_protocol_version(payload: &str) -> AapProtocolVersion {
        if payload.len() < 4 {
            return AapProtocolVersion::V1;
        }
        
        // Check version byte in header
        if let Ok(byte) = u8::from_str_radix(&payload[2..4], 16) {
            let version = byte & 0x0F;
            match version {
                3 => return AapProtocolVersion::V3,
                2 => return AapProtocolVersion::V2,
                1 => return AapProtocolVersion::V1,
                _ => {}
            }
        }
        
        // Fallback: detect by length
        if payload.len() > 90 {
            AapProtocolVersion::V3
        } else if payload.len() > 70 {
            AapProtocolVersion::V2
        } else {
            AapProtocolVersion::V1
        }
    }

    /// Parse data frame (position data)
    fn parse_data_frame(payload: &str) -> Result<AapFrame> {
        if payload.len() < 70 {
            bail!("Payload too short: {} < 70", payload.len());
        }

        let frame_type = Self::detect_frame_type(payload);
        let protocol_version = Self::detect_protocol_version(payload);

        // Parse base fields (same for all versions)
        let hour_raw = &payload[4..10];
        let lat_raw = &payload[10..18];
        let lon_raw = &payload[18..26];
        let speed_raw = &payload[26..30];
        let heading_raw = &payload[30..32];
        let power_raw = &payload[32..34];
        let fuel_raw = &payload[34..36];
        let mems_raw = &payload[36..42];
        let flags_raw = &payload[42..44];
        let temp_raw = &payload[44..48];
        let odo_raw = &payload[48..56];
        let send_flag_raw = &payload[56..58];
        let added_info_raw = &payload[58..66];
        let date_raw = &payload[66..70];

        // Decode timestamp
        let recorded_at = Self::decode_timestamp(hour_raw, date_raw)?;
        
        // Decode flags
        let flags = u8::from_str_radix(flags_raw, 16)?;
        let is_north = (flags & 0x01) != 0;
        let is_east = (flags & 0x02) != 0;
        let ignition_on = (flags & 0x04) != 0;
        let is_valid = (flags & 0x40) != 0;

        // Decode coordinates
        let latitude = Self::decode_coordinate(lat_raw, is_north)?;
        let longitude = Self::decode_coordinate(lon_raw, is_east)?;

        // Decode speed (stored as mph * 10, convert to kph)
        // GISV1 formula: (int)(raw / 10 * 1.609) - truncates to integer
        let speed_raw_val = u32::from_str_radix(speed_raw, 16)?;
        let speed_kph = ((speed_raw_val / 10) as f64) * 1.609;

        // Decode heading
        let heading_deg = u32::from_str_radix(heading_raw, 16)? as f64;

        // Decode power
        let power_val = u8::from_str_radix(power_raw, 16)?;
        let power_source_rescue = power_val >= 128;
        let power_voltage = power_val % 128;

        // Decode base fuel from position 34
        let base_fuel_percent = u8::from_str_radix(fuel_raw, 16)?;

        // Decode MEMS
        let mems = Self::decode_mems(mems_raw, speed_kph)?;

        // Decode other fields
        let temperature_raw = u16::from_str_radix(temp_raw, 16)?;
        let odometer_km = u32::from_str_radix(odo_raw, 16)?;
        let send_flag = u8::from_str_radix(send_flag_raw, 16)?;
        let added_info = u32::from_str_radix(added_info_raw, 16)?;

        // Parse FMS data based on protocol version
        let fms = Self::parse_fms(payload, protocol_version)?;

        // GISV1 Logic: If base fuel is 0, try FMS fuel as fallback
        let fuel_percent = if base_fuel_percent == 0 {
            fms.fuel_percent.unwrap_or(0)
        } else {
            base_fuel_percent
        };

        // Parse extended fields for V3 (signal quality and satellites at positions 70-73)
        let (signal_quality, satellites) = if protocol_version == AapProtocolVersion::V3 && payload.len() >= 74 {
            // For V3, signal quality and satellites are at different positions
            // They come before the FMS block in some implementations
            let sig = payload.get(70..72).and_then(|s| u8::from_str_radix(s, 16).ok());
            let sat = payload.get(72..74).and_then(|s| u8::from_str_radix(s, 16).ok());
            (sig, sat)
        } else {
            (None, None)
        };

        Ok(AapFrame {
            frame_type,
            protocol_version,
            recorded_at,
            latitude,
            longitude,
            speed_kph,
            heading_deg,
            power_voltage,
            power_source_rescue,
            fuel_percent,
            ignition_on,
            is_valid,
            is_real_time: matches!(frame_type, AapFrameType::RealTime | AapFrameType::RealTimeHistory),
            odometer_km,
            temperature_raw,
            send_flag,
            added_info,
            signal_quality,
            satellites,
            mems,
            fms,
            raw_payload: payload.to_string(),
        })
    }

    /// Decode timestamp from hour and date fields
    fn decode_timestamp(hour_raw: &str, date_raw: &str) -> Result<NaiveDateTime> {
        let total_secs = u32::from_str_radix(hour_raw, 16)?;
        let hour = (total_secs / 3600) % 24;
        let minute = (total_secs % 3600) / 60;
        let second = total_secs % 60;

        let date_val = u32::from_str_radix(date_raw, 16)?;
        let day = (date_val % 31) + 1;
        let month = ((date_val / 31) % 12) + 1;
        let year = (date_val / (31 * 12)) + 2000;

        let date = NaiveDate::from_ymd_opt(year as i32, month, day)
            .context("Invalid date in frame")?;
        let time = NaiveTime::from_hms_opt(hour, minute, second)
            .context("Invalid time in frame")?;

        Ok(NaiveDateTime::new(date, time))
    }

    /// Decode coordinate from AAP protocol format
    /// GISV1 formula: String.Format("{0:D2}", raw / 1000000) + "." + String.Format("{0:D6}", (raw % 1000000) * 100 / 60)
    /// Uses INTEGER arithmetic to match GISV1 exactly
    /// 
    /// CRITICAL FIX: GISV1 uses {0:D6} format which ALWAYS pads to 6 digits
    /// So we must ALWAYS divide by 1,000,000 regardless of decimal_int length!
    fn decode_coordinate(raw: &str, is_positive: bool) -> Result<f64> {
        let value = i64::from_str_radix(raw, 16)?;
        
        // GISV1 uses INTEGER division - we must match exactly
        let degrees = value / 1_000_000;  // Integer division
        let minutes_part = value % 1_000_000;
        let decimal_int = (minutes_part * 100) / 60;  // Integer division like C#
        
        // CRITICAL: GISV1 uses D6 format = ALWAYS 6 digits, so ALWAYS divide by 1,000,000
        // This matches hh.rs implementation and fixes precision bug
        let mut coord = degrees as f64 + (decimal_int as f64 / 1_000_000.0);

        if !is_positive {
            coord = -coord;
        }

        Ok(coord)
    }

    /// Decode MEMS accelerometer data
    fn decode_mems(raw: &str, speed_kph: f64) -> Result<MemsData> {
        if raw.len() < 6 {
            return Ok(MemsData::default());
        }

        let raw_x = Self::parse_signed_byte(&raw[0..2])?;
        let raw_y = Self::parse_signed_byte(&raw[2..4])?;
        let raw_z = Self::parse_signed_byte(&raw[4..6])?;

        // Convert to G-force (range -2G to +2G, 64 units per G)
        let accel_x = raw_x as f64 / 64.0;
        let accel_y = raw_y as f64 / 64.0;
        let accel_z = raw_z as f64 / 64.0;

        // Detect driving events
        let event = Self::detect_driving_event(accel_x, accel_y, accel_z, speed_kph, None);

        Ok(MemsData {
            accel_x,
            accel_y,
            accel_z,
            raw_x,
            raw_y,
            raw_z,
            event,
        })
    }

    /// Parse signed byte from hex
    fn parse_signed_byte(raw: &str) -> Result<i8> {
        let value = u8::from_str_radix(raw, 16)?;
        Ok(if value >= 128 {
            (value as i16 - 256) as i8
        } else {
            value as i8
        })
    }

    /// Detect driving event from MEMS data
    fn detect_driving_event(
        accel_x: f64,
        accel_y: f64,
        accel_z: f64,
        speed_kph: f64,
        rpm: Option<u16>,
    ) -> Option<DrivingEvent> {
        // Thresholds from GISV1
        const HARSH_BRAKE_THRESHOLD: f64 = -0.4;
        const HARSH_ACCEL_THRESHOLD: f64 = 0.4;
        const SHARP_TURN_THRESHOLD: f64 = 0.4;
        const SPEED_BUMP_THRESHOLD: f64 = 0.5;
        const POTHOLE_THRESHOLD: f64 = -0.6;
        const HIGH_RPM_THRESHOLD: u16 = 3200;
        const OVERSPEED_THRESHOLD: f64 = 110.0;

        if accel_x < HARSH_BRAKE_THRESHOLD {
            return Some(DrivingEvent::HarshBraking);
        }
        if accel_x > HARSH_ACCEL_THRESHOLD {
            return Some(DrivingEvent::HarshAcceleration);
        }
        if accel_y.abs() > SHARP_TURN_THRESHOLD {
            return Some(DrivingEvent::SharpTurn);
        }
        if accel_z > SPEED_BUMP_THRESHOLD && speed_kph > 30.0 {
            return Some(DrivingEvent::SpeedBump);
        }
        if accel_z < POTHOLE_THRESHOLD {
            return Some(DrivingEvent::Pothole);
        }
        if let Some(r) = rpm {
            if r > HIGH_RPM_THRESHOLD {
                return Some(DrivingEvent::HighRpm);
            }
        }
        if speed_kph > OVERSPEED_THRESHOLD {
            return Some(DrivingEvent::Overspeeding);
        }

        None
    }

    /// Parse FMS data for protocol V1
    /// V1: Basic fuel at position 34 (2 bytes hex)
    fn parse_fms_v1(payload: &str) -> Result<FmsData> {
        let mut fms = FmsData::default();
        
        // V1: Fuel % at position 34-35
        if payload.len() >= 36 {
            if let Ok(fuel) = u8::from_str_radix(&payload[34..36], 16) {
                if fuel <= 100 {
                    fms.fuel_percent = Some(fuel);
                    fms.has_data = true;
                }
            }
        }
        
        Ok(fms)
    }

    /// Parse FMS data for protocol V2
    /// V2: FMS Fuel at position 54 (2 bytes hex), fallback to V1 position
    fn parse_fms_v2(payload: &str) -> Result<FmsData> {
        let mut fms = FmsData::default();
        
        // V2: Try FMS Fuel % at position 54-55 first
        if payload.len() >= 56 {
            if let Ok(fuel) = u8::from_str_radix(&payload[54..56], 16) {
                if fuel > 0 && fuel <= 100 {
                    fms.fuel_percent = Some(fuel);
                    fms.has_data = true;
                    return Ok(fms);
                }
            }
        }
        
        // Fallback to V1 position (34-35)
        if payload.len() >= 36 {
            if let Ok(fuel) = u8::from_str_radix(&payload[34..36], 16) {
                if fuel <= 100 {
                    fms.fuel_percent = Some(fuel);
                    fms.has_data = true;
                }
            }
        }
        
        Ok(fms)
    }

    /// Parse FMS data for protocol V3
    /// V3: Full FMS data starting at position 70
    /// Layout:
    /// - 70-71: FMS Fuel %
    /// - 72-73: FMS Temp + 40
    /// - 74-81: FMS Odometer (km)
    /// - 82-83: FMS Speed (km/h)
    /// - 84-87: FMS RPM
    /// - 88-91: FMS Fuel Rate (1/512 km/L)
    /// - 92-99: FMS Total Fuel Used x2
    fn parse_fms_v3(payload: &str) -> Result<FmsData> {
        let mut fms = FmsData::default();
        
        // Minimum length check for basic FMS data
        if payload.len() < 72 {
            return Ok(fms);
        }

        // FMS Fuel % (position 70-71)
        if let Ok(fuel) = u8::from_str_radix(&payload[70..72], 16) {
            if fuel <= 100 {
                fms.fuel_percent = Some(fuel);
                fms.has_data = true;
            }
        }

        // FMS Temperature (position 72-73, value - 40 = Celsius)
        if payload.len() >= 74 {
            if let Ok(temp_raw) = u8::from_str_radix(&payload[72..74], 16) {
                fms.temperature_c = Some(temp_raw as i16 - 40);
            }
        }

        // FMS Odometer in km (position 74-81, 4 bytes)
        if payload.len() >= 82 {
            if let Ok(odo) = u32::from_str_radix(&payload[74..82], 16) {
                fms.odometer_km = Some(odo);
            }
        }

        // FMS Speed in km/h (position 82-83)
        if payload.len() >= 84 {
            if let Ok(speed) = u8::from_str_radix(&payload[82..84], 16) {
                fms.speed_kph = Some(speed);
            }
        }

        // FMS RPM (position 84-87, 2 bytes)
        if payload.len() >= 88 {
            if let Ok(rpm) = u16::from_str_radix(&payload[84..88], 16) {
                fms.rpm = Some(rpm);
            }
        }

        // FMS Fuel Rate (position 88-91) - value in 1/512 km/L
        // Formula: raw / 512 = km/L, so L/100km = 100 / (raw/512) = 51200/raw
        if payload.len() >= 92 {
            if let Ok(fuel_rate_raw) = u16::from_str_radix(&payload[88..92], 16) {
                if fuel_rate_raw > 0 {
                    let km_per_liter = fuel_rate_raw as f64 / 512.0;
                    if km_per_liter > 0.0 {
                        fms.fuel_rate_l_per_100km = Some(100.0 / km_per_liter);
                    }
                }
            }
        }

        // FMS Total Fuel Used x2 (position 92-99, 4 bytes)
        if payload.len() >= 100 {
            if let Ok(total_fuel_raw) = u32::from_str_radix(&payload[92..100], 16) {
                fms.total_fuel_used_liters = Some(total_fuel_raw * 2);
            }
        }

        Ok(fms)
    }

    /// Parse FMS data based on protocol version
    fn parse_fms(payload: &str, version: AapProtocolVersion) -> Result<FmsData> {
        match version {
            AapProtocolVersion::V1 => Self::parse_fms_v1(payload),
            AapProtocolVersion::V2 => Self::parse_fms_v2(payload),
            AapProtocolVersion::V3 => Self::parse_fms_v3(payload),
        }
    }

    /// Parse connect frame (AA00/HH00)
    fn parse_connect_frame(payload: &str) -> Result<AapInfoFrame> {
        let parts: Vec<&str> = payload.split(',').map(|s| s.trim()).collect();
        
        let firmware_version = parts.first()
            .map(|s| s.to_string())
            .unwrap_or_default();

        let mut icc_id = None;
        let mut imei = None;

        for part in &parts {
            if let Some(value) = part.strip_prefix("ICC:") {
                let trimmed = value.trim();
                if !trimmed.is_empty() {
                    icc_id = Some(trimmed.to_string());
                }
            } else if let Some(value) = part.strip_prefix("IMEI:") {
                let trimmed = value.trim();
                if !trimmed.is_empty() {
                    imei = Some(trimmed.to_string());
                }
            }
        }

        if imei.is_none() {
            bail!("Connect frame missing IMEI");
        }

        Ok(AapInfoFrame {
            firmware_version,
            icc_id,
            imei,
            device_id: None,
            timestamp: None,
        })
    }

    /// Parse info frame (AA01/HH01)
    fn parse_info_frame_internal(payload: &str) -> Result<AapInfoFrame> {
        let after_header = &payload[4..];
        let parts: Vec<&str> = after_header.split(',').map(|s| s.trim()).collect();

        let firmware_version = parts.first()
            .map(|s| s.to_string())
            .unwrap_or_default();

        let mut icc_id = None;
        let mut imei = None;

        for part in parts.iter().skip(1) {
            if let Some(value) = part.strip_prefix("ICC:") {
                let trimmed = value.trim();
                if !trimmed.is_empty() {
                    icc_id = Some(trimmed.to_string());
                }
            } else if let Some(value) = part.strip_prefix("IMEI:") {
                let trimmed = value.trim();
                if !trimmed.is_empty() {
                    imei = Some(trimmed.to_string());
                }
            }
        }

        Ok(AapInfoFrame {
            firmware_version,
            icc_id,
            imei,
            device_id: None,
            timestamp: None,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_parse_v3_frame() {
        let payload = "HH130094F80228D3D20099CF4F00000A2926FC04FBE780FB00000000010000000016630B17";
        let frame = AapDecoder::parse(payload).expect("Should parse V3 frame");

        assert_eq!(frame.protocol_version, AapProtocolVersion::V3);
        assert!(frame.is_valid);
        assert!(frame.ignition_on);
        assert!((frame.latitude - 36.3835).abs() < 0.001);
        assert!((frame.longitude - 10.1335).abs() < 0.001);
    }

    #[test]
    fn test_parse_info_frame() {
        let payload = "HH011.0.103R10, ICC:8921602050440128136F, IMEI:861001002935274";
        let info = AapDecoder::parse_info_frame(payload).expect("Should parse info frame");

        assert_eq!(info.firmware_version, "1.0.103R10");
        assert_eq!(info.imei.as_deref(), Some("861001002935274"));
        assert_eq!(info.icc_id.as_deref(), Some("8921602050440128136F"));
    }

    #[test]
    fn test_detect_driving_events() {
        // Harsh braking (X < -0.4)
        let event = AapDecoder::detect_driving_event(-0.5, 0.0, 0.0, 50.0, None);
        assert_eq!(event, Some(DrivingEvent::HarshBraking));

        // Sharp turn (|Y| > 0.4)
        let event = AapDecoder::detect_driving_event(0.0, 0.5, 0.0, 50.0, None);
        assert_eq!(event, Some(DrivingEvent::SharpTurn));

        // Speed bump (Z > 0.5 and speed > 30)
        let event = AapDecoder::detect_driving_event(0.0, 0.0, 0.6, 50.0, None);
        assert_eq!(event, Some(DrivingEvent::SpeedBump));

        // Overspeeding (speed > 110, with normal accelerations)
        let event = AapDecoder::detect_driving_event(0.0, 0.0, 0.0, 120.0, None);
        assert_eq!(event, Some(DrivingEvent::Overspeeding));

        // Normal driving (all values within thresholds)
        let event = AapDecoder::detect_driving_event(0.1, 0.1, 0.2, 60.0, None);
        assert_eq!(event, None);
    }

    #[test]
    fn test_decode_timestamp() {
        // 10:35:36 = 38136 seconds
        let hour_raw = "0094F8"; // 38136 in hex
        let date_raw = "1663";   // 2015-05-28 encoded
        
        let dt = AapDecoder::decode_timestamp(hour_raw, date_raw).expect("Should decode timestamp");
        assert_eq!(dt.year(), 2015);
        assert_eq!(dt.month(), 5);
        assert_eq!(dt.day(), 28);
        assert_eq!(dt.hour(), 10);
        assert_eq!(dt.minute(), 35);
        assert_eq!(dt.second(), 36);
    }

    #[test]
    fn test_parse_fms_v1() {
        // V1: Fuel at position 34-35
        // Create a payload with fuel = 50 (0x32) at position 34
        let mut payload = "HH10".to_string();
        payload.push_str("0094F8");           // 4-9: hour
        payload.push_str("21B4E8F0");         // 10-17: latitude
        payload.push_str("05A3D2B0");         // 18-25: longitude
        payload.push_str("0064");             // 26-29: speed
        payload.push_str("5A");               // 30-31: heading
        payload.push_str("0C");               // 32-33: power
        payload.push_str("32");               // 34-35: fuel = 50 (0x32)
        payload.push_str("000000");           // 36-41: MEMS
        payload.push_str("47");               // 42-43: flags
        payload.push_str("0050");             // 44-47: temp
        payload.push_str("00001234");         // 48-55: odometer
        payload.push_str("00");               // 56-57: send flag
        payload.push_str("00000000");         // 58-65: added info
        payload.push_str("1663");             // 66-69: date

        let fms = AapDecoder::parse_fms_v1(&payload).expect("Should parse V1 FMS");
        assert_eq!(fms.fuel_percent, Some(50));
        assert!(fms.has_data);
    }

    #[test]
    fn test_parse_fms_v2() {
        // V2: FMS Fuel at position 54-55
        let mut payload = "HH12".to_string();
        payload.push_str("0094F8");           // 4-9: hour
        payload.push_str("21B4E8F0");         // 10-17: latitude
        payload.push_str("05A3D2B0");         // 18-25: longitude
        payload.push_str("0064");             // 26-29: speed
        payload.push_str("5A");               // 30-31: heading
        payload.push_str("0C");               // 32-33: power
        payload.push_str("00");               // 34-35: fuel (V1 position, empty)
        payload.push_str("000000");           // 36-41: MEMS
        payload.push_str("47");               // 42-43: flags
        payload.push_str("0050");             // 44-47: temp
        payload.push_str("00001234");         // 48-55: odometer
        payload.push_str("4B");               // 54-55: FMS fuel = 75 (0x4B) - this overwrites part of odometer for test
        // Rebuild properly
        let mut payload = "HH12".to_string();
        payload.push_str("0094F8");           // 4-9: hour
        payload.push_str("21B4E8F0");         // 10-17: latitude
        payload.push_str("05A3D2B0");         // 18-25: longitude
        payload.push_str("0064");             // 26-29: speed
        payload.push_str("5A");               // 30-31: heading
        payload.push_str("0C");               // 32-33: power
        payload.push_str("00");               // 34-35: fuel (V1 position)
        payload.push_str("000000");           // 36-41: MEMS
        payload.push_str("47");               // 42-43: flags
        payload.push_str("0050");             // 44-47: temp
        payload.push_str("00001234");         // 48-55: odometer
        payload.push_str("4B");               // 54-55: FMS fuel = 75 (0x4B)
        // Pad to position 56
        while payload.len() < 56 {
            payload.push('0');
        }
        // Set fuel at position 54
        let mut chars: Vec<char> = payload.chars().collect();
        chars[54] = '4';
        chars[55] = 'B';
        let payload: String = chars.into_iter().collect();

        let fms = AapDecoder::parse_fms_v2(&payload).expect("Should parse V2 FMS");
        assert_eq!(fms.fuel_percent, Some(75));
        assert!(fms.has_data);
    }

    #[test]
    fn test_parse_fms_v3_full() {
        // V3: Full FMS data starting at position 70
        // Build a payload with exactly the right positions
        // Positions 0-69: base frame data
        // Positions 70-71: FMS Fuel %
        // Positions 72-73: FMS Temp
        // Positions 74-81: FMS Odometer
        // Positions 82-83: FMS Speed
        // Positions 84-87: FMS RPM
        // Positions 88-91: FMS Fuel Rate
        // Positions 92-99: FMS Total Fuel
        
        // Create base payload (70 chars)
        let base = "HH13";                    // 0-3
        let hour = "0094F8";                  // 4-9
        let lat = "21B4E8F0";                 // 10-17
        let lon = "05A3D2B0";                 // 18-25
        let speed = "0064";                   // 26-29
        let heading = "5A";                   // 30-31
        let power = "0C";                     // 32-33
        let fuel = "32";                      // 34-35
        let mems = "000000";                  // 36-41
        let flags = "47";                     // 42-43
        let temp = "0050";                    // 44-47
        let odo = "00001234";                 // 48-55
        let send_flag = "00";                 // 56-57
        let added_info = "00000000";          // 58-65
        let date = "1663";                    // 66-69
        
        // FMS V3 data (positions 70-99)
        let fms_fuel = "3C";                  // 70-71: 60%
        let fms_temp = "50";                  // 72-73: 80 -> 40°C
        let fms_odo = "0001E240";             // 74-81: 123456 km
        let fms_speed = "50";                 // 82-83: 80 km/h
        let fms_rpm = "09C4";                 // 84-87: 2500 RPM
        let fms_rate = "0400";                // 88-91: 1024 -> 50 L/100km
        let fms_total = "00001388";           // 92-99: 5000 -> 10000 L
        
        let payload = format!(
            "{}{}{}{}{}{}{}{}{}{}{}{}{}{}{}{}{}{}{}{}{}",
            base, hour, lat, lon, speed, heading, power, fuel, mems, flags, temp, odo, send_flag, added_info, date,
            fms_fuel, fms_temp, fms_odo, fms_speed, fms_rpm, fms_rate
        );
        // Add total fuel separately to ensure we have 100 chars
        let payload = format!("{}{}", payload, fms_total);

        // Verify payload length
        assert_eq!(payload.len(), 100, "Payload should be 100 chars for full V3 FMS");

        let fms = AapDecoder::parse_fms_v3(&payload).expect("Should parse V3 FMS");
        assert_eq!(fms.fuel_percent, Some(60), "Fuel percent should be 60");
        assert_eq!(fms.temperature_c, Some(40), "Temperature should be 40°C");
        assert_eq!(fms.odometer_km, Some(123456), "Odometer should be 123456 km");
        assert_eq!(fms.speed_kph, Some(80), "Speed should be 80 km/h");
        assert_eq!(fms.rpm, Some(2500), "RPM should be 2500");
        assert!(fms.fuel_rate_l_per_100km.is_some(), "Fuel rate should be present");
        let fuel_rate = fms.fuel_rate_l_per_100km.unwrap();
        assert!((fuel_rate - 50.0).abs() < 0.1, "Fuel rate should be ~50 L/100km");
        assert_eq!(fms.total_fuel_used_liters, Some(10000), "Total fuel should be 10000 L");
        assert!(fms.has_data, "has_data should be true");
    }

    #[test]
    fn test_decode_real_gps_frame() {
        // Real GPS frame provided for testing
        let payload = "AA13008ACB0228D1A5008B736800A6062B000ED6E8E7800000000000030000000025D00000000000000000000000000000000C1F";
        
        println!("=== Decoding Real GPS Frame ===");
        println!("Payload length: {}", payload.len());
        println!("Raw payload: {}", payload);
        
        // Parse the frame
        let result = AapDecoder::parse(payload);
        
        match result {
            Ok(frame) => {
                println!("\n=== Decoded Successfully ===");
                println!("Frame type: {:?}", frame.frame_type);
                println!("Protocol version: {:?}", frame.protocol_version);
                println!("Timestamp: {}", frame.recorded_at);
                println!("Latitude: {:.6}", frame.latitude);
                println!("Longitude: {:.6}", frame.longitude);
                println!("Speed: {:.1} km/h", frame.speed_kph);
                println!("Heading: {:.0}°", frame.heading_deg);
                println!("Power voltage: {} V", frame.power_voltage);
                println!("Power source rescue: {}", frame.power_source_rescue);
                println!("Fuel: {}%", frame.fuel_percent);
                println!("Ignition: {}", if frame.ignition_on { "ON" } else { "OFF" });
                println!("GPS valid: {}", frame.is_valid);
                println!("Real-time: {}", frame.is_real_time);
                println!("Odometer: {} km", frame.odometer_km);
                println!("Temperature raw: {}", frame.temperature_raw);
                println!("Send flag: {}", frame.send_flag);
                println!("Added info: {}", frame.added_info);
                println!("Signal quality: {:?}", frame.signal_quality);
                println!("Satellites: {:?}", frame.satellites);
                println!("\n=== MEMS Data ===");
                println!("Accel X: {:.3} G (raw: {})", frame.mems.accel_x, frame.mems.raw_x);
                println!("Accel Y: {:.3} G (raw: {})", frame.mems.accel_y, frame.mems.raw_y);
                println!("Accel Z: {:.3} G (raw: {})", frame.mems.accel_z, frame.mems.raw_z);
                println!("Driving event: {:?}", frame.mems.event);
                println!("\n=== FMS Data ===");
                println!("FMS Fuel: {:?}%", frame.fms.fuel_percent);
                println!("FMS Temperature: {:?}°C", frame.fms.temperature_c);
                println!("FMS Odometer: {:?} km", frame.fms.odometer_km);
                println!("FMS Speed: {:?} km/h", frame.fms.speed_kph);
                println!("FMS RPM: {:?}", frame.fms.rpm);
                println!("FMS Fuel Rate: {:?} L/100km", frame.fms.fuel_rate_l_per_100km);
                println!("FMS has data: {}", frame.fms.has_data);
                
                // Basic assertions
                assert!(frame.latitude != 0.0, "Latitude should not be 0");
                assert!(frame.longitude != 0.0, "Longitude should not be 0");
            }
            Err(e) => {
                println!("ERROR: Failed to decode frame: {}", e);
                panic!("Frame decoding failed: {}", e);
            }
        }
    }
}
