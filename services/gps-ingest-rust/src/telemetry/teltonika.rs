use anyhow::{anyhow, Result};
use nom_teltonika::{
    parser, AVLEventIOValue, AVLFrame, AVLRecord, Codec, Priority, TeltonikaFrame,
};
use tracing::{debug, info};

use super::model::{FrameKind, FrameVersion, HhFrame};

// ============================================================================
// Teltonika Codec 8 / 8 Extended / 16 Decoder (FMB130, FMB150, FMC, FMU, FMM)
// ============================================================================
//
// Protocol layout — TCP only (UDP is handled by a separate datagram codec):
//
//   ┌─ Login (first packet sent by the device on every TCP connection) ─┐
//   │  [u16 BE length] [ASCII IMEI bytes]                                │
//   │  → server replies with 1 byte: 0x01 = accept, 0x00 = reject       │
//   └────────────────────────────────────────────────────────────────────┘
//
//   ┌─ AVL frame (sent repeatedly after a successful login) ────────────┐
//   │  [u32 BE preamble = 0x00000000]                                    │
//   │  [u32 BE length]   ← length of [codec ... CRC16 prefix bytes]      │
//   │  [u8 codec]        ← 0x08 = Codec 8, 0x8E = Codec 8 Extended       │
//   │  [u8 record count] ← N                                             │
//   │  [N x AVL record]                                                  │
//   │  [u8 record count] ← N (repeated)                                  │
//   │  [u32 BE CRC16]                                                    │
//   │  → server replies with 4 bytes: u32 BE = number of records ack'd   │
//   └────────────────────────────────────────────────────────────────────┘
//
// We delegate the binary parsing to `nom-teltonika` (MIT, v0.1.x). This module
// owns only:
//   1. Identifying the AVL IO IDs we care about and mapping them onto the
//      canonical `HhFrame` fields (same struct NEMS L/S and Noron use).
//   2. Rescaling MEMS axis values from Teltonika's signed milli-g to the
//      int8 range NEMS uses, so the downstream V7 accident detector keeps
//      its `>= 100` magnitude threshold without any tuning.
//
// IO ID assignments come from the Teltonika wiki (cross-checked against
// Traccar's `TeltonikaProtocolDecoder.java` Apache 2.0 reference, which has
// the canonical list for every FMB/FMC/FMU/FMM device). Anything not in this
// match arm is logged at debug level and ignored — it can be added later as
// the operator needs it without touching the parser.
// ============================================================================

// ─── AVL IO IDs we map to canonical HhFrame fields ──────────────────────────

const IO_DIGITAL_INPUT_1: u16 = 1;
const IO_DIGITAL_INPUT_2: u16 = 2;
const IO_TOTAL_ODOMETER_M: u16 = 16;
const IO_AXIS_X: u16 = 17;
const IO_AXIS_Y: u16 = 18;
const IO_AXIS_Z: u16 = 19;
const IO_GSM_SIGNAL: u16 = 21;
const IO_SPEED_KPH: u16 = 24;
const IO_RPM: u16 = 36;
const IO_EXTERNAL_VOLTAGE_MV: u16 = 66;
const IO_BATTERY_VOLTAGE_MV: u16 = 67;
const IO_TEMP_SENSOR_1: u16 = 72;
const IO_TEMP_SENSOR_2: u16 = 73;
const IO_IGNITION: u16 = 239;
const IO_MOVEMENT: u16 = 240;
const IO_CRASH_DETECTION: u16 = 247;
const IO_GREEN_DRIVING_TYPE: u16 = 253;
const IO_CRASH_TRACE: u16 = 257;

// ─── Public surface ─────────────────────────────────────────────────────────

/// Parse the IMEI sent by the device immediately after opening the TCP
/// connection. The expected format is `[u16 BE length] [ASCII IMEI]`.
///
/// Returns the IMEI string (typically 15 ASCII digits) on success. The caller
/// is responsible for ACK'ing with `0x01` (accept) or `0x00` (reject) and for
/// resolving the IMEI against the `gps_devices` table.
pub fn parse_imei(buffer: &[u8]) -> Result<String> {
    let (_, imei) = parser::imei(buffer)
        .map_err(|e| anyhow!("Teltonika IMEI parse failed: {e:?}"))?;
    Ok(imei)
}

