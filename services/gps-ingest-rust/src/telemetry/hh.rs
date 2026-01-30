use anyhow::{bail, Context, Result};
use chrono::{NaiveDate, NaiveDateTime, NaiveTime};
use tracing::debug;

use super::model::{FrameKind, FrameVersion, HhFrame, HhInfoFrame};

/// Supported protocol headers (HH is placeholder, AA is real after NDA)
const VALID_HEADERS: [&str; 2] = ["HH", "AA"];

pub fn parse_frame(payload: &str) -> Result<HhFrame> {
    let payload = payload.trim();
    if !VALID_HEADERS.iter().any(|h| payload.starts_with(h)) {
        anyhow::bail!("invalid header: must start with HH or AA");
    }
    let header = &payload[2..4];
    let (kind, version) = decode_header(header)?;

    match version {
        FrameVersion::V1 => parse_v1(payload, kind, version),
        FrameVersion::V3 => parse_v3(payload, kind, version),
        FrameVersion::Unknown(v) => anyhow::bail!("unsupported HH frame version: {}", v),
    }
}

/// Check if frame is a system info frame (HH02/AA02=software reset, HH03/AA03=GSM reset, HH07/AA07=time request)
pub fn is_system_frame(payload: &str) -> bool {
    let payload = payload.trim();
    payload.starts_with("HH02") || payload.starts_with("AA02") ||
    payload.starts_with("HH03") || payload.starts_with("AA03") ||
    payload.starts_with("HH07") || payload.starts_with("AA07")
}

/// Parses HH00/AA00 connect frames or HH01/AA01 info frames (firmware/ICC/IMEI).
/// Also extracts MAT prefix if present (e.g., "NR08G0663 AA00...")
pub fn parse_info_frame(payload: &str) -> Result<HhInfoFrame> {
    let payload = payload.trim();
    
    // Handle HH00/AA00 connect frames: may have prefix like "NR08G0663 AA00..."
    // Example: NR08G0663 AA00123634281125@25/11/28,12:36:31+00 ,ICC:..., IMEI:...
    if let Some(aa_pos) = payload.find("AA00").or_else(|| payload.find("HH00")) {
        // Extract MAT prefix if present (everything before AA00/HH00)
        let mat = if aa_pos > 0 {
            let prefix = payload[..aa_pos].trim();
            if !prefix.is_empty() {
                Some(prefix.to_string())
            } else {
                None
            }
        } else {
            None
        };
        return parse_connect_frame_with_mat(&payload[aa_pos..], mat);
    }
    
    if !payload.starts_with("HH01") && !payload.starts_with("AA01") {
        bail!("payload is not an HH00/AA00/HH01/AA01 info frame");
    }

    // Example: HH011.0.103R10, ICC:892160..., IMEI:8610...
    let after_header = &payload[4..];
    let parts: Vec<&str> = after_header.split(',').map(|s| s.trim()).collect();

    let firmware_version = parts.get(0).unwrap_or(&"").to_string();
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

    Ok(HhInfoFrame {
        firmware_version,
        icc_id,
        imei,
        mat: None, // HH01/AA01 frames don't have MAT prefix
    })
}

/// Parse HH00/AA00 connect frames with optional MAT prefix
/// Example: AA00123634281125@25/11/28,12:36:31+00 ,ICC:89216020803464581196F, IMEI:860141071569116
fn parse_connect_frame_with_mat(payload: &str, mat: Option<String>) -> Result<HhInfoFrame> {
    // Split by comma to extract ICC and IMEI
    let parts: Vec<&str> = payload.split(',').map(|s| s.trim()).collect();
    
    // First part contains AA00 + time info, extract firmware version from it if available
    let firmware_version = parts.get(0)
        .map(|s| s.trim())
        .unwrap_or("")
        .to_string();
    
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
        bail!("connect frame missing IMEI");
    }

    Ok(HhInfoFrame {
        firmware_version,
        icc_id,
        imei,
        mat, // MAT prefix extracted from frame (e.g., "NR08G0663")
    })
}

