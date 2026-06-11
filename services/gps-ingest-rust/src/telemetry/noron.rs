use anyhow::{bail, Context, Result};
use chrono::{NaiveDate, NaiveDateTime, NaiveTime};
use tracing::{debug, info, warn};

use super::model::{FrameKind, FrameVersion, HhFrame};

// ============================================================================
// Noron NR024 Binary Protocol Decoder
// ============================================================================
//
// Protocol: Binary, Little-Endian
// Transport: UDP (same as GISV1)
//
// Packet structure:
//   [nPackLen: u16 LE] [nFlag: u16 LE] [body: variable]
//
// Message types (nFlag):
//   0x0000 - Handshake (login): body = 11-byte device ID (UTF-8)
//   0x0003 - Alarm position upload
//   0x0008 - Standard position upload
//   0x0032 - Extended position (+ temperature + mileage)
//   0x0200 - Position with server IP/port prefix
//   0x0201 - Position with server IP/port prefix
//   0x8009 - Control response with server IP/port prefix
//   0x8201 - Position with server IP/port prefix
//
// Position data layout (after header):
//   bEnable:     1 byte   - GPS valid flag
//   bAlarm:      1 byte   - Alarm type
//   nSpeed:      1 byte   - Speed (km/h)
//   nDirection:  2 bytes (LE i16)  - Heading degrees
//   fLongitude:  4 bytes (LE f32)  - Longitude (IEEE 754)
//   fLatitude:   4 bytes (LE f32)  - Latitude (IEEE 754)
//   lDateTime:   4 bytes (LE i32)  - Bit-packed datetime
//   sUserID:     11 bytes (UTF-8)  - Device ID
//   IoValue:     1 byte   - I/O state (bit 0 = ignition)
//   VolValue:    1 byte   - Voltage
//
// Extended fields (0x0032 only, after VolValue):
//   Temperature: 2 bytes (LE i16)
//   Mileage:     4 bytes (LE f32, cast to i32)
//
// DateTime bit-packing (32-bit integer):
//   year   = (val >> 26) + 2000
//   month  = (val >> 22) & 0x0F
//   day    = (val >> 17) & 0x1F
//   hour   = (val >> 12) & 0x1F
//   minute = (val >> 6) & 0x3F
//   second = val & 0x3F
//
// Handshake ACK (server → device):
//   Fixed 13 bytes: 0D 0A 2A 4B 57 00 13 00 00 80 01 0D 0A
// ============================================================================

/// Minimum packet size: 4 bytes header
const MIN_PACKET_SIZE: usize = 4;

/// Handshake ACK response (13 bytes)
/// Markers: \r\n*KW\0 + length(0x0013) + flag(0x8000) + error(0x01) + \r\n
pub const HANDSHAKE_ACK: [u8; 13] = [
    0x0D, 0x0A, 0x2A, 0x4B, 0x57, 0x00, // \r\n*KW\0
    0x13, 0x00, // packet length (LE)
    0x00, 0x80, // flag 0x8000 (LE)
    0x01,       // error code (success)
    0x0D, 0x0A, // \r\n
];

/// Noron message types
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum NoronFlag {
    Handshake,          // 0x0000 - Login/handshake
    Alarm,              // 0x0003 - Alarm position
    PositionStandard,   // 0x0008 - Standard position
    PositionExtended,   // 0x0032 - Position + temp + mileage
    Position0200,       // 0x0200 - Position with IP prefix
    Position0201,       // 0x0201 - Position with IP prefix
    ControlResponse,    // 0x8009 - Control response with IP prefix
    Position8201,       // 0x8201 - Position with IP prefix
    Unknown(u16),       // Unknown flag
}

impl NoronFlag {
    fn from_u16(val: u16) -> Self {
        match val {
            0x0000 => NoronFlag::Handshake,
            0x0003 => NoronFlag::Alarm,
            0x0008 => NoronFlag::PositionStandard,
            0x0032 => NoronFlag::PositionExtended,
            0x0200 => NoronFlag::Position0200,
            0x0201 => NoronFlag::Position0201,
            0x8009 => NoronFlag::ControlResponse,
            0x8201 => NoronFlag::Position8201,
            other  => NoronFlag::Unknown(other),
        }
    }

