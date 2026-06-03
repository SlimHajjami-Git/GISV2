use anyhow::{anyhow, bail, Result};
use chrono::{Datelike, NaiveDate, NaiveDateTime, NaiveTime};
use tracing::{debug, info};

use super::model::{FrameKind, FrameVersion, HhFrame};

// ============================================================================
// Coban TK103 / TK303 ASCII protocol (and TK104, TK06, GT02 — all share the
// same wire format with minor sentence-type variations).
// ============================================================================
//
// Two coexisting sentence families:
//
//   A) *HQ format (TK103 native)
//      *HQ,<IMEI>,<type>,<HHMMSS>,A,<lat>,<N|S>,<lon>,<E|W>,<spd>,<crs>,<DDMMYY>,...#
//      e.g.
//      *HQ,358640052173468,V1,054200,A,3145.2530,N,03517.4528,E,000.0,000,180326,FFFFFBFF#
//
//      Sentence types we read:
//          V1   periodic position (most common, every interval)
//          V4   command result
//          BO   SOS / panic button
//          BR   battery low
//          BS   speeding
//
//   B) imei: format (used by many TK103 clones that announce IMEI on connect)
//      imei:<imei>,<event>,<datetime>,<phone>,F,<HHMMSS.SSS>,A,<lat>,<N|S>,<lon>,<E|W>,<spd>,<crs>;
//      e.g.
//      imei:359710045748769,tracker,240315120013,,F,120013.000,A,4044.5036,N,12136.3267,W,0.00,0;
//
//      Events: "tracker" = periodic, "help me" = SOS, "low battery", "stockade" = geofence,
//              "move" = movement after stop, "speed" = overspeed.
//
// Lat/lon in BOTH families are DDMM.MMMM (NMEA-style "degrees and minutes"),
// not decimal degrees — we convert.
// ============================================================================

/// Decoded result of one sentence. The caller streams sentences (one per
/// payload typically, but devices sometimes batch them with newlines).
#[derive(Debug)]
pub enum TkDecodeResult {
    /// Device announced its IMEI on connection — typical first packet for the
    /// `imei:` family. We use it the same way as a Teltonika or Noron login:
    /// look up the device, ACK with `LOAD\r\n` (industry-standard ack), and
    /// register the IMEI for the duration of the connection.
    Login { imei: String },

    /// Position-bearing sentence.
    Position { frame: HhFrame, imei: String },

    /// Heartbeat with no GPS (some clones emit `KA` packets or empty positions).
    Heartbeat { imei: Option<String> },

    /// Sentence we don't decode yet (e.g. a vendor extension) — we keep
    /// the connection but log the head so an operator can decide whether
    /// to add it.
    Unknown { head: String },
}

/// Parse one sentence. The caller is responsible for splitting on `#`
/// or `;` boundaries (or newlines, depending on the device) — see
/// `extract_sentences` below.
pub fn parse_sentence(line: &str) -> Result<TkDecodeResult> {
    // Strip framing. GPS103 logins arrive as `##,imei:<IMEI>,A;` — trimming the
    // leading `##`/`;` leaves a `,imei:...` we normalise by dropping the comma.
    let trimmed = line
        .trim_matches(|c: char| c.is_whitespace() || c == '#' || c == ';')
        .trim_start_matches(',')
        .trim();
    if trimmed.is_empty() {
        return Ok(TkDecodeResult::Heartbeat { imei: None });
    }

    if trimmed.starts_with("*HQ,") || trimmed.starts_with("HQ,") {
        return parse_hq(trimmed);
    }

    if let Some(rest) = trimmed.strip_prefix("imei:") {
        return parse_imei_form(rest);
    }

    // Bare `<IMEI>;` keepalive — many TK103/TK303/GPS103 clones announce and
    // ping with just the 15-digit IMEI. Treat it as a heartbeat that carries
    // the IMEI so the handler keeps the device online and attributes later
    // position sentences to it.
    if (14..=17).contains(&trimmed.len()) && trimmed.bytes().all(|b| b.is_ascii_digit()) {
        return Ok(TkDecodeResult::Heartbeat { imei: Some(trimmed.to_string()) });
    }

    debug!(head = %&trimmed[..trimmed.len().min(32)], "TK103 unknown sentence");
    Ok(TkDecodeResult::Unknown { head: trimmed[..trimmed.len().min(64)].to_string() })
}