fn decode_header(header: &str) -> Result<(FrameKind, FrameVersion)> {
    let bytes = u8::from_str_radix(header, 16)?;
    let x = bytes >> 4;
    let y = bytes & 0x0F;
    let kind = match x {
        1 => FrameKind::RealTimeAndHistory,
        2 => FrameKind::History,
        3 => FrameKind::RealTime,
        other => anyhow::bail!("unknown frame kind: {}", other),
    };
    let version = match y {
        1 => FrameVersion::V1,
        3 => FrameVersion::V3,
        other => FrameVersion::Unknown(other),
    };
    Ok((kind, version))
}

fn parse_v1(payload: &str, kind: FrameKind, version: FrameVersion) -> Result<HhFrame> {
    // V1 Protocol - SAME positions as V3 according to GISV1 implementation
    // GISV1 parses ALL frames (V1, V2, V3) with the same positions!
    // The only difference is V3 has optional FMS data at position 70+
    // 
    // Position 4-9: Hour (6 chars)
    // Position 10-17: Latitude (8 chars)
    // Position 18-25: Longitude (8 chars)
    // Position 26-29: Speed (4 chars)
    // Position 30-31: Heading / 8 (2 chars)
    // Position 32-33: Power (2 chars)
    // Position 34-35: Fuel (2 chars)
    // Position 36-41: MEMS (6 chars)
    // Position 42-43: Flags (2 chars)
    // Position 44-47: Temp (4 chars)
    // Position 48-55: Odometer (8 chars)
    // Position 56-57: Send flag (2 chars)
    // Position 58-65: Added info (8 chars)
    // Position 66-69: Date (4 chars)
    
    let hour_raw = payload.get(4..10).context("V1: missing hour")?;
    let lat_raw = payload.get(10..18).context("V1: missing latitude")?;
    let lon_raw = payload.get(18..26).context("V1: missing longitude")?;
    let speed_raw = payload.get(26..30).context("V1: missing speed")?;
    let heading_raw = payload.get(30..32).context("V1: missing heading")?;
    let power_raw = payload.get(32..34).context("V1: missing power")?;
    let fuel_raw = payload.get(34..36).context("V1: missing fuel")?;
    let mems_raw = payload.get(36..42).context("V1: missing mems")?;
    let flags_raw = payload.get(42..44).context("V1: missing flags")?;
    let temp_raw = payload.get(44..48).context("V1: missing temp")?;
    let odo_raw = payload.get(48..56).context("V1: missing odometer")?;
    let send_flag_raw = payload.get(56..58).context("V1: missing send_flag")?;
    let added_info_raw = payload.get(58..66).context("V1: missing added_info")?;
    let date_raw = payload.get(66..70).context("V1: missing date")?;

    let (recorded_at, is_real_time) = decode_timestamp(hour_raw, date_raw)?;
    let latitude = decode_coordinate(lat_raw, flags_raw, 0x01, true)?;
    let longitude = decode_coordinate(lon_raw, flags_raw, 0x02, false)?;
    let speed_kph = parse_speed(speed_raw)?;
    let heading_deg = u8::from_str_radix(heading_raw, 16).unwrap_or(0) as f64 * 8.0;
    let (power_voltage, power_source_rescue) = decode_power(power_raw);
    let ignition_on = decode_bit(flags_raw, 0x04);
    let mems = decode_mems(mems_raw)?;
    let temperature_raw = u16::from_str_radix(temp_raw, 16)?;
    let odometer_km = u32::from_str_radix(odo_raw, 16)?;
    let send_flag = u8::from_str_radix(send_flag_raw, 16)?;
    let added_info = u32::from_str_radix(added_info_raw, 16)?;

    Ok(HhFrame {
        kind,
        version,
        recorded_at,
        latitude,
        longitude,
        speed_kph,
        heading_deg,
        power_voltage,
        power_source_rescue,
        fuel_raw: u8::from_str_radix(fuel_raw, 16)?,
        ignition_on,
        mems_x: mems.0,
        mems_y: mems.1,
        mems_z: mems.2,
        temperature_raw,
        odometer_km,
        send_flag,
        added_info,
        signal_quality: None,
        satellites_in_view: None,
        rpm: None,
        is_valid: decode_bit(flags_raw, 0x40),
        is_real_time,
        flags_raw: u8::from_str_radix(flags_raw, 16)?,
        raw_payload: payload.to_string(),
        remaining_payload: None,
        address: None,
    })
}