    /// Whether this flag type has IP+port prefix (6 bytes) before position data
    fn has_ip_prefix(&self) -> bool {
        matches!(
            self,
            NoronFlag::ControlResponse
                | NoronFlag::Position0200
                | NoronFlag::Position0201
                | NoronFlag::Position8201
        )
    }

    /// Whether this flag type has extended data (temperature + mileage)
    fn has_extended_data(&self) -> bool {
        matches!(self, NoronFlag::PositionExtended)
    }

    /// Whether this is a position frame (vs handshake or unknown)
    pub fn is_position(&self) -> bool {
        matches!(
            self,
            NoronFlag::Alarm
                | NoronFlag::PositionStandard
                | NoronFlag::PositionExtended
                | NoronFlag::Position0200
                | NoronFlag::Position0201
                | NoronFlag::ControlResponse
                | NoronFlag::Position8201
        )
    }
}

impl std::fmt::Display for NoronFlag {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            NoronFlag::Handshake => write!(f, "HANDSHAKE(0x0000)"),
            NoronFlag::Alarm => write!(f, "ALARM(0x0003)"),
            NoronFlag::PositionStandard => write!(f, "POSITION(0x0008)"),
            NoronFlag::PositionExtended => write!(f, "POSITION_EXT(0x0032)"),
            NoronFlag::Position0200 => write!(f, "POSITION(0x0200)"),
            NoronFlag::Position0201 => write!(f, "POSITION(0x0201)"),
            NoronFlag::ControlResponse => write!(f, "CTRL_RESP(0x8009)"),
            NoronFlag::Position8201 => write!(f, "POSITION(0x8201)"),
            NoronFlag::Unknown(v) => write!(f, "UNKNOWN(0x{:04X})", v),
        }
    }
}

/// Decoded Noron packet header
#[derive(Debug)]
pub struct NoronPacket {
    pub pack_len: u16,
    pub flag: NoronFlag,
    pub device_id: Option<String>,  // Only for handshake
    pub position: Option<NoronPosition>,
}

/// Decoded Noron position data
#[derive(Debug, Clone)]
pub struct NoronPosition {
    pub gps_valid: bool,    // bEnable
    pub alarm: u8,          // bAlarm
    pub speed: u8,          // nSpeed (km/h)
    pub heading: i16,       // nDirection
    pub longitude: f32,     // fLongitude (IEEE 754 float)
    pub latitude: f32,      // fLatitude (IEEE 754 float)
    pub recorded_at: NaiveDateTime,
    pub device_id: String,  // sUserID (11 bytes, null-trimmed)
    pub io_value: u8,       // IoValue
    pub vol_value: u8,      // VolValue
    pub temperature: Option<i16>,   // Extended only
    pub mileage: Option<i32>,       // Extended only (meters)
}

/// Result of decoding a Noron packet from raw bytes
#[derive(Debug)]
pub enum NoronDecodeResult {
    /// Handshake packet - device_id extracted, must send ACK
    Handshake { device_id: String },
    /// Position data - converted to HhFrame for pipeline
    Position { frame: HhFrame, device_id: String },
    /// Unknown or unhandled flag - log and skip
    Unknown { flag: u16 },
}

/// Try to decode one or more Noron packets from a raw byte buffer.
/// Returns a list of decode results (a single TCP read may contain multiple packets).
pub fn decode_buffer(buffer: &[u8]) -> Vec<Result<NoronDecodeResult>> {
    let mut results = Vec::new();
    let mut offset = 0;

    while offset + MIN_PACKET_SIZE <= buffer.len() {
        match decode_single_packet(&buffer[offset..]) {
            Ok((result, consumed)) => {
                results.push(Ok(result));
                offset += consumed;
            }
            Err(e) => {
                // If we can't decode, try to skip using pack_len
                if offset + 2 <= buffer.len() {
                    let pack_len = u16::from_le_bytes([buffer[offset], buffer[offset + 1]]) as usize;
                    if pack_len > 0 && offset + pack_len <= buffer.len() {
                        warn!(
                            offset,
                            pack_len,
                            error = %e,
                            "Skipping malformed Noron packet"
                        );
                        offset += pack_len;
                    } else {
                        results.push(Err(e));
                        break; // Can't recover
                    }
                } else {
                    results.push(Err(e));
                    break;
                }
            }
        }
    }

    results
}

