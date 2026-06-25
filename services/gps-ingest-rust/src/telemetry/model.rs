use chrono::NaiveDateTime;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FrameKind {
    RealTimeAndHistory,
    History,
    RealTime,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FrameVersion {
    V1,
    V3,
    Unknown(u8),
}

#[derive(Debug, Clone)]
pub struct HhFrame {
    pub kind: FrameKind,
    pub version: FrameVersion,
    pub recorded_at: NaiveDateTime,
    pub latitude: f64,
    pub longitude: f64,
    pub speed_kph: f64,
    pub heading_deg: f64,
    pub power_voltage: u8,
    pub power_source_rescue: bool,
    pub fuel_raw: u8,
    pub ignition_on: bool,
    pub mems_x: i8,
    pub mems_y: i8,
    pub mems_z: i8,
    pub temperature_raw: u16,
    pub odometer_km: u32,
    pub send_flag: u8,
    pub added_info: u32,
    pub signal_quality: Option<u8>,
    pub satellites_in_view: Option<u8>,
    pub rpm: Option<u16>,
    pub fuel_rate_l_per_100km: Option<f64>,
    pub fms_temperature_c: Option<i16>,
    pub is_valid: bool,
    pub is_real_time: bool,
    pub flags_raw: u8,
    pub raw_payload: String,
    pub remaining_payload: Option<String>,
    pub address: Option<String>,
}

#[derive(Debug, Clone)]
pub struct HhInfoFrame {
    pub firmware_version: String,
    pub icc_id: Option<String>,
    pub imei: Option<String>,
    pub mat: Option<String>, // MAT (GPS logical identifier, distinct from vehicle plate_number)
}

/// Metadata inferred from the TCP listener protocol.
/// Used to automatically tag devices with the correct GPS model / firmware flavor.
#[derive(Debug, Clone, Copy, Default)]
pub struct ProtocolMetadata {
    pub model_name: Option<&'static str>,
    pub firmware_flavor: Option<&'static str>,
}

impl ProtocolMetadata {
    pub fn from_protocol(protocol: &str) -> Self {
        match protocol {
            // gps_type_1 => NEMS model, legacy "L" firmware
            "gps_type_1" => Self {
                model_name: Some("NEMS"),
                firmware_flavor: Some("L"),
            },
            // gps_type_2 => NEMS model, "S" firmware
            "gps_type_2" => Self {
                model_name: Some("NEMS"),
                firmware_flavor: Some("S"),
            },
            // noron => Noron NR024 binary protocol tracker
            "noron" => Self {
                model_name: Some("NORON"),
                firmware_flavor: Some("NR024"),
            },
            // teltonika => Teltonika family — FMB120, FMB130, FMB150, FMC130,
            // FMU130, FMM130 all speak the same Codec 8 / 8 Extended on TCP. The
            // exact model is stamped per-device in gps_devices.model by the
            // operator; this metadata only tags the firmware family.
            "teltonika" => Self {
                model_name: Some("TELTONIKA"),
                firmware_flavor: Some("Codec8"),
            },
            // gt06 => Concox GT06 binary protocol — used by GT06N, TR06,
            // JT701, JM-VL01, JI09, JI10 and the Coban-branded GT06 clones.
            // Operators can refine the exact model in gps_devices later.
            "gt06" => Self {
                model_name: Some("GT06"),
                firmware_flavor: Some("Concox"),
            },
            // coban => Coban TK103 / TK303 family (and TK104, TK06, GT02 —
            // all share the same ASCII *HQ / imei: framing).
            "coban" => Self {
                model_name: Some("COBAN"),
                firmware_flavor: Some("TK103"),
            },
            // Future types can be added here as soon as new GPS models are supported
            _ => Self::default(),
        }
    }
}