fn parse_v3(payload: &str, kind: FrameKind, version: FrameVersion) -> Result<HhFrame> {
    // V3 Protocol - According to ACI spec (WITH HEADING and optional FMS DATA)
    // Base frame (position 4-69):
    // Position 4-9: Hour (6 chars)
    // Position 10-17: Latitude (8 chars)
    // Position 18-25: Longitude (8 chars)
    // Position 26-29: Speed x10 in mph (4 chars)
    // Position 30-31: Heading / 8 (2 chars)
    // Position 32-33: Power (2 chars)
    // Position 34-35: Fuel Analogic2 (2 chars)
    // Position 36-41: MEMS (6 chars)
    // Position 42-43: Flags (2 chars)
    // Position 44-47: Temp (4 chars)
    // Position 48-55: Odometer (8 chars)
    // Position 56-57: Send flag (2 chars)
    // Position 58-65: Added info (8 chars)
    // Position 66-69: Date (4 chars)
    //
    // Optional FMS DATA (position 70-99):
    // Position 70-71: FMS FUEL % (2 chars)
    // Position 72-73: FMS TEMP +40 (2 chars)
    // Position 74-81: FMS ODOMETER in KM (8 chars)
    // Position 82-83: FMS SPEED (2 chars)
    // Position 84-87: FMS RPM (4 chars)
    // Position 88-91: FMS FUEL RATE (4 chars)
    // Position 92-99: FMS TOTAL FUEL USED (8 chars)
    //
    // After FMS (or after date if no FMS):
    // Signal quality, Satellites
    
    let hour_raw = payload.get(4..10).context("V3: missing hour")?;
    let lat_raw = payload.get(10..18).context("V3: missing latitude")?;
    let lon_raw = payload.get(18..26).context("V3: missing longitude")?;
    let speed_raw = payload.get(26..30).context("V3: missing speed")?;
    let heading_raw = payload.get(30..32).context("V3: missing heading")?;
    let power_raw = payload.get(32..34).context("V3: missing power")?;
    let fuel_raw = payload.get(34..36).context("V3: missing fuel")?;
    let mems_raw = payload.get(36..42).context("V3: missing mems")?;
    let flags_raw = payload.get(42..44).context("V3: missing flags")?;
    let temp_raw = payload.get(44..48).context("V3: missing temp")?;
    let odo_raw = payload.get(48..56).context("V3: missing odometer")?;
    let send_flag_raw = payload.get(56..58).context("V3: missing send_flag")?;
    let added_info_raw = payload.get(58..66).context("V3: missing added_info")?;
    let date_raw = payload.get(66..70).context("V3: missing date")?;

    let (recorded_at, is_real_time) = decode_timestamp(hour_raw, date_raw)?;
    let latitude = decode_coordinate(lat_raw, flags_raw, 0x01, true)?;
    let longitude = decode_coordinate(lon_raw, flags_raw, 0x02, false)?;
    let speed_kph = parse_speed(speed_raw)?;
    let heading_deg = parse_heading(heading_raw)?;
    let (power_voltage, power_source_rescue) = decode_power(power_raw);
    let ignition_on = decode_bit(flags_raw, 0x04);
    let mems = decode_mems(mems_raw)?;
    let temperature_raw = u16::from_str_radix(temp_raw, 16)?;
    let base_odometer = u32::from_str_radix(odo_raw, 16)?;
    let send_flag = u8::from_str_radix(send_flag_raw, 16)?;
    let added_info = u32::from_str_radix(added_info_raw, 16)?;
    let base_fuel = u8::from_str_radix(fuel_raw, 16)?;

    // Check if FMS data is present (frame length >= 100 chars)
    let has_fms_data = payload.len() >= 100;
    
    // Parse FMS data if present
    let (fms_fuel, fms_odometer, fms_rpm) = if has_fms_data {
        let fms_fuel = payload.get(70..72)
            .and_then(|s| u8::from_str_radix(s, 16).ok())
            .filter(|&v| v > 0 && v <= 100);
        let fms_odo = payload.get(74..82)
            .and_then(|s| u32::from_str_radix(s, 16).ok())
            .filter(|&v| v > 0);
        let fms_rpm = payload.get(84..88)
            .and_then(|s| u16::from_str_radix(s, 16).ok())
            .filter(|&v| v > 0);
        (fms_fuel, fms_odo, fms_rpm)
    } else {
        (None, None, None)
    };
    
    // Use FMS values if base values are 0
    let fuel_final = if base_fuel == 0 { fms_fuel.unwrap_or(0) } else { base_fuel };
    let odometer_km = if base_odometer == 0 { fms_odometer.unwrap_or(0) } else { base_odometer };
    
    // Signal/Satellites position depends on FMS data presence
    let (signal_quality, satellites_in_view, remaining_payload) = if has_fms_data {
        // After FMS data (position 100+)
        (
            payload.get(100..102).and_then(|s| u8::from_str_radix(s, 16).ok()),
            payload.get(102..104).and_then(|s| u8::from_str_radix(s, 16).ok()),
            payload.get(104..).map(ToOwned::to_owned)
        )
    } else {
        // No FMS - right after date (position 70+)
        (
            payload.get(70..72).and_then(|s| u8::from_str_radix(s, 16).ok()),
            payload.get(72..74).and_then(|s| u8::from_str_radix(s, 16).ok()),
            payload.get(74..).map(ToOwned::to_owned)
        )
    };

    Ok(HhFrame {
        kind,
        version,
        recorded_at,
        latitude,
        longitude,
        speed_kph,
        heading_deg,
        power_voltage,
        power_source_rescue,
        fuel_raw: fuel_final,
        ignition_on,
        mems_x: mems.0,
        mems_y: mems.1,
        mems_z: mems.2,
        temperature_raw,
        odometer_km,
        send_flag,
        added_info,
        signal_quality,
        satellites_in_view,
        rpm: fms_rpm,
        is_valid: decode_bit(flags_raw, 0x40),
        is_real_time,
        flags_raw: u8::from_str_radix(flags_raw, 16)?,
        raw_payload: payload.to_string(),
        remaining_payload,
        address: None,
    })
}