/// Decode a single Noron packet starting at the given buffer position.
/// Returns (result, bytes_consumed).
fn decode_single_packet(buffer: &[u8]) -> Result<(NoronDecodeResult, usize)> {
    if buffer.len() < MIN_PACKET_SIZE {
        bail!("Noron packet too short: {} bytes (minimum {})", buffer.len(), MIN_PACKET_SIZE);
    }

    // Read header
    let pack_len = u16::from_le_bytes([buffer[0], buffer[1]]);
    let flag_raw = u16::from_le_bytes([buffer[2], buffer[3]]);
    let flag = NoronFlag::from_u16(flag_raw);

    debug!(pack_len, flag = %flag, "Decoding Noron packet");

    match flag {
        NoronFlag::Handshake => {
            // Handshake: body = 11 bytes device ID
            if buffer.len() < 15 {
                bail!("Noron handshake packet too short: {} bytes (need 15)", buffer.len());
            }
            let device_id_raw = &buffer[4..15];
            let device_id = String::from_utf8_lossy(device_id_raw)
                .replace('\0', "")
                .trim()
                .to_string();

            info!(device_id = %device_id, "Noron handshake received");
            Ok((NoronDecodeResult::Handshake { device_id }, 15))
        }

        NoronFlag::Alarm
        | NoronFlag::PositionStandard
        | NoronFlag::PositionExtended
        | NoronFlag::Position0200
        | NoronFlag::Position0201
        | NoronFlag::ControlResponse
        | NoronFlag::Position8201 => {
            let position = parse_position(buffer, flag)?;
            let device_id = position.device_id.clone();
            let consumed = calculate_consumed_bytes(buffer, flag);
            let frame = noron_position_to_hh_frame(&position, flag);
            Ok((NoronDecodeResult::Position { frame, device_id }, consumed))
        }

        NoronFlag::Unknown(v) => {
            warn!(flag = v, "Unknown Noron flag");
            // Try to skip by pack_len
            let skip = if pack_len > 0 { pack_len as usize } else { buffer.len() };
            Ok((NoronDecodeResult::Unknown { flag: v }, skip.min(buffer.len())))
        }
    }
}

/// Calculate how many bytes were consumed for a position packet
fn calculate_consumed_bytes(buffer: &[u8], flag: NoronFlag) -> usize {
    let base = 4; // header
    let ip_prefix = if flag.has_ip_prefix() { 6 } else { 0 };
    let position_size = 1 + 1 + 1 + 2 + 4 + 4 + 4 + 11 + 1 + 1; // 30 bytes
    let extended = if flag.has_extended_data() { 2 + 4 } else { 0 }; // temp + mileage
    let error_code = if matches!(flag, NoronFlag::ControlResponse) { 1 } else { 0 };
    let total = base + ip_prefix + position_size + extended + error_code;
    total.min(buffer.len())
}