/// Parse one TCP frame (preamble + length + codec + records + CRC) and
/// return its `AVLFrame` body. `nom-teltonika` actually models the TCP
/// envelope as `TeltonikaFrame` (an enum AVL | GPRS); we only handle the
/// AVL branch here since GPRS frames are command responses sent in reply
/// to our own Codec 12/13/14 requests (a downlink we never initiate).
///
/// Returns an error on a malformed buffer OR on an unexpected GPRS frame
/// — both situations are best handled by the caller as "not yet enough
/// bytes, keep reading".
pub fn parse_avl_frame(buffer: &[u8]) -> Result<AVLFrame> {
    let (_, parsed) = parser::tcp_frame(buffer)
        .map_err(|e| anyhow!("Teltonika TCP frame parse failed: {e:?}"))?;
    match parsed {
        TeltonikaFrame::AVL(frame) => Ok(frame),
        TeltonikaFrame::GPRS(_) => Err(anyhow!(
            "Teltonika: received GPRS command-response frame on telemetry listener (ignored)"
        )),
    }
}

/// Convert a parsed AVL frame into a list of canonical `HhFrame`s, one per
/// AVL record. The codec is annotated on the raw payload string so logs can
/// distinguish Codec 8 from Codec 8 Extended for diagnostics.
pub fn avl_frame_to_hh_frames(frame: &AVLFrame) -> Vec<HhFrame> {
    let codec_label = codec_label(frame.codec);
    frame
        .records
        .iter()
        .map(|record| avl_record_to_hh_frame(record, codec_label))
        .collect()
}

// ─── Implementation ─────────────────────────────────────────────────────────