/// Standard ACK Coban TK families expect after a successful login. Many
/// clones don't actually need this but the canonical TK103 reference
/// implementation expects `LOAD\r\n` after the first IMEI announcement to
/// keep the device from re-sending its login packet every few seconds.
pub const LOGIN_ACK: &[u8] = b"LOAD\r\n";

/// "ON\r\n" is what TK103 returns to acknowledge a keepalive ping. Used
/// by some firmwares as a heartbeat probe.
pub const KEEPALIVE_ACK: &[u8] = b"ON\r\n";

/// Mirror GISV1's TK103 date handling (getDateTimeTK103_2 / getDateTimeTK102):
/// keep the device's reported time only when it is plausible, otherwise fall
/// back to the server-receive time (GISV1 defaulted to `DateTime.Now` when the
/// device date was missing or garbled). Same approach as the GT06 RTC guard.
fn plausible_or_server_time(parsed: Result<NaiveDateTime>) -> NaiveDateTime {
    match parsed {
        Ok(dt) if dt.year() >= 2020 => dt,
        _ => chrono::Utc::now().naive_utc(),
    }
}

// ─── *HQ family parser ──────────────────────────────────────────────────────
//
// Example:
//   *HQ,358640052173468,V1,054200,A,3145.2530,N,03517.4528,E,000.0,000,180326,FFFFFBFF#

fn parse_hq(s: &str) -> Result<TkDecodeResult> {
    let body = s.strip_prefix("*HQ,").or_else(|| s.strip_prefix("HQ,"))
        .ok_or_else(|| anyhow!("not an HQ sentence"))?;
    let parts: Vec<&str> = body.split(',').collect();

    // Minimum useful: IMEI, type, HHMMSS, valid, lat, NS, lon, EW, spd, crs, DDMMYY → 11 fields
    if parts.len() < 11 {
        bail!("HQ sentence too short: {} fields", parts.len());
    }

    let imei = parts[0].to_string();
    let sentence_type = parts[1];
    let hhmmss = parts[2];
    let valid_flag = parts[3]; // 'A' = valid, 'V' = void
    let lat_str = parts[4];
    let ns = parts[5];
    let lon_str = parts[6];
    let ew = parts[7];
    let speed = parts[8].parse::<f64>().unwrap_or(0.0);
    let course = parts[9].parse::<f64>().unwrap_or(0.0);
    let ddmmyy = parts[10];

    let recorded_at = plausible_or_server_time(parse_hq_datetime(ddmmyy, hhmmss));
    let latitude = parse_nmea_degrees(lat_str, ns == "S")?;
    let longitude = parse_nmea_degrees(lon_str, ew == "W")?;
    let is_valid = valid_flag.eq_ignore_ascii_case("A")
        && (-90.0..=90.0).contains(&latitude)
        && (-180.0..=180.0).contains(&longitude)
        && (latitude != 0.0 || longitude != 0.0);

    let send_flag = match sentence_type {
        "V1" => 1,
        "V4" => 0,
        "BO" => 11, // SOS / panic
        "BR" => 11, // Battery low alarm
        "BS" => 5,  // Overspeed
        _ => 1,
    };

    let frame = HhFrame {
        kind: FrameKind::RealTime,
        version: FrameVersion::V1,
        recorded_at,
        latitude,
        longitude,
        speed_kph: speed,
        heading_deg: course.clamp(0.0, 360.0),
        power_voltage: 0,
        power_source_rescue: false,
        fuel_raw: 0,
        ignition_on: speed > 0.5,
        mems_x: 0, mems_y: 0, mems_z: 0,
        temperature_raw: 0,
        odometer_km: 0,
        send_flag,
        added_info: 0,
        signal_quality: None,
        satellites_in_view: None,
        rpm: None,
        fuel_rate_l_per_100km: None,
        fms_temperature_c: None,
        is_valid,
        is_real_time: true,
        flags_raw: 0,
        raw_payload: format!("TK103:HQ {} lat={:.6} lon={:.6} spd={}", sentence_type, latitude, longitude, speed),
        remaining_payload: None,
        address: None,
    };

    info!(
        imei = %imei,
        ty = sentence_type,
        lat = latitude,
        lon = longitude,
        speed,
        is_valid,
        "TK103 HQ sentence decoded"
    );

    Ok(TkDecodeResult::Position { frame, imei })
}