/// Parse position data from a Noron packet
fn parse_position(buffer: &[u8], flag: NoronFlag) -> Result<NoronPosition> {
    let mut cursor = 4usize; // Skip header (pack_len + flag)

    // Some flags have IP+port prefix (6 bytes) before position data
    if flag.has_ip_prefix() {
        if buffer.len() < cursor + 6 {
            bail!("Noron packet too short for IP prefix at offset {}", cursor);
        }
        // Skip nGisIp (4 bytes) + nPort (2 bytes)
        cursor += 6;
    }

    // Minimum position data: 30 bytes
    let min_remaining = 30;
    if buffer.len() < cursor + min_remaining {
        bail!(
            "Noron position data too short: buffer {} bytes, cursor at {}, need {} more",
            buffer.len(),
            cursor,
            min_remaining
        );
    }

    // bEnable (1 byte)
    let gps_valid = buffer[cursor] != 0;
    cursor += 1;

    // bAlarm (1 byte)
    let alarm = buffer[cursor];
    cursor += 1;

    // nSpeed (1 byte)
    let speed = buffer[cursor];
    cursor += 1;

    // nDirection (2 bytes LE i16)
    let heading = i16::from_le_bytes([buffer[cursor], buffer[cursor + 1]]);
    cursor += 2;

    // fLongitude (4 bytes LE f32)
    let longitude = f32::from_le_bytes([
        buffer[cursor],
        buffer[cursor + 1],
        buffer[cursor + 2],
        buffer[cursor + 3],
    ]);
    cursor += 4;

    // fLatitude (4 bytes LE f32)
    let latitude = f32::from_le_bytes([
        buffer[cursor],
        buffer[cursor + 1],
        buffer[cursor + 2],
        buffer[cursor + 3],
    ]);
    cursor += 4;

    // lDateTime (4 bytes LE i32) - bit-packed
    let datetime_raw = i32::from_le_bytes([
        buffer[cursor],
        buffer[cursor + 1],
        buffer[cursor + 2],
        buffer[cursor + 3],
    ]);
    let recorded_at = decode_noron_datetime(datetime_raw)?;
    cursor += 4;

    // sUserID (11 bytes UTF-8)
    let device_id_raw = &buffer[cursor..cursor + 11];
    let device_id = String::from_utf8_lossy(device_id_raw)
        .replace('\0', "")
        .trim()
        .to_string();
    cursor += 11;

    // IoValue (1 byte)
    let io_value = buffer[cursor];
    cursor += 1;

    // VolValue (1 byte)
    let vol_value = buffer[cursor];
    cursor += 1;

    // Extended data (only for 0x0032)
    let (temperature, mileage) = if flag.has_extended_data() && buffer.len() >= cursor + 6 {
        let temp = i16::from_le_bytes([buffer[cursor], buffer[cursor + 1]]);
        cursor += 2;
        let mileage_f = f32::from_le_bytes([
            buffer[cursor],
            buffer[cursor + 1],
            buffer[cursor + 2],
            buffer[cursor + 3],
        ]);
        (
            Some(temp),
            Some(mileage_f as i32),
        )
    } else {
        (None, None)
    };

    info!(
        device_id = %device_id,
        flag = %flag,
        gps_valid,
        alarm,
        speed,
        heading,
        lon = format_args!("{:.6}", longitude),
        lat = format_args!("{:.6}", latitude),
        datetime = %recorded_at,
        io_value = format_args!("0x{:02X}", io_value),
        vol_value,
        temp = ?temperature,
        mileage = ?mileage,
        raw_speed_byte = format_args!("0x{:02X}", buffer[if flag.has_ip_prefix() { 10 } else { 6 }]),
        raw_enable_byte = format_args!("0x{:02X}", buffer[if flag.has_ip_prefix() { 8 } else { 4 }]),
        "NORON DECODED POSITION"
    );

    Ok(NoronPosition {
        gps_valid,
        alarm,
        speed,
        heading,
        longitude,
        latitude,
        recorded_at,
        device_id,
        io_value,
        vol_value,
        temperature,
        mileage,
    })
}

/// Decode Noron bit-packed datetime from a 32-bit integer
///
/// Layout:
///   bits 26-31: year - 2000
///   bits 22-25: month (1-12)
///   bits 17-21: day (1-31)
///   bits 12-16: hour (0-23)
///   bits 6-11:  minute (0-59)
///   bits 0-5:   second (0-59)
fn decode_noron_datetime(raw: i32) -> Result<NaiveDateTime> {
    let year = ((raw >> 26) & 0x3F) as i32 + 2000;
    let month = ((raw >> 22) & 0x0F) as u32;
    let day = ((raw >> 17) & 0x1F) as u32;
    let hour = ((raw >> 12) & 0x1F) as u32;
    let minute = ((raw >> 6) & 0x3F) as u32;
    let second = (raw & 0x3F) as u32;

    // Validate ranges
    if month < 1 || month > 12 || day < 1 || day > 31 || hour > 23 || minute > 59 || second > 59 {
        // Fallback to current time (same as GISV1 catch block)
        warn!(
            raw,
            year,
            month,
            day,
            hour,
            minute,
            second,
            "Invalid Noron datetime, using current time"
        );
        return Ok(chrono::Utc::now().naive_utc());
    }

    let date = NaiveDate::from_ymd_opt(year, month, day)
        .context("Invalid Noron date")?;
    let time = NaiveTime::from_hms_opt(hour, minute, second)
        .context("Invalid Noron time")?;

    Ok(NaiveDateTime::new(date, time))
}