fn avl_record_to_hh_frame(record: &AVLRecord, codec_label: &'static str) -> HhFrame {
    let mut mems_x: i8 = 0;
    let mut mems_y: i8 = 0;
    let mut mems_z: i8 = 0;
    let mut power_voltage_byte: u8 = 0;
    let mut ignition_on: bool = record.speed > 0; // fallback before IO 239 fires
    let mut temperature_raw: u16 = 0;
    let mut fms_temperature_c: Option<i16> = None;
    let mut odometer_km: u32 = 0;
    let mut rpm: Option<u16> = None;
    let mut signal_quality: Option<u8> = None;
    let mut crash_detected = false;
    let mut digital_input_1: bool = false;
    let mut digital_input_2: bool = false;

    for io in &record.io_events {
        match io.id {
            IO_DIGITAL_INPUT_1 => digital_input_1 = io_as_u32(&io.value) != 0,
            IO_DIGITAL_INPUT_2 => digital_input_2 = io_as_u32(&io.value) != 0,
            IO_AXIS_X => mems_x = mg_to_int8(io_as_signed(&io.value)),
            IO_AXIS_Y => mems_y = mg_to_int8(io_as_signed(&io.value)),
            IO_AXIS_Z => mems_z = mg_to_int8(io_as_signed(&io.value)),
            IO_IGNITION => ignition_on = io_as_u32(&io.value) == 1,
            IO_EXTERNAL_VOLTAGE_MV => {
                // External voltage in millivolts. Our canonical column stores
                // an 8-bit volt count (matching the legacy NEMS encoding where
                // 1 unit ≈ 0.1V — see how db.rs already interprets it).
                power_voltage_byte = ((io_as_u32(&io.value) / 100).min(255)) as u8;
            }
            IO_TEMP_SENSOR_1 | IO_TEMP_SENSOR_2 => {
                // Dallas / BLE temperature sensor reading in 0.1 °C units.
                // We persist the raw value for the FMS pipeline AND a
                // pre-scaled signed value so the monitoring map can render
                // it without re-decoding the unit.
                let raw = io_as_signed(&io.value);
                if temperature_raw == 0 {
                    temperature_raw = (raw.unsigned_abs() & 0xFFFF) as u16;
                    fms_temperature_c = Some((raw / 10).clamp(i16::MIN as i32, i16::MAX as i32) as i16);
                }
            }
            IO_TOTAL_ODOMETER_M => odometer_km = io_as_u32(&io.value) / 1000,
            IO_RPM => rpm = Some(io_as_u32(&io.value).min(65_535) as u16),
            IO_GSM_SIGNAL => signal_quality = Some(io_as_u32(&io.value).min(255) as u8),
            IO_CRASH_DETECTION | IO_CRASH_TRACE => {
                if io_as_u32(&io.value) > 0 {
                    crash_detected = true;
                }
            }
            IO_MOVEMENT | IO_SPEED_KPH | IO_GREEN_DRIVING_TYPE | IO_BATTERY_VOLTAGE_MV => {
                // These IOs are present but not currently consumed by the
                // canonical pipeline — silently accepted.
            }
            other => {
                debug!(io_id = other, "Teltonika IO ignored (no canonical mapping yet)");
            }
        }
    }

    // ── send_flag heuristic ────────────────────────────────────────────────
    //
    // The downstream pipeline (and the .NET monitoring/accident-detection
    // services) reads `send_flag` to know WHY this frame was emitted.
    // Teltonika encodes the same information via `priority` + an explicit
    // crash IO. Translate to the NEMS-compatible send_flag enum:
    //   0  CMDUSER  | 1 SENDP    | 2 GPSVAL  | 3 CAPDEV  | 4 IOCHANGE
    //   5  OVERSPEED| 6 JERCK    | 7 IBUTTON | 11 ALERT
    let send_flag: u8 = if crash_detected {
        6 // JERCK = harsh / accident event — triggers accident-detection short-circuit
    } else {
        match record.priority {
            Priority::Panic => 11,    // ALERT
            Priority::High => 4,      // IOCHANGE (priority lift on IO event)
            Priority::Low => 1,       // SENDP (periodic timer)
        }
    };

    let recorded_at = record.timestamp.naive_utc();

    let is_valid = record.satellites >= 3
        && (record.latitude != 0.0 || record.longitude != 0.0)
        && record.latitude.abs() <= 90.0
        && record.longitude.abs() <= 180.0;

    // Use V3 (= has_fms) when we have at least one FMS-derived field so the
    // .NET pipeline stores rpm/odometer/temperature in their dedicated columns.
    let has_fms = rpm.is_some() || odometer_km > 0 || fms_temperature_c.is_some();
    let version = if has_fms { FrameVersion::V3 } else { FrameVersion::V1 };

    // Compact diagnostic payload — readable in `kubectl logs`.
    let raw_payload = format!(
        "TELTONIKA:{} prio={:?} lat={:.6} lon={:.6} spd={} ign={} mems={}/{}/{} crash={} ios={}",
        codec_label,
        record.priority,
        record.latitude,
        record.longitude,
        record.speed,
        ignition_on,
        mems_x, mems_y, mems_z,
        crash_detected,
        record.io_events.len(),
    );

    if crash_detected {
        info!(
            codec = codec_label,
            mems_x, mems_y, mems_z,
            speed = record.speed,
            "Teltonika CRASH EVENT received — flagged for downstream accident detection"
        );
    }

    HhFrame {
        kind: FrameKind::RealTime,
        version,
        recorded_at,
        latitude: record.latitude,
        longitude: record.longitude,
        speed_kph: record.speed as f64,
        heading_deg: (record.angle as f64).clamp(0.0, 360.0),
        power_voltage: power_voltage_byte,
        power_source_rescue: false,
        fuel_raw: 0, // FMB does not report fuel level in a canonical IO — would need a Dallas / 1-Wire sensor mapping per fleet
        ignition_on,
        mems_x,
        mems_y,
        mems_z,
        temperature_raw,
        odometer_km,
        send_flag,
        added_info: record.trigger_event_id as u32,
        signal_quality,
        satellites_in_view: Some(record.satellites),
        rpm,
        fuel_rate_l_per_100km: None,
        fms_temperature_c,
        is_valid,
        is_real_time: true,
        // Pack ignition + digital inputs into a single byte so the .NET side
        // can show GPIO state without an extra column. Bit 0 = ignition,
        // bit 1 = digital input 1, bit 2 = digital input 2.
        flags_raw: (ignition_on as u8)
            | ((digital_input_1 as u8) << 1)
            | ((digital_input_2 as u8) << 2),
        raw_payload,
        remaining_payload: None,
        address: None,
    }
}

// ─── Value helpers ──────────────────────────────────────────────────────────

/// Extracts an unsigned 32-bit value from an IO payload regardless of its
/// declared width (Teltonika encodes the same field as u8/u16/u32/u64
/// depending on the codec sub-variant). For values that can be negative
/// (axis, temperature), use `io_as_signed` instead.
fn io_as_u32(v: &AVLEventIOValue) -> u32 {
    match v {
        AVLEventIOValue::U8(x) => *x as u32,
        AVLEventIOValue::U16(x) => *x as u32,
        AVLEventIOValue::U32(x) => *x,
        AVLEventIOValue::U64(x) => *x as u32, // We don't consume any 64-bit IDs today
        AVLEventIOValue::Variable(_) => 0,
    }
}