fn parse_hq_datetime(ddmmyy: &str, hhmmss: &str) -> Result<NaiveDateTime> {
    if ddmmyy.len() != 6 || hhmmss.len() < 6 {
        bail!("HQ date/time malformed: {} {}", ddmmyy, hhmmss);
    }
    let day = ddmmyy[0..2].parse::<u32>()?;
    let month = ddmmyy[2..4].parse::<u32>()?;
    let year_2d = ddmmyy[4..6].parse::<i32>()?;
    let year = 2000 + year_2d;
    let hour = hhmmss[0..2].parse::<u32>()?;
    let minute = hhmmss[2..4].parse::<u32>()?;
    let second = hhmmss[4..6].parse::<u32>()?;

    let date = NaiveDate::from_ymd_opt(year, month, day)
        .ok_or_else(|| anyhow!("HQ invalid date {}-{}-{}", year, month, day))?;
    let time = NaiveTime::from_hms_opt(hour, minute, second)
        .ok_or_else(|| anyhow!("HQ invalid time {}:{}:{}", hour, minute, second))?;
    Ok(NaiveDateTime::new(date, time))
}

// ─── imei: family parser ────────────────────────────────────────────────────
//
// Example:
//   imei:359710045748769,tracker,240315120013,,F,120013.000,A,4044.5036,N,12136.3267,W,0.00,0
//   imei:359710045748769,help me,...                (SOS)
//   imei:359710045748769,low battery,...            (alarm)

fn parse_imei_form(s: &str) -> Result<TkDecodeResult> {
    let parts: Vec<&str> = s.split(',').collect();
    if parts.is_empty() {
        bail!("imei: sentence empty");
    }

    let imei = parts[0].to_string();

    // Lone `imei:<IMEI>` — first packet of the connection. The device just
    // announces itself; we register the IMEI and ACK with LOAD\r\n.
    if parts.len() < 2 {
        return Ok(TkDecodeResult::Login { imei });
    }

    let event = parts[1];

    // Connection / login with an event tag but no GPS yet — still a login.
    if parts.len() < 5 {
        return Ok(TkDecodeResult::Login { imei });
    }

    // Position payload requires at least 12 fields.
    if parts.len() < 12 {
        // Lone heartbeat with event tag only — no GPS.
        return Ok(TkDecodeResult::Heartbeat { imei: Some(imei) });
    }

    let datetime_str = parts[2];      // YYMMDDHHMM
    let _phone = parts[3];
    let _gps_flag = parts[4];         // 'F' (fixed) or 'L'
    let hhmmss = parts[5];            // HHMMSS.SSS
    let valid = parts[6];             // 'A' or 'V'
    let lat_str = parts[7];
    let ns = parts[8];
    let lon_str = parts[9];
    let ew = parts[10];
    let speed = parts[11].parse::<f64>().unwrap_or(0.0);
    let course = parts.get(12).and_then(|s| s.parse::<f64>().ok()).unwrap_or(0.0);

    let recorded_at = plausible_or_server_time(parse_imei_datetime(datetime_str, hhmmss));
    let latitude = parse_nmea_degrees(lat_str, ns == "S")?;
    let longitude = parse_nmea_degrees(lon_str, ew == "W")?;
    let is_valid = valid.eq_ignore_ascii_case("A")
        && (-90.0..=90.0).contains(&latitude)
        && (-180.0..=180.0).contains(&longitude)
        && (latitude != 0.0 || longitude != 0.0);

    let send_flag = match event {
        "tracker" => 1,
        "help me" | "sos" => 11,
        "low battery" => 11,
        "move" | "stockade" => 4,
        "speed" | "overspeed" => 5,
        "stop" => 4,
        _ => 1,
    };

    let frame = HhFrame {
        kind: FrameKind::RealTime,
        version: FrameVersion::V1,
        recorded_at,
        latitude,
        longitude,
        speed_kph: speed * 1.852, // imei: format reports speed in KNOTS, not km/h
        heading_deg: course.clamp(0.0, 360.0),
        power_voltage: 0,
        power_source_rescue: false,
        fuel_raw: 0,
        ignition_on: speed > 0.3,
        mems_x: 0, mems_y: 0, mems_z: 0,
        temperature_raw: 0,
        odometer_km: 0,
        send_flag,
        added_info: 0,
        signal_quality: None,
        satellites_in_view: None,
        rpm: None,
        fuel_rate_l_per_100km: None,
        fms_temperature_c: None,
        is_valid,
        is_real_time: true,
        flags_raw: 0,
        raw_payload: format!("TK103:imei event={} lat={:.6} lon={:.6} spd_kn={}", event, latitude, longitude, speed),
        remaining_payload: None,
        address: None,
    };

    info!(
        imei = %imei,
        event,
        lat = latitude,
        lon = longitude,
        is_valid,
        "TK103 imei: sentence decoded"
    );

    Ok(TkDecodeResult::Position { frame, imei })
}