/// Convert a NoronPosition into an HhFrame for the shared ingestion pipeline
fn noron_position_to_hh_frame(pos: &NoronPosition, flag: NoronFlag) -> HhFrame {
    // Map Noron alarm to send_flag equivalent:
    // alarm 0 = normal (periodic), alarm > 0 = alert
    let send_flag = if pos.alarm > 0 { 11 } else { 1 }; // 11 = ALERT, 1 = SENDP

    // Speed: always use raw speed from tracker (matching GISV1 SaveUdpData behavior)
    // GISV1 never forced speed to 0 — it stored nSpeed as-is
    let speed_kph = pos.speed as f64;

    // Ignition: derive from speed + IoValue
    // If vehicle is moving (speed > 0), ignition MUST be on regardless of IoValue
    // IoValue bit 0 may not reliably represent ignition on all Noron models
    let ignition_on = speed_kph > 0.0 || (pos.io_value & 0x01) != 0;

    // Heading: clamp to 0-360
    let heading_deg = (pos.heading as f64).clamp(0.0, 360.0);

    // Odometer from mileage (if extended packet)
    let odometer_km = pos.mileage.map(|m| m as u32).unwrap_or(0);

    // Temperature
    let temperature_raw = pos.temperature.map(|t| t as u16).unwrap_or(0);

    // Use V3 for extended packets (0x0032) so has_fms=true and odometer/temperature
    // get stored in DB — matching GISV1 SaveDynData behavior
    let version = if flag.has_extended_data() { FrameVersion::V3 } else { FrameVersion::V1 };

    // Build raw_payload hex string for logging
    let raw_payload = format!(
        "NORON:{} lat={:.6} lon={:.6} spd={} hdg={} alarm={} io={} vol={}",
        flag, pos.latitude, pos.longitude, pos.speed, pos.heading,
        pos.alarm, pos.io_value, pos.vol_value
    );

    info!(
        device_id = %pos.device_id,
        speed_kph,
        ignition_on,
        heading_deg,
        is_valid = pos.gps_valid,
        "NORON FRAME → pipeline"
    );

    HhFrame {
        kind: FrameKind::RealTime,
        version,
        recorded_at: pos.recorded_at,
        latitude: pos.latitude as f64,
        longitude: pos.longitude as f64,
        speed_kph,
        heading_deg,
        power_voltage: pos.vol_value,
        power_source_rescue: false,
        fuel_raw: 0, // Noron doesn't report fuel
        ignition_on,
        mems_x: 0,
        mems_y: 0,
        mems_z: 0,
        temperature_raw,
        odometer_km,
        send_flag,
        added_info: pos.alarm as u32,
        signal_quality: None,
        satellites_in_view: None,
        rpm: None,
        fuel_rate_l_per_100km: None,
        fms_temperature_c: pos.temperature,
        is_valid: pos.gps_valid,
        // Honest real-time flag (see teltonika.rs): buffered backlog tagged false.
        is_real_time: (chrono::Utc::now().naive_utc() - pos.recorded_at).num_seconds() < 300,
        flags_raw: pos.io_value,
        raw_payload,
        remaining_payload: None,
        address: None,
    }
}

// ============================================================================
// Tests
// ============================================================================

#[cfg(test)]
mod tests {
    use super::*;

    /// Build a minimal handshake packet (nFlag = 0x0000)
    fn build_handshake_packet(device_id: &str) -> Vec<u8> {
        let mut buf = Vec::new();
        // nPackLen (LE u16) = 15
        buf.extend_from_slice(&15u16.to_le_bytes());
        // nFlag (LE u16) = 0x0000
        buf.extend_from_slice(&0u16.to_le_bytes());
        // device ID (11 bytes, zero-padded)
        let mut id_bytes = [0u8; 11];
        let src = device_id.as_bytes();
        let copy_len = src.len().min(11);
        id_bytes[..copy_len].copy_from_slice(&src[..copy_len]);
        buf.extend_from_slice(&id_bytes);
        buf
    }

    /// Build a standard position packet (nFlag = 0x0008)
    fn build_position_packet(device_id: &str, lat: f32, lon: f32, speed: u8) -> Vec<u8> {
        let mut buf = Vec::new();
        // nPackLen
        buf.extend_from_slice(&34u16.to_le_bytes());
        // nFlag = 0x0008
        buf.extend_from_slice(&0x0008u16.to_le_bytes());
        // bEnable = 1 (GPS valid)
        buf.push(1);
        // bAlarm = 0
        buf.push(0);
        // nSpeed
        buf.push(speed);
        // nDirection (LE i16) = 180
        buf.extend_from_slice(&180i16.to_le_bytes());
        // fLongitude (LE f32)
        buf.extend_from_slice(&lon.to_le_bytes());
        // fLatitude (LE f32)
        buf.extend_from_slice(&lat.to_le_bytes());
        // lDateTime - encode 2025-03-10 14:30:00
        // year=25, month=3, day=10, hour=14, min=30, sec=0
        let dt: i32 = (25 << 26) | (3 << 22) | (10 << 17) | (14 << 12) | (30 << 6) | 0;
        buf.extend_from_slice(&dt.to_le_bytes());
        // sUserID (11 bytes)
        let mut id_bytes = [0u8; 11];
        let src = device_id.as_bytes();
        let copy_len = src.len().min(11);
        id_bytes[..copy_len].copy_from_slice(&src[..copy_len]);
        buf.extend_from_slice(&id_bytes);
        // IoValue = 1 (ignition on)
        buf.push(1);
        // VolValue = 12
        buf.push(12);
        buf
    }

