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
pub fn parse_info_frame(payload: &str) -> Result<HhInfoFrame> {
    let payload = payload.trim();
    
    // Handle HH00/AA00 connect frames: may have prefix like "NR08G0663 AA00..."
    // Example: NR08G0663 AA00123634281125@25/11/28,12:36:31+00 ,ICC:..., IMEI:...
    if let Some(aa_pos) = payload.find("AA00").or_else(|| payload.find("HH00")) {
        return parse_connect_frame(&payload[aa_pos..]);
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
    })
}

/// Parse HH00/AA00 connect frames
/// Example: AA00123634281125@25/11/28,12:36:31+00 ,ICC:89216020803464581196F, IMEI:860141071569116
fn parse_connect_frame(payload: &str) -> Result<HhInfoFrame> {
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
    ensure_payload_len(payload, 70)?;
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

    let (recorded_at, is_real_time) = decode_timestamp(hour_raw, date_raw)?;
    let latitude = decode_coordinate(lat_raw, flags_raw, 0x01, true)?;
    let longitude = decode_coordinate(lon_raw, flags_raw, 0x02, false)?;
    let speed_kph = parse_speed(speed_raw)?;
    let heading_deg = parse_heading(heading_raw)?;
    let (power_voltage, power_source_rescue) = decode_power(power_raw);
    let ignition_on = decode_bit(flags_raw, 0x04);
    let mems = decode_mems(mems_raw)?;
    let temperature_raw = u16::from_str_radix(temp_raw, 16)?;
    let odometer_km = u32::from_str_radix(odo_raw, 16)?;
    let send_flag = u8::from_str_radix(send_flag_raw, 16)?;
    let added_info = u32::from_str_radix(added_info_raw, 16)?;

    // Parse base fuel from position 34-35
    let base_fuel = u8::from_str_radix(fuel_raw, 16)?;
    
    // GISV1 Logic: If base fuel is 0, try FMS fuel as fallback
    // For V1/V2, FMS fuel is at position 54-55 (2 hex chars) if frame is long enough
    let fuel_final = if base_fuel == 0 && payload.len() >= 56 {
        // Try FMS fuel at position 54-55
        payload.get(54..56)
            .and_then(|s| u8::from_str_radix(s, 16).ok())
            .filter(|&v| v > 0 && v <= 100)
            .unwrap_or(base_fuel)
    } else {
        base_fuel
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
    ensure_payload_len(payload, 74)?;
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

    let (recorded_at, is_real_time) = decode_timestamp(hour_raw, date_raw)?;
    let latitude = decode_coordinate(lat_raw, flags_raw, 0x01, true)?;
    let longitude = decode_coordinate(lon_raw, flags_raw, 0x02, false)?;
    let speed_kph = parse_speed(speed_raw)?;
    let heading_deg = parse_heading(heading_raw)?;
    let (power_voltage, power_source_rescue) = decode_power(power_raw);
    let ignition_on = decode_bit(flags_raw, 0x04);
    let mems = decode_mems(mems_raw)?;
    let temperature_raw = u16::from_str_radix(temp_raw, 16)?;
    let odometer_km = u32::from_str_radix(odo_raw, 16)?;
    let send_flag = u8::from_str_radix(send_flag_raw, 16)?;
    let added_info = u32::from_str_radix(added_info_raw, 16)?;

    let signal_quality = payload.get(70..72).and_then(|s| u8::from_str_radix(s, 16).ok());
    let satellites_in_view = payload.get(72..74).and_then(|s| u8::from_str_radix(s, 16).ok());
    let remaining_payload = payload.get(74..).map(ToOwned::to_owned);

    // Parse base fuel from position 34-35
    let base_fuel = u8::from_str_radix(fuel_raw, 16)?;
    
    // GISV1 Logic: If base fuel is 0, try multiple FMS positions as fallback
    // V3 (NR08/NR09) can have fuel at positions: 54, 70, 82, 86, 90, 94 (2 hex chars each)
    let fuel_final = if base_fuel == 0 {
        let possible_positions: &[usize] = &[54, 70, 82, 86, 90, 94];
        let mut found_fuel: Option<u8> = None;
        
        for &pos in possible_positions {
            if payload.len() >= pos + 2 {
                if let Some(raw) = payload.get(pos..pos+2) {
                    if let Ok(val) = u8::from_str_radix(raw, 16) {
                        if val > 0 && val <= 100 {
                            tracing::info!(
                                position = pos,
                                fuel_value = val,
                                "V3: FMS Fuel found"
                            );
                            found_fuel = Some(val);
                            break;
                        }
                    }
                }
            }
        }
        
        // If still not found, try position 78-81 (4 bytes fuel level in 0.5L)
        if found_fuel.is_none() && payload.len() >= 82 {
            if let Some(raw) = payload.get(78..82) {
                if let Ok(val) = u16::from_str_radix(raw, 16) {
                    if val > 0 && val < 65535 {
                        // Convert to percentage (assuming 80L tank max)
                        let pct = std::cmp::min(100, ((val / 2) * 100 / 80) as u8);
                        if pct > 0 {
                            tracing::info!(
                                fuel_level_raw = val,
                                fuel_percent = pct,
                                "V3: FMS Fuel Level at 78 converted"
                            );
                            found_fuel = Some(pct);
                        }
                    }
                }
            }
        }
        
        if found_fuel.is_none() {
            tracing::debug!(
                payload_len = payload.len(),
                "V3: No FMS fuel found at any position"
            );
        }
        
        found_fuel.unwrap_or(base_fuel)
    } else {
        base_fuel
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
        rpm: None,
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