fn parse_imei_datetime(yymmddhhmm: &str, hhmmss_ss: &str) -> Result<NaiveDateTime> {
    if yymmddhhmm.len() < 10 || hhmmss_ss.len() < 6 {
        bail!("imei date/time malformed: {} {}", yymmddhhmm, hhmmss_ss);
    }
    let year = 2000 + yymmddhhmm[0..2].parse::<i32>()?;
    let month = yymmddhhmm[2..4].parse::<u32>()?;
    let day = yymmddhhmm[4..6].parse::<u32>()?;
    // The hhmmss field overrides the hour/minute from the datetime string
    // because it has sub-second resolution; we still take the date from the
    // datetime string and the time from hhmmss.
    let hour = hhmmss_ss[0..2].parse::<u32>()?;
    let minute = hhmmss_ss[2..4].parse::<u32>()?;
    let second = hhmmss_ss[4..6].parse::<u32>()?;

    let date = NaiveDate::from_ymd_opt(year, month, day)
        .ok_or_else(|| anyhow!("imei invalid date {}-{}-{}", year, month, day))?;
    let time = NaiveTime::from_hms_opt(hour, minute, second)
        .ok_or_else(|| anyhow!("imei invalid time {}:{}:{}", hour, minute, second))?;
    Ok(NaiveDateTime::new(date, time))
}

/// Convert NMEA "DDMM.MMMM" or "DDDMM.MMMM" to decimal degrees.
/// The integer part is degrees + minutes packed: the rightmost two digits
/// before the decimal are minutes, the rest is degrees.
///
/// Example: "4044.5036" → 40° 44.5036' → 40.7417266…° decimal.
fn parse_nmea_degrees(raw: &str, negate: bool) -> Result<f64> {
    let dot = raw.find('.').unwrap_or(raw.len());
    if dot < 2 {
        bail!("NMEA degrees too short: {}", raw);
    }
    let (deg_part, min_part) = raw.split_at(dot - 2);
    let degrees: f64 = if deg_part.is_empty() { 0.0 } else { deg_part.parse()? };
    let minutes: f64 = min_part.parse()?;
    let mut value = degrees + minutes / 60.0;
    if negate {
        value = -value;
    }
    Ok(value)
}