    /// Build an extended position packet (nFlag = 0x0032) with temperature + mileage
    fn build_extended_packet(device_id: &str, lat: f32, lon: f32, speed: u8, temp: i16, mileage: f32) -> Vec<u8> {
        let mut buf = Vec::new();
        // nPackLen
        buf.extend_from_slice(&40u16.to_le_bytes());
        // nFlag = 0x0032
        buf.extend_from_slice(&0x0032u16.to_le_bytes());
        // bEnable = 1
        buf.push(1);
        // bAlarm = 0
        buf.push(0);
        // nSpeed
        buf.push(speed);
        // nDirection = 90
        buf.extend_from_slice(&90i16.to_le_bytes());
        // fLongitude
        buf.extend_from_slice(&lon.to_le_bytes());
        // fLatitude
        buf.extend_from_slice(&lat.to_le_bytes());
        // lDateTime - 2025-03-10 14:30:00
        let dt: i32 = (25 << 26) | (3 << 22) | (10 << 17) | (14 << 12) | (30 << 6) | 0;
        buf.extend_from_slice(&dt.to_le_bytes());
        // sUserID
        let mut id_bytes = [0u8; 11];
        let src = device_id.as_bytes();
        let copy_len = src.len().min(11);
        id_bytes[..copy_len].copy_from_slice(&src[..copy_len]);
        buf.extend_from_slice(&id_bytes);
        // IoValue = 1
        buf.push(1);
        // VolValue = 12
        buf.push(12);
        // Temperature (LE i16)
        buf.extend_from_slice(&temp.to_le_bytes());
        // Mileage (LE f32)
        buf.extend_from_slice(&mileage.to_le_bytes());
        buf
    }

    #[test]
    fn test_decode_handshake() {
        let packet = build_handshake_packet("NR024TEST1");
        let results = decode_buffer(&packet);
        assert_eq!(results.len(), 1);
        match results[0].as_ref().unwrap() {
            NoronDecodeResult::Handshake { device_id } => {
                assert_eq!(device_id, "NR024TEST1");
            }
            other => panic!("Expected Handshake, got {:?}", other),
        }
    }

    #[test]
    fn test_decode_position_standard() {
        let packet = build_position_packet("NR024TEST1", 36.8065, 10.1815, 60);
        let results = decode_buffer(&packet);
        assert_eq!(results.len(), 1);
        match results[0].as_ref().unwrap() {
            NoronDecodeResult::Position { frame, device_id } => {
                assert_eq!(device_id, "NR024TEST1");
                assert!((frame.latitude - 36.8065).abs() < 0.001);
                assert!((frame.longitude - 10.1815).abs() < 0.001);
                assert_eq!(frame.speed_kph, 60.0);
                assert!(frame.ignition_on);
                assert!(frame.is_valid);
                assert_eq!(frame.heading_deg, 180.0);
                // Check datetime: 2025-03-10 14:30:00
                assert_eq!(frame.recorded_at.year(), 2025);
                assert_eq!(frame.recorded_at.month(), 3);
                assert_eq!(frame.recorded_at.day(), 10);
                assert_eq!(frame.recorded_at.hour(), 14);
                assert_eq!(frame.recorded_at.minute(), 30);
            }
            other => panic!("Expected Position, got {:?}", other),
        }
    }