/// Reinterprets an IO payload as a signed integer. Teltonika packs negative
/// values (axis acceleration, temperature) as two's complement of the
/// natural width; the parser hands us the raw bits as an unsigned variant
/// so we cast back to the signed equivalent here.
fn io_as_signed(v: &AVLEventIOValue) -> i32 {
    match v {
        AVLEventIOValue::U8(x) => *x as i8 as i32,
        AVLEventIOValue::U16(x) => *x as i16 as i32,
        AVLEventIOValue::U32(x) => *x as i32,
        AVLEventIOValue::U64(x) => *x as i64 as i32,
        AVLEventIOValue::Variable(_) => 0,
    }
}

/// Convert Teltonika milli-g to the int8 axis scale shared with NEMS.
/// NEMS saturates at ±127 ≈ ±2g, so 1g = 1000 mg = 63 in our scale. We
/// divide by 16 (≈ 1g → 62) which keeps the V7 accident detector's
/// `>= 100` magnitude threshold (≈ 1.6 g) consistent across both families.
fn mg_to_int8(mg: i32) -> i8 {
    (mg / 16).clamp(-128, 127) as i8
}

/// Pretty-printable codec label for diagnostics logs. The strings are
/// `&'static str` so we can carry them into `tracing::info!` without
/// extra allocations.
fn codec_label(codec: Codec) -> &'static str {
    match codec {
        Codec::C8 => "Codec8",
        Codec::C8Ext => "Codec8E",
        Codec::C16 => "Codec16",
        Codec::C12 => "Codec12",
        Codec::C13 => "Codec13",
        Codec::C14 => "Codec14",
    }
}

// ─── Build helpers used by the TCP handler ──────────────────────────────────

/// Build the 4-byte ACK the server returns after a successful AVL frame.
/// Teltonika expects the number of records the server has accepted, encoded
/// big-endian on 4 bytes. Returning the same count the device sent us is
/// the only way for it to consider the data delivered.
pub fn build_avl_ack(record_count: u32) -> [u8; 4] {
    record_count.to_be_bytes()
}

/// Build the 1-byte IMEI handshake ACK. `accept = true` returns `0x01`
/// (device may start streaming AVL frames), `accept = false` returns
/// `0x00` (device will close the TCP connection).
pub fn build_imei_ack(accept: bool) -> [u8; 1] {
    [if accept { 0x01 } else { 0x00 }]
}

// ─── Tests ──────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;

    /// Real-world IMEI handshake (15 ASCII digits prefixed by their length on
    /// 2 BE bytes). Anything else MUST fail to parse so we don't silently
    /// accept a random TCP client as an FMB device.
    #[test]
    fn parses_real_imei_handshake() {
        let mut packet = vec![0x00, 0x0F]; // length = 15
        packet.extend_from_slice(b"352093088123456");
        let imei = parse_imei(&packet).unwrap();
        assert_eq!(imei, "352093088123456");
    }

    #[test]
    fn rejects_garbage_imei() {
        let garbage = [0xDE, 0xAD, 0xBE, 0xEF];
        assert!(parse_imei(&garbage).is_err());
    }

    #[test]
    fn mg_to_int8_saturates_at_2g() {
        assert_eq!(mg_to_int8(0), 0);
        assert_eq!(mg_to_int8(1000), 62);   // 1g ≈ 62
        assert_eq!(mg_to_int8(-1000), -62);
        assert_eq!(mg_to_int8(2000), 125);  // ~2g
        assert_eq!(mg_to_int8(5000), 127);  // saturated
        assert_eq!(mg_to_int8(-5000), -128);
    }

    #[test]
    fn avl_ack_is_be_record_count() {
        assert_eq!(build_avl_ack(1), [0, 0, 0, 1]);
        assert_eq!(build_avl_ack(50), [0, 0, 0, 50]);
        assert_eq!(build_avl_ack(256), [0, 0, 1, 0]);
    }

    #[test]
    fn imei_ack_is_single_byte() {
        assert_eq!(build_imei_ack(true), [0x01]);
        assert_eq!(build_imei_ack(false), [0x00]);
    }
}