/// Split a raw TCP read into individual sentences.
/// TK103 family uses `#` (HQ) or `;` (imei:) as terminators, with optional
/// newlines between batched sentences.
pub fn extract_sentences(raw: &str) -> Vec<String> {
    let mut out = Vec::new();
    for chunk in raw.split(|c: char| c == '#' || c == ';' || c == '\n' || c == '\r') {
        let trimmed = chunk.trim();
        if !trimmed.is_empty() {
            out.push(trimmed.to_string());
        }
    }
    out
}

// ─── Tests ──────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_canonical_hq_v1() {
        let line = "*HQ,358640052173468,V1,054200,A,3145.2530,N,03517.4528,E,000.0,000,180326,FFFFFBFF";
        match parse_sentence(line).unwrap() {
            TkDecodeResult::Position { frame, imei } => {
                assert_eq!(imei, "358640052173468");
                assert!(frame.is_valid);
                assert!((frame.latitude - 31.754216).abs() < 0.001);
                assert!((frame.longitude - 35.290880).abs() < 0.001);
                assert_eq!(frame.send_flag, 1);
            }
            other => panic!("expected Position, got {:?}", other),
        }
    }

    #[test]
    fn parses_imei_tracker() {
        let line = "imei:359710045748769,tracker,240315120013,,F,120013.000,A,4044.5036,N,12136.3267,W,0.00,0";
        match parse_sentence(line).unwrap() {
            TkDecodeResult::Position { frame, imei } => {
                assert_eq!(imei, "359710045748769");
                assert!(frame.is_valid);
                assert!((frame.latitude - 40.741726).abs() < 0.001);
                assert!((frame.longitude + 121.605445).abs() < 0.001); // west → negative
            }
            other => panic!("expected Position, got {:?}", other),
        }
    }

    #[test]
    fn imei_login_without_payload_is_login() {
        let line = "imei:359710045748769";
        match parse_sentence(line).unwrap() {
            TkDecodeResult::Login { imei } => assert_eq!(imei, "359710045748769"),
            other => panic!("expected Login, got {:?}", other),
        }
    }

    #[test]
    fn sos_event_lifts_send_flag() {
        let line = "imei:359710045748769,help me,240315120013,,F,120013.000,A,4044.5036,N,12136.3267,W,0.00,0";
        if let TkDecodeResult::Position { frame, .. } = parse_sentence(line).unwrap() {
            assert_eq!(frame.send_flag, 11);
        }
    }

    #[test]
    fn batched_sentences_extract_correctly() {
        let raw = "*HQ,123,V1,054200,A,3145.2530,N,03517.4528,E,000.0,000,180326,FF#imei:456;";
        let sentences = extract_sentences(raw);
        assert_eq!(sentences.len(), 2);
        assert!(sentences[0].starts_with("*HQ"));
        assert!(sentences[1].starts_with("imei:"));
    }

    #[test]
    fn nmea_degrees_decodes_correctly() {
        // 40° 44.5036' N = 40.741726…
        let v = parse_nmea_degrees("4044.5036", false).unwrap();
        assert!((v - 40.74172).abs() < 0.0001);
        // 121° 36.3267' W = -121.60544…
        let v = parse_nmea_degrees("12136.3267", true).unwrap();
        assert!((v + 121.60545).abs() < 0.0001);
    }

    #[test]
    fn bare_imei_is_heartbeat_with_imei() {
        // GPS103 keepalive: just the IMEI + ';'
        match parse_sentence("863844051418157;").unwrap() {
            TkDecodeResult::Heartbeat { imei } => assert_eq!(imei.as_deref(), Some("863844051418157")),
            other => panic!("expected Heartbeat with imei, got {:?}", other),
        }
    }

    #[test]
    fn gps103_login_with_hashes_is_login() {
        // '##,imei:<IMEI>,A;' — leading ## must not break IMEI extraction
        match parse_sentence("##,imei:863844051418157,A;").unwrap() {
            TkDecodeResult::Login { imei } => assert_eq!(imei, "863844051418157"),
            other => panic!("expected Login, got {:?}", other),
        }
    }
}