    #[test]
    fn test_decode_position_extended() {
        let packet = build_extended_packet("NR024TEST1", 36.8065, 10.1815, 80, 25, 15432.0);
        let results = decode_buffer(&packet);
        assert_eq!(results.len(), 1);
        match results[0].as_ref().unwrap() {
            NoronDecodeResult::Position { frame, device_id } => {
                assert_eq!(device_id, "NR024TEST1");
                assert_eq!(frame.speed_kph, 80.0);
                assert_eq!(frame.fms_temperature_c, Some(25));
                assert_eq!(frame.odometer_km, 15432);
            }
            other => panic!("Expected Position, got {:?}", other),
        }
    }

    #[test]
    fn test_decode_datetime() {
        // 2025-03-10 14:30:45
        let raw: i32 = (25 << 26) | (3 << 22) | (10 << 17) | (14 << 12) | (30 << 6) | 45;
        let dt = decode_noron_datetime(raw).unwrap();
        assert_eq!(dt.year(), 2025);
        assert_eq!(dt.month(), 3);
        assert_eq!(dt.day(), 10);
        assert_eq!(dt.hour(), 14);
        assert_eq!(dt.minute(), 30);
        assert_eq!(dt.second(), 45);
    }

    #[test]
    fn test_decode_datetime_y2k_base() {
        // 2000-01-01 00:00:00 (all zeros except month=1, day=1)
        let raw: i32 = (0 << 26) | (1 << 22) | (1 << 17) | (0 << 12) | (0 << 6) | 0;
        let dt = decode_noron_datetime(raw).unwrap();
        assert_eq!(dt.year(), 2000);
        assert_eq!(dt.month(), 1);
        assert_eq!(dt.day(), 1);
    }

    #[test]
    fn test_multiple_packets_in_buffer() {
        let mut buffer = Vec::new();
        buffer.extend_from_slice(&build_handshake_packet("DEV001"));
        buffer.extend_from_slice(&build_position_packet("DEV001", 36.8, 10.2, 50));
        
        let results = decode_buffer(&buffer);
        assert_eq!(results.len(), 2);
        assert!(matches!(results[0].as_ref().unwrap(), NoronDecodeResult::Handshake { .. }));
        assert!(matches!(results[1].as_ref().unwrap(), NoronDecodeResult::Position { .. }));
    }

    #[test]
    fn test_speed_preserved_and_ignition_derived_from_speed() {
        let mut packet = build_position_packet("DEV001", 36.8, 10.2, 60);
        // Set IoValue to 0 (IoValue bit 0 = 0) - it's the second-to-last byte
        let len = packet.len();
        packet[len - 2] = 0; // IoValue = 0
        
        let results = decode_buffer(&packet);
        match results[0].as_ref().unwrap() {
            NoronDecodeResult::Position { frame, .. } => {
                // Speed preserved as-is (matching GISV1 behavior)
                assert_eq!(frame.speed_kph, 60.0);
                // Ignition derived from speed: speed > 0 → ignition on
                assert!(frame.ignition_on);
            }
            other => panic!("Expected Position, got {:?}", other),
        }
    }

    #[test]
    fn test_ignition_off_when_stationary() {
        let mut packet = build_position_packet("DEV001", 36.8, 10.2, 0);
        let len = packet.len();
        packet[len - 2] = 0; // IoValue = 0, speed = 0
        
        let results = decode_buffer(&packet);
        match results[0].as_ref().unwrap() {
            NoronDecodeResult::Position { frame, .. } => {
                assert_eq!(frame.speed_kph, 0.0);
                assert!(!frame.ignition_on); // speed=0 AND IoValue bit0=0 → off
            }
            other => panic!("Expected Position, got {:?}", other),
        }
    }

    #[test]
    fn test_handshake_ack_bytes() {
        // Verify ACK matches GISV1 NR024TrackerShakeHandResp.ToBuffer()
        assert_eq!(HANDSHAKE_ACK.len(), 13);
        assert_eq!(HANDSHAKE_ACK[0], 0x0D); // \r
        assert_eq!(HANDSHAKE_ACK[1], 0x0A); // \n
        assert_eq!(HANDSHAKE_ACK[2], 0x2A); // *
        assert_eq!(HANDSHAKE_ACK[3], 0x4B); // K
        assert_eq!(HANDSHAKE_ACK[4], 0x57); // W
        assert_eq!(HANDSHAKE_ACK[5], 0x00); // \0
        assert_eq!(HANDSHAKE_ACK[11], 0x0D); // \r
        assert_eq!(HANDSHAKE_ACK[12], 0x0A); // \n
    }

    use chrono::Timelike;
    use chrono::Datelike;
}