fn ensure_payload_len(payload: &str, min_len: usize) -> Result<()> {
    if payload.len() < min_len {
        anyhow::bail!("payload too short: {} < {}", payload.len(), min_len);
    }
    Ok(())
}

fn decode_timestamp(hour_raw: &str, date_raw: &str) -> Result<(NaiveDateTime, bool)> {
    let total_secs = u32::from_str_radix(hour_raw, 16)?;
    let hour = (total_secs / 3600) % 24;
    let minute = (total_secs % 3600) / 60;
    let second = total_secs % 60;

    let date_val = u32::from_str_radix(date_raw, 16)?;
    let day = (date_val % 31) + 1;
    let month = ((date_val / 31) % 12) + 1;
    let year = (date_val / (31 * 12)) + 2000;

    let date = NaiveDate::from_ymd_opt(year as i32, month as u32, day as u32)
        .context("invalid date in frame")?;
    let time = NaiveTime::from_hms_opt(hour, minute, second).context("invalid time in frame")?;

    let is_real_time = true; // HH doc indicates last flag bit; adjust if necessary
    Ok((NaiveDateTime::new(date, time), is_real_time))
}

fn decode_coordinate(raw: &str, flags_raw: &str, bit_mask: u8, _is_lat: bool) -> Result<f64> {
    let value = i64::from_str_radix(raw, 16)?;
    
    // GISV1 formula: String.Format("{0:D2}", raw / 1000000) + "." + String.Format("{0:D6}", (raw % 1000000) * 100 / 60)
    // The D6 format means ALWAYS 6 digits, so divisor is always 1,000,000
    let degrees = value / 1_000_000;  // Integer division
    let minutes_part = value % 1_000_000;
    let decimal_int = (minutes_part * 100) / 60;  // Integer division like C#
    
    // IMPORTANT: GISV1 uses D6 format = always 6 digits, so always divide by 1,000,000
    let mut coord = degrees as f64 + (decimal_int as f64 / 1_000_000.0);

    let flags = u8::from_str_radix(flags_raw, 16)?;
    let is_positive = (flags & bit_mask) != 0;
    if !is_positive {
        coord = -coord;
    }

    Ok(coord)
}

fn parse_speed(raw: &str) -> Result<f64> {
    // GISV1 formula: (int)(raw / 10 * 1.609) - uses integer division
    let raw_val = u32::from_str_radix(raw, 16)?;
    Ok(((raw_val / 10) as f64) * 1.609)
}

fn parse_heading(raw: &str) -> Result<f64> {
    Ok(u32::from_str_radix(raw, 16)? as f64)
}

fn decode_power(raw: &str) -> (u8, bool) {
    let value = u8::from_str_radix(raw, 16).unwrap_or(0);
    let source_rescue = value >= 128;
    let voltage = value % 128;
    (voltage, source_rescue)
}

fn decode_bit(flags_raw: &str, mask: u8) -> bool {
    u8::from_str_radix(flags_raw, 16)
        .map(|flags| (flags & mask) != 0)
        .unwrap_or(false)
}

fn decode_mems(raw: &str) -> Result<(i8, i8, i8)> {
    let x = parse_signed_byte(&raw[0..2])?;
    let y = parse_signed_byte(&raw[2..4])?;
    let z = parse_signed_byte(&raw[4..6])?;
    Ok((x, y, z))
}

fn parse_signed_byte(raw: &str) -> Result<i8> {
    let value = u8::from_str_radix(raw, 16)?;
    Ok(if value >= 128 {
        (value as i16 - 256) as i8
    } else {
        value as i8
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_v3_sample_frame() {
        let payload = "HH130094F80228D3D20099CF4F00000A2926FC04FBE780FB00000000010000000016630B17";
        let frame = parse_frame(payload).expect("frame should parse");

        assert!(matches!(frame.version, FrameVersion::V3));
        assert!(matches!(frame.kind, FrameKind::RealTimeAndHistory));
        assert_eq!(frame.recorded_at, chrono::NaiveDate::from_ymd_opt(2015, 5, 28).unwrap().and_hms_opt(10, 35, 36).unwrap());

        // Coordinates converted from NMEA (DDMM.MMMM) to decimal degrees
        // 3623.0098 -> 36 + 23.0098/60 = 36.3835
        // 1008.0079 -> 10 + 08.0079/60 = 10.1335
        assert!((frame.latitude - 36.3835).abs() < 0.001, "lat was {}", frame.latitude);
        assert!((frame.longitude - 10.1335).abs() < 0.001, "lng was {}", frame.longitude);
        assert_eq!(frame.speed_kph, 0.0);
        assert_eq!(frame.heading_deg, 10.0);

        assert_eq!(frame.power_voltage, 41);
        assert!(!frame.power_source_rescue);
        assert_eq!(frame.fuel_raw, 0x26);
        assert!(frame.ignition_on);
        assert_eq!((frame.mems_x, frame.mems_y, frame.mems_z), (-4, 4, -5));
        assert_eq!(frame.temperature_raw, 0x80FB);
        assert_eq!(frame.odometer_km, 0);
        assert_eq!(frame.send_flag, 0x01);
        assert_eq!(frame.added_info, 0);
        assert_eq!(frame.signal_quality, Some(11));
        assert_eq!(frame.satellites_in_view, Some(23));
        assert!(frame.rpm.is_none());
        assert!(frame.is_valid);
        assert!(frame.is_real_time);
        assert_eq!(frame.flags_raw, 0xE7);
    }

    #[test]
    fn parse_info_sample_frame() {
        let payload = "HH011.0.103R10, ICC:8921602050440128136F, IMEI:861001002935274";
        let info = parse_info_frame(payload).expect("info frame should parse");
        assert_eq!(info.firmware_version, "1.0.103R10");
        assert_eq!(info.icc_id.as_deref(), Some("8921602050440128136F"));
        assert_eq!(info.imei.as_deref(), Some("861001002935274"));
    }
}
