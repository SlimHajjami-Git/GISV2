use std::{collections::{HashMap, VecDeque}, sync::Arc};

use anyhow::{anyhow, Context, Result};
use chrono::{DateTime, Duration, Utc};
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    net::{TcpListener, TcpStream, UdpSocket},
    signal,
    sync::{mpsc, Mutex},
};
use tracing::{debug, error, info, warn};

use crate::{
    config::{AppConfig, ListenerConfig, TransportKind},
    ports::{TelemetryEventPublisher, TelemetryStore},
    redis_cache::RedisCache,
    services::{
        driving_events::DrivingEventsDetector,
        fuel_tracker::FuelTracker,
        geofence_detector::GeofenceDetector,
        geocoding::GeocodingService,
        gps_stabilizer::GpsStabilizer,
        gps_validator::GpsValidator,
        speed_filter::SpeedFilter,
        stop_detector::StopDetector,
        trip_detector::TripDetector,
    },
    telemetry,
};

type ConnectionMap = Arc<Mutex<HashMap<String, String>>>;

/// Device command senders: maps device_id → mpsc sender for instant push from .NET.
/// When an external source (.NET controller) inserts a row in `device_commands`,
/// it hits `POST /commands/push` on this service, which looks up the sender for the
/// target device_id and pushes the command text into the TCP connection handler task.
/// The handler's `tokio::select!` receives the message and writes it to the socket
/// with sub-100ms latency (vs. 5s-5min when waiting for the next device frame).
/// The per-frame DB polling remains as a fallback for commands inserted while the
/// device is offline or while this service is restarting.
pub type CommandSenders = Arc<Mutex<HashMap<i32, mpsc::Sender<String>>>>;

/// Cooldown period: once a device event is recorded, ignore further events for 24h
const DEVICE_EVENT_COOLDOWN_SECS: i64 = 24 * 3600;

/// Shared services for stop detection, fuel tracking, geocoding, geofencing, GPS stabilization, validation, trip detection, and driving events
pub struct TelemetryServices {
    pub stop_detector: StopDetector,
    pub fuel_tracker: FuelTracker,
    pub geocoding: GeocodingService,
    pub geofence_detector: GeofenceDetector,
    pub gps_stabilizer: GpsStabilizer,
    pub gps_validator: GpsValidator,
    pub trip_detector: TripDetector,
    pub driving_events_detector: DrivingEventsDetector,
    pub speed_filter: SpeedFilter,
    /// Tracks last device event timestamp per device_id to prevent duplicates
    pub device_event_cooldown: Mutex<HashMap<i32, DateTime<Utc>>>,
}

pub async fn run_listeners(
    config: &AppConfig,
    database: Arc<dyn TelemetryStore>,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    redis_cache: Option<Arc<RedisCache>>,
    command_senders: CommandSenders,
) -> Result<()> {
    if config.listeners.is_empty() {
        info!("No listeners configured; nothing to start");
        return Ok(());
    }

    // Create shared services for stop detection, fuel tracking, geocoding, and geofencing
    let nominatim_url = std::env::var("NOMINATIM_URL").ok();
    let geofence_detector = GeofenceDetector::new();
    
    // Load initial geofences
    match database.load_geofences().await {
        Ok(geofences) => {
            geofence_detector.load_geofences(geofences).await;
        }
        Err(e) => {
            warn!(?e, "Failed to load initial geofences");
        }
    }
    
    let services = Arc::new(TelemetryServices {
        stop_detector: StopDetector::new(),
        fuel_tracker: FuelTracker::new(),
        geocoding: GeocodingService::new(nominatim_url),
        geofence_detector,
        gps_stabilizer: GpsStabilizer::new(),
        gps_validator: GpsValidator::new(),
        trip_detector: TripDetector::new(),
        driving_events_detector: DrivingEventsDetector::new(),
        speed_filter: SpeedFilter::new(),
        device_event_cooldown: Mutex::new(HashMap::new()),
    });
    info!("Telemetry services initialized (StopDetector, FuelTracker, Geocoding, GeofenceDetector, GpsStabilizer, GpsValidator, TripDetector, DrivingEventsDetector, SpeedFilter)");

    let mut handles = Vec::new();
    for listener in &config.listeners {
        match listener.transport {
            TransportKind::Tcp => {
                let cfg = listener.clone();
                let db = Arc::clone(&database);
                let mapping: ConnectionMap = Arc::new(Mutex::new(HashMap::new()));
                let publisher_clone = publisher.clone();
                let services_clone = Arc::clone(&services);
                let redis_clone = redis_cache.clone();
                let cmd_senders_clone = Arc::clone(&command_senders);
                handles.push(tokio::spawn(async move {
                    if let Err(err) = run_tcp_listener(cfg, db, mapping, publisher_clone, services_clone, redis_clone, cmd_senders_clone).await {
                        error!(?err, "TCP listener terminated unexpectedly");
                    }
                }));
            }
            TransportKind::Udp => {
                let cfg = listener.clone();
                let db = Arc::clone(&database);
                let publisher_clone = publisher.clone();
                let services_clone = Arc::clone(&services);
                let redis_clone = redis_cache.clone();
                handles.push(tokio::spawn(async move {
                    if let Err(err) = run_udp_listener(cfg, db, publisher_clone, services_clone, redis_clone).await {
                        error!(?err, "UDP listener terminated unexpectedly");
                    }
                }));
            }
        }
    }

    info!(count = handles.len(), "Listeners running; awaiting shutdown signal");
    signal::ctrl_c().await?;
    info!("Shutdown signal received; stopping listeners");

    for handle in handles {
        handle.abort();
    }

    Ok(())
}

async fn run_tcp_listener(
    cfg: ListenerConfig,
    database: Arc<dyn TelemetryStore>,
    connection_map: ConnectionMap,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
    command_senders: CommandSenders,
) -> Result<()> {
    let bind_addr = format!("0.0.0.0:{}", cfg.port);
    let listener = TcpListener::bind(&bind_addr).await?;
    info!(port = cfg.port, protocol = %cfg.protocol, "TCP listener started");

    let cfg = Arc::new(cfg);
    loop {
        let (stream, peer_addr) = listener.accept().await?;
        let cfg_clone = Arc::clone(&cfg);
        let db = Arc::clone(&database);
        let map_clone = Arc::clone(&connection_map);
        let publisher_clone = publisher.clone();
        let services_clone = Arc::clone(&services);
        let redis_clone = redis_cache.clone();
        let cmd_senders_clone = Arc::clone(&command_senders);
        tokio::spawn(async move {
            // Stateful binary / ASCII protocols dispatch to dedicated handlers
            // rather than squeezing their logic into the generic NEMS handler.
            let result = match cfg_clone.protocol.as_str() {
                "teltonika" => handle_teltonika_connection(
                    stream, cfg_clone, db, publisher_clone, services_clone, redis_clone,
                ).await,
                "gt06" => handle_gt06_connection(
                    stream, cfg_clone, db, publisher_clone, services_clone, redis_clone,
                ).await,
                "coban" => handle_coban_connection(
                    stream, cfg_clone, db, publisher_clone, services_clone, redis_clone,
                ).await,
                _ => handle_tcp_connection(
                    stream, cfg_clone, db, map_clone, publisher_clone, services_clone,
                    redis_clone, cmd_senders_clone,
                ).await,
            };
            if let Err(err) = result {
                error!(?err, "TCP connection handler exited with error");
            }
        });
        info!(protocol = %cfg.protocol, peer = %peer_addr, "Accepted TCP connection");
    }
}

async fn run_udp_listener(
    cfg: ListenerConfig,
    database: Arc<dyn TelemetryStore>,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
) -> Result<()> {
    let bind_addr = format!("0.0.0.0:{}", cfg.port);
    let socket = UdpSocket::bind(&bind_addr).await?;
    info!(port = cfg.port, protocol = %cfg.protocol, "UDP listener started");

    // Track device IDs by source address (like connection_map for TCP)
    let peer_device_map: Arc<Mutex<HashMap<String, String>>> = Arc::new(Mutex::new(HashMap::new()));

    let mut buf = vec![0u8; 4096];
    loop {
        let (len, src_addr) = socket.recv_from(&mut buf).await?;
        let payload = buf[..len].to_vec();
        let hex_dump = hex::encode(&payload);
        let peer_str = src_addr.to_string();

        info!(
            protocol = %cfg.protocol,
            port = cfg.port,
            size = len,
            peer = %peer_str,
            payload = %hex_dump,
            "Received UDP payload"
        );

        // Only Noron protocol is supported via UDP for now
        if cfg.protocol == "noron" {
            let results = telemetry::noron::decode_buffer(&payload);
            for result in results {
                match result {
                    Ok(telemetry::noron::NoronDecodeResult::Handshake { device_id }) => {
                        // Store device ID in peer map
                        {
                            let mut map = peer_device_map.lock().await;
                            map.insert(peer_str.clone(), device_id.clone());
                        }
                        // Send handshake ACK back via UDP
                        if let Err(e) = socket.send_to(&telemetry::noron::HANDSHAKE_ACK, src_addr).await {
                            warn!(?e, "Failed to send Noron UDP handshake ACK");
                        } else {
                            info!(device_id = %device_id, peer = %peer_str, "Sent Noron UDP handshake ACK");
                        }
                    }
                    Ok(telemetry::noron::NoronDecodeResult::Position { frame, device_id }) => {
                        // Resolve device UID: prefer peer_device_map, fallback to packet device_id
                        let resolved_uid = {
                            let map = peer_device_map.lock().await;
                            map.get(&peer_str).cloned().unwrap_or_else(|| device_id.clone())
                        };

                        if let Err(err) = route_noron_frame(
                            &resolved_uid,
                            frame,
                            Arc::clone(&database),
                            publisher.clone(),
                            Arc::clone(&services),
                            redis_cache.clone(),
                        )
                        .await
                        {
                            warn!(?err, device_id = %resolved_uid, "Failed to process Noron UDP position");
                        }
                    }
                    Ok(telemetry::noron::NoronDecodeResult::Unknown { flag }) => {
                        warn!(flag, peer = %peer_str, "Unknown Noron UDP packet flag, skipping");
                    }
                    Err(e) => {
                        warn!(error = %e, peer = %peer_str, "Failed to decode Noron UDP packet");
                    }
                }
            }
        } else {
            warn!(protocol = %cfg.protocol, "UDP not supported for this protocol");
        }
    }
}

async fn handle_tcp_connection(
    stream: TcpStream,
    cfg: Arc<ListenerConfig>,
    database: Arc<dyn TelemetryStore>,
    connection_map: ConnectionMap,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
    command_senders: CommandSenders,
) -> Result<()> {
    let peer = stream.peer_addr().ok().map(|addr| addr.to_string());

    // Split the TCP stream so the frame-read loop and the external command-push
    // channel can coexist on the same connection without borrow-checker conflicts.
    // The `writer` half is shared via Arc<Mutex<_>> between the select! loop (for
    // ACKs, auto-recovery, DB-polled commands) and the external push path.
    let (mut reader, write_half) = tokio::io::split(stream);
    let writer = Arc::new(Mutex::new(write_half));

    // Channel for instant push: POST /commands/push (in main.rs) looks up this
    // device_id in the CommandSenders map and sends the command text here.
    // Bound at 8 to drop excess if operators spam clicks; realistically there's
    // at most one command queued at a time.
    let (cmd_tx, mut cmd_rx) = mpsc::channel::<String>(8);
    let mut registered_device_id: Option<i32> = None;

    let mut buffer = vec![0u8; 8192];

    // Rate limit auto-recovery: track last AJ+GO send time per device_id
    // to avoid spamming the same device every frame when bit5 stays 0
    let mut last_auto_recovery: HashMap<i32, std::time::Instant> = HashMap::new();
    const AUTO_RECOVERY_COOLDOWN_SECS: u64 = 300; // 5 minutes

    // Ring buffer: keep last 10 position frames for context logging when bit5=0 is detected
    let mut frame_history: VecDeque<String> = VecDeque::with_capacity(12);
    // Counter: after bit5=0 detection, log the next N frames as "after" context
    let mut log_after_bit5: u8 = 0;

    // Noron protocol uses binary framing — handle separately from ASCII protocols
    let is_noron = cfg.protocol == "noron";

    loop {
        tokio::select! {
            // Prefer pushed commands for low latency on remote stop/go
            biased;

            // ── BRANCH 1: external push from POST /commands/push ──
            maybe_cmd = cmd_rx.recv() => {
                let peer_str = peer.as_deref().unwrap_or("unknown");
                match maybe_cmd {
                    Some(cmd) => {
                        info!(peer = peer_str, cmd = %cmd.trim(), "Received pushed command via mpsc channel");
                        let mut w = writer.lock().await;
                        match w.write_all(cmd.as_bytes()).await {
                            Ok(_) => info!(peer = peer_str, cmd = %cmd.trim(), "Pushed command written to socket (instant path)"),
                            Err(e) => {
                                error!(?e, peer = peer_str, "Failed to write pushed command to socket");
                                break;
                            }
                        }
                        // Skip the frame-processing body; loop again to read next
                        continue;
                    }
                    None => {
                        // All senders dropped — handler should exit gracefully
                        debug!(peer = peer_str, "Command channel closed; ending connection handler");
                        break;
                    }
                }
            }

            // ── BRANCH 2: device sent us something ──
            read_res = reader.read(&mut buffer) => {
                let read = read_res?;
                if read == 0 {
                    break;
                }

        let payload = &buffer[..read];
        let hex_dump = hex::encode(payload);
        info!(protocol = %cfg.protocol, port = cfg.port, size = read, payload = %hex_dump, "Received raw payload");

        if is_noron {
            // ── Noron NR024 binary protocol ──
            // Decode all packets in buffer (may contain multiple)
            let results = telemetry::noron::decode_buffer(payload);
            for result in results {
                match result {
                    Ok(telemetry::noron::NoronDecodeResult::Handshake { device_id }) => {
                        // Store device ID in connection map
                        if let Some(ref peer_str) = peer {
                            let mut map = connection_map.lock().await;
                            map.insert(peer_str.clone(), device_id.clone());
                        }
                        // Send handshake ACK (same as GISV1 NR024TrackerShakeHandResp.ToBuffer())
                        if let Err(e) = writer.lock().await.write_all(&telemetry::noron::HANDSHAKE_ACK).await {
                            warn!(?e, "Failed to send Noron handshake ACK");
                        } else {
                            info!(device_id = %device_id, "Sent Noron handshake ACK (13 bytes)");
                        }
                    }
                    Ok(telemetry::noron::NoronDecodeResult::Position { frame, device_id }) => {
                        // Resolve device UID: prefer connection_map, fallback to packet device_id
                        let resolved_uid = if let Some(ref peer_str) = peer {
                            let map = connection_map.lock().await;
                            map.get(peer_str).cloned().unwrap_or_else(|| device_id.clone())
                        } else {
                            device_id.clone()
                        };

                        // Route through the shared pipeline (same as NEMS frames)
                        if let Err(err) = route_noron_frame(
                            &resolved_uid,
                            frame,
                            Arc::clone(&database),
                            publisher.clone(),
                            Arc::clone(&services),
                            redis_cache.clone(),
                        )
                        .await
                        {
                            warn!(?err, device_id = %resolved_uid, "Failed to process Noron position");
                        }
                    }
                    Ok(telemetry::noron::NoronDecodeResult::Unknown { flag }) => {
                        warn!(flag, "Unknown Noron packet flag, skipping");
                    }
                    Err(e) => {
                        warn!(error = %e, "Failed to decode Noron packet");
                    }
                }
            }
        } else {
            // ── ASCII protocols (NEMS HH/AA) ──
            // ACK mechanism for AAP/ACI protocol (same as GISV1)
            // When tracker sends "AAAA", respond with "AA06" to keep short interval (1 min)
            // Without ACK, tracker falls back to degraded mode (3 min interval)
            if let Ok(ascii) = std::str::from_utf8(payload) {
                if ascii.contains("AAAA") {
                    if let Err(e) = writer.lock().await.write_all(b"AA06").await {
                        warn!(?e, "Failed to send AA06 ACK to tracker");
                    } else {
                        info!(peer = peer.as_deref().unwrap_or("unknown"), "Sent AA06 ACK to tracker (keepalive)");
                    }
                }

                // ── Ensure IMEI is in connection_map (needed for auto-recovery + pending commands) ──
                // After pod restart, connection_map is empty. Resolve via MAT prefix
                // so that pending commands can be sent even on the first frame.
                let check_frames = extract_frames_smart(ascii);
                if let Some(ref p) = peer {
                    let has_imei = {
                        let map = connection_map.lock().await;
                        map.contains_key(p)
                    };
                    if !has_imei {
                        for frame_str in &check_frames {
                            let (mat_prefix, _) = extract_mat_prefix(frame_str.trim());
                            if let Some(ref mat) = mat_prefix {
                                if let Ok(Some(uid)) = database.get_device_uid_by_mat(mat).await {
                                    info!(peer = p.as_str(), mat = %mat, imei = %uid, "Resolved IMEI via MAT prefix (connection_map was empty)");
                                    let mut map = connection_map.lock().await;
                                    map.insert(p.clone(), uid);
                                    break;
                                }
                            }
                        }
                    }
                }

                // ── AJ+ immobilization auto-recovery (DB-driven) ──
                // Check POSITION frames for bit5=0 (AJ+STOP active).
                // Before sending AJ+GO, check database: if immobilization_requested=true,
                // the stop was intentional by an operator → do NOT send AJ+GO.
                // Commands and passwords come from DB — nothing is hardcoded.
                for frame_str in &check_frames {
                    let trimmed = frame_str.trim();
                    let peer_str = peer.as_deref().unwrap_or("unknown");

                    // ── Extract MAT prefix for logging ──
                    let (frame_mat, _) = extract_mat_prefix(trimmed);
                    let frame_after_mat = if let Some(ref m) = frame_mat {
                        trimmed.get(m.len()..).map(|s| s.trim_start()).unwrap_or(trimmed)
                    } else {
                        trimmed
                    };

                    // ── Log non-standard frames (AA00, AA03, AAAA, etc.) ──
                    if frame_after_mat.starts_with("AA0") || frame_after_mat.starts_with("AA4")
                        || frame_after_mat.starts_with("AA5") || frame_after_mat.starts_with("AA6")
                        || frame_after_mat.starts_with("AA7") || frame_after_mat.starts_with("AA8")
                        || frame_after_mat.starts_with("AA9") || frame_after_mat.starts_with("AAAA") {
                        let ft = frame_after_mat.get(..4).unwrap_or("????");
                        let imei_opt = if let Some(ref p) = peer {
                            let map = connection_map.lock().await;
                            map.get(p).cloned()
                        } else { None };
                        let _ = database.log_frame_debug(
                            None,
                            imei_opt.as_deref(),
                            frame_mat.as_deref(),
                            ft,
                            None, None,
                            &trimmed[..std::cmp::min(trimmed.len(), 200)],
                            "non_standard_frame",
                            Some(peer_str),
                        ).await;
                    }

                    // ── Check position frames for immobilization (bit5=0) ──
                    let header_pos = if trimmed.starts_with("AA1") || trimmed.starts_with("AA2") || trimmed.starts_with("AA3")
                        || trimmed.starts_with("HH1") || trimmed.starts_with("HH2") || trimmed.starts_with("HH3") {
                        Some(0)
                    } else if let Some(pos) = trimmed.find(" AA1").or_else(|| trimmed.find(" AA2")).or_else(|| trimmed.find(" AA3"))
                        .or_else(|| trimmed.find(" HH1")).or_else(|| trimmed.find(" HH2")).or_else(|| trimmed.find(" HH3")) {
                        Some(pos + 1)
                    } else {
                        None
                    };

                    if let Some(start) = header_pos {
                        let actual_frame = &trimmed[start..];
                        if actual_frame.len() >= 44 {
                            if let Some(flags_hex) = actual_frame.get(42..44) {
                                if let Ok(flags) = u8::from_str_radix(flags_hex, 16) {
                                    let bit5_on = (flags & 0x20) != 0;
                                    let gps_valid = (flags & 0x40) != 0;
                                    let frame_preview = &actual_frame[..std::cmp::min(actual_frame.len(), 200)];

                                    // ── Buffer every position frame for context logging ──
                                    frame_history.push_back(format!(
                                        "flags=0x{:02X} bit5={} gps={} | {}",
                                        flags, if bit5_on { "1" } else { "0" },
                                        if gps_valid { "OK" } else { "NO" },
                                        frame_preview
                                    ));
                                    if frame_history.len() > 10 {
                                        frame_history.pop_front();
                                    }

                                    // ── Log "after" context frames following a bit5=0 detection ──
                                    if log_after_bit5 > 0 {
                                        let after_imei = if let Some(ref p) = peer {
                                            let map = connection_map.lock().await;
                                            map.get(p).cloned()
                                        } else { None };
                                        info!(
                                            imei = ?after_imei,
                                            flags = format!("0x{:02X}", flags),
                                            bit5 = bit5_on,
                                            remaining = log_after_bit5,
                                            frame = %frame_preview,
                                            "🔍 Frame AFTER bit5=0 detection (context)"
                                        );
                                        log_after_bit5 -= 1;
                                    }

                                    // Only check bit5 if GPS is valid (bit6=1).
                                    // flags=0x00 means corrupted/empty data, NOT real immobilization.
                                    if gps_valid && !bit5_on {
                                        // ── Resolve device for immobilization check ──
                                        let imei_opt = if let Some(ref p) = peer {
                                            let map = connection_map.lock().await;
                                            map.get(p).cloned()
                                        } else {
                                            None
                                        };

                                        let device_id_opt = if let Some(ref imei) = imei_opt {
                                            database.get_device_id(imei).await.ok().flatten()
                                        } else {
                                            None
                                        };

                                        if let Some(ref imei) = imei_opt {
                                            if let Some(device_id) = device_id_opt {
                                                // Rate limit: skip if we already processed this device recently
                                                let now = std::time::Instant::now();
                                                let cooldown_elapsed = match last_auto_recovery.get(&device_id) {
                                                    Some(last) => now.duration_since(*last).as_secs() >= AUTO_RECOVERY_COOLDOWN_SECS,
                                                    None => true, // Never sent before
                                                };

                                                if !cooldown_elapsed {
                                                    // Skip — already processed recently (don't log every frame)
                                                    break;
                                                }

                                                // ── Dump frame history (last ~10 frames BEFORE detection) ──
                                                info!(
                                                    imei = %imei,
                                                    device_id,
                                                    history_count = frame_history.len(),
                                                    "🔍 bit5=0 DETECTED — dumping last {} frames as context",
                                                    frame_history.len()
                                                );
                                                for (i, hist) in frame_history.iter().enumerate() {
                                                    info!(
                                                        imei = %imei,
                                                        device_id,
                                                        index = i + 1,
                                                        total = frame_history.len(),
                                                        frame = %hist,
                                                        "🔍 BEFORE bit5=0 [{}/{}]",
                                                        i + 1, frame_history.len()
                                                    );
                                                }
                                                // Start logging next 5 frames as "after" context
                                                log_after_bit5 = 5;

                                                // Log once per cooldown period (not every frame)
                                                let _ = database.log_frame_debug(
                                                    device_id_opt,
                                                    imei_opt.as_deref(),
                                                    frame_mat.as_deref(),
                                                    actual_frame.get(..4).unwrap_or("????"),
                                                    Some(flags_hex),
                                                    Some(flags as i16),
                                                    &actual_frame[..std::cmp::min(actual_frame.len(), 200)],
                                                    "bit5_immobilization_detected",
                                                    Some(peer_str),
                                                ).await;

                                                // Check DB: was this stop requested by an operator?
                                                match database.get_immobilization_state(device_id).await {
                                                    Ok((true, _)) => {
                                                        // Stop was requested by operator → do nothing
                                                        info!(
                                                            peer = peer_str,
                                                            imei = %imei,
                                                            flags = format!("0x{:02X}", flags),
                                                            "IMMOBILIZATION DETECTED but REQUESTED by operator - NOT sending AJ+GO"
                                                        );
                                                    }
                                                    Ok((false, command_go)) => {
                                                        // Unwanted stop → send command_go from DB
                                                        warn!(
                                                            peer = peer_str,
                                                            imei = %imei,
                                                            device_id,
                                                            flags = format!("0x{:02X}", flags),
                                                            command = %command_go.trim(),
                                                            "IMMOBILIZATION DETECTED (unwanted) - sending auto-recovery from DB"
                                                        );
                                                        if let Err(e) = writer.lock().await.write_all(command_go.as_bytes()).await {
                                                            error!(?e, peer = peer_str, "Failed to send auto-recovery command");
                                                        } else {
                                                            info!(peer = peer_str, imei = %imei, device_id, "Auto-recovery command SENT");
                                                            last_auto_recovery.insert(device_id, now);
                                                            let _ = database.log_auto_recovery(device_id, &command_go, flags_hex).await;
                                                        }
                                                    }
                                                    Err(e) => {
                                                        warn!(?e, "Failed to check immobilization state, skipping auto-recovery");
                                                    }
                                                }
                                            }
                                        } else {
                                            warn!(peer = peer_str, "IMMOBILIZATION DETECTED but no IMEI known yet - skipping");
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                // ── Send pending commands from DB (manual STOP/GO from operators) ──
                // This is the fallback path: catches commands inserted while the device
                // was offline or while this service was restarting. The instant path
                // (POST /commands/push → mpsc channel) is preferred when the device is
                // already connected and the sender is registered below.
                if let Some(ref p) = peer {
                    let imei_opt = {
                        let map = connection_map.lock().await;
                        map.get(p).cloned()
                    };
                    if let Some(ref imei) = imei_opt {
                        if let Ok(Some(device_id)) = database.get_device_id(imei).await {
                            // ── Register mpsc sender for instant command push ──
                            // Only done once per connection, the first time we successfully
                            // resolve the device_id. Dropped in the cleanup block on disconnect.
                            if registered_device_id != Some(device_id) {
                                command_senders.lock().await.insert(device_id, cmd_tx.clone());
                                registered_device_id = Some(device_id);
                                debug!(device_id, imei = %imei, "Registered command sender for instant push");
                            }

                            if let Ok(Some((cmd_id, cmd_text))) = database.get_pending_command(device_id).await {
                                let peer_str = peer.as_deref().unwrap_or("unknown");
                                info!(peer = peer_str, imei = %imei, cmd_id, cmd = %cmd_text, "Sending pending command from DB (fallback path)");
                                if let Err(e) = writer.lock().await.write_all(cmd_text.as_bytes()).await {
                                    error!(?e, "Failed to send pending command");
                                    let _ = database.update_command_status(cmd_id, "failed", Some(&e.to_string())).await;
                                } else {
                                    info!(peer = peer_str, imei = %imei, cmd_id, "Pending command SENT successfully");
                                    let _ = database.update_command_status(cmd_id, "sent", None).await;
                                }
                            }
                        }
                    }
                }
            }

            // ── Route payload for normal processing (DB, Redis, RabbitMQ) ──
            if let Err(err) = route_payload(
                &cfg.protocol,
                payload,
                Arc::clone(&database),
                peer.as_deref(),
                Arc::clone(&connection_map),
                publisher.clone(),
                Arc::clone(&services),
                redis_cache.clone(),
            )
            .await
            {
                warn!(?err, "Failed to process payload");
            }
        }

            } // end `read_res = reader.read(...)` branch
        } // end tokio::select!
    } // end outer loop

    // Connection closed — clean up registered channel + connection map
    if let Some(did) = registered_device_id {
        command_senders.lock().await.remove(&did);
        debug!(device_id = did, "Unregistered command sender on disconnect");
    }
    if let Some(ref peer_str) = peer {
        connection_map.lock().await.remove(peer_str);
    }

    Ok(())
}

async fn route_payload(
    protocol: &str,
    raw_payload: &[u8],
    database: Arc<dyn TelemetryStore>,
    peer_addr: Option<&str>,
    connection_map: ConnectionMap,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
) -> Result<()> {
    let ascii_payload = String::from_utf8(raw_payload.to_vec()).context("payload is not UTF-8")?;
    
    // Ignore keepalive payloads (just newlines/whitespace)
    let trimmed = ascii_payload.trim();
    if trimmed.is_empty() {
        tracing::debug!("Ignoring keepalive payload");
        return Ok(());
    }
    
    // Extract frames using smart parsing that handles binary data with embedded newlines
    let frames = extract_frames_smart(&ascii_payload);

    if frames.is_empty() {
        return Err(anyhow!("no valid HH/AA frames found in payload"));
    }

    info!(
        protocol, 
        frame_count = frames.len(), 
        payload_len = ascii_payload.len(),
        "Processing batch of frames"
    );

    // Log each received frame (preview only to avoid flooding logs)
    for (idx, frame_str) in frames.iter().enumerate() {
        let preview_len = std::cmp::min(64, frame_str.len());
        let preview = &frame_str[..preview_len];
        tracing::info!(
            protocol,
            frame_idx = idx,
            frame_len = frame_str.len(),
            preview = %preview,
            "Telemetry frame received"
        );
    }

    for frame_str in &frames {
        if let Err(err) = process_single_frame(
            protocol,
            frame_str,
            Arc::clone(&database),
            peer_addr,
            Arc::clone(&connection_map),
            publisher.clone(),
            Arc::clone(&services),
            redis_cache.clone(),
        )
        .await
        {
            // Log detailed error info to help diagnose rejected frames
            warn!(
                ?err,
                frame = %frame_str,
                frame_len = frame_str.len(),
                frame_header = &frame_str[..std::cmp::min(4, frame_str.len())],
                "Failed to process individual frame - FRAME LOST"
            );
        }
    }

    Ok(())
}

async fn process_single_frame(
    protocol: &str,
    frame_str: &str,
    database: Arc<dyn TelemetryStore>,
    peer_addr: Option<&str>,
    connection_map: ConnectionMap,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
) -> Result<()> {
    match protocol {
        "gps_type_1" => {
            // Process info/connect frames FIRST (AA00/AA01/HH00/HH01) so the connection map
            // has the IMEI before we process system frames (device sends AA00 then AA02 in same payload)
            let is_info_frame = frame_str.starts_with("HH01") || frame_str.starts_with("AA01") ||
                frame_str.starts_with("HH00") || frame_str.starts_with("AA00") ||
                frame_str.contains(" AA00") || frame_str.contains(" HH00");

            if is_info_frame {
                let info = telemetry::hh::parse_info_frame(frame_str)?;
                let imei = database.ingest_info_frame(protocol, &info).await?;
                if let Some(peer) = peer_addr {
                    let mut map = connection_map.lock().await;
                    map.insert(peer.to_string(), imei.clone());
                }
                info!(protocol, imei, "Registered device via info frame");
                return Ok(());
            }

            // Process system frames (AA02/HH02=restart, AA03/HH03=GSM reset)
            if telemetry::hh::is_system_frame(frame_str) {
                let is_restart = frame_str.starts_with("AA02") || frame_str.starts_with("HH02");
                let is_gsm_reset = frame_str.starts_with("AA03") || frame_str.starts_with("HH03");

                if is_gsm_reset {
                    // AA03/HH03 GSM reset is normal (modem reconnect), not a power cut — ignore
                    debug!(protocol, frame = %frame_str, "GSM reset frame — ignoring (not a power cut)");
                    return Ok(());
                }

                if !is_restart {
                    // AA07/HH07 time request — just log
                    info!(protocol, frame = %frame_str, "Received time request frame");
                    return Ok(());
                }

                // Resolve IMEI from connection map
                let resolved_uid = if let Some(peer) = peer_addr {
                    let map = connection_map.lock().await;
                    map.get(peer).cloned()
                } else {
                    None
                };

                let resolved_uid = match resolved_uid {
                    Some(uid) => uid,
                    None => {
                        warn!(protocol, frame = %frame_str, "System frame but no IMEI in connection map");
                        return Ok(());
                    }
                };

                // Look up device info
                let device_id = match database.get_device_id(&resolved_uid).await? {
                    Some(id) => id,
                    None => {
                        warn!(imei = %resolved_uid, "System frame for unknown device");
                        return Ok(());
                    }
                };

                let (vehicle_id, company_id, _) = database.get_device_vehicle_info(device_id).await?;

                // Calculate offline duration from last known position
                let last_pos = database.get_last_position(device_id).await?;
                let now = chrono::Utc::now();
                let offline_duration_secs = last_pos.as_ref().map(|p| {
                    let last_utc = chrono::DateTime::<chrono::Utc>::from_naive_utc_and_offset(
                        p.recorded_at, chrono::Utc,
                    );
                    (now - last_utc).num_seconds()
                });

                let was_moving = false; // LastKnownPosition doesn't store speed
                let ignition_was_on = last_pos.as_ref().map_or(false, |p| p.ignition_on);

                let event_type = crate::services::device_event::classify_restart(
                    offline_duration_secs,
                    was_moving,
                    ignition_was_on,
                );

                let record = crate::services::device_event::DeviceEventRecord {
                    device_id,
                    vehicle_id,
                    company_id,
                    event_type: event_type.clone(),
                    event_at: now,
                    offline_duration_secs: offline_duration_secs.map(|d| d as i32),
                    last_known_lat: last_pos.as_ref().map(|p| p.latitude),
                    last_known_lon: last_pos.as_ref().map(|p| p.longitude),
                    last_known_address: None,
                    was_moving,
                    details: Some(serde_json::json!({
                        "frame_type": &frame_str[..4],
                        "imei": &resolved_uid,
                    })),
                };

                // Only insert + notify if offline >= 6h (power cut suspected)
                let min_offline = crate::services::device_event::POWER_CUT_MIN_OFFLINE_SECS;
                if offline_duration_secs.unwrap_or(0) >= min_offline {
                    // Anti-duplicate: check cooldown (24h per device)
                    let now_utc = Utc::now();
                    let mut cooldown = services.device_event_cooldown.lock().await;
                    if let Some(last_event) = cooldown.get(&device_id) {
                        let elapsed = now_utc.signed_duration_since(*last_event).num_seconds();
                        if elapsed < DEVICE_EVENT_COOLDOWN_SECS {
                            info!(
                                device_id,
                                elapsed_secs = elapsed,
                                "Device event skipped — cooldown active (24h)"
                            );
                            return Ok(());
                        }
                    }
                    cooldown.insert(device_id, now_utc);
                    drop(cooldown);

                    match database.insert_device_event(&record).await {
                        Ok(id) => info!(
                            device_id, event_id = id, event_type = event_type.as_str(),
                            offline_secs = ?offline_duration_secs,
                            "Device event recorded (offline >= 6h)"
                        ),
                        Err(e) => warn!(device_id, error = %e, "Failed to insert device event"),
                    }

                    if let Some(pub_ref) = &publisher {
                        if let Err(e) = pub_ref.publish_device_event(&record).await {
                            warn!(device_id, error = %e, "Failed to publish device event to RabbitMQ");
                        }
                    }
                } else {
                    debug!(
                        device_id,
                        offline_secs = ?offline_duration_secs,
                        "Device restart detected but offline < 6h, skipping"
                    );
                }

                return Ok(());
            }

            // Regular position frame
            {
                // Try to extract MAT prefix from frame (format: "NR08G0664 AA23...")
                let (mat_prefix, actual_frame) = extract_mat_prefix(frame_str);
                
                let mut frame = telemetry::hh::parse_frame(actual_frame)?;
                
                // Try to resolve device UID in order of priority:
                // 1. From connection map (learned from AA01/HH01)
                // 2. From MAT prefix (lookup in database)
                let resolved_uid = if let Some(peer) = peer_addr {
                    let map = connection_map.lock().await;
                    if let Some(uid) = map.get(peer).cloned() {
                        Some(uid)
                    } else if let Some(mat) = &mat_prefix {
                        // Try to find device by MAT in database
                        drop(map); // Release lock before async call
                        match database.get_device_uid_by_mat(mat).await {
                            Ok(Some(uid)) => {
                                info!(mat = %mat, imei = %uid, "Resolved device via MAT prefix");
                                // Store in connection map for future frames
                                let mut map = connection_map.lock().await;
                                map.insert(peer.to_string(), uid.clone());
                                Some(uid)
                            }
                            Ok(None) => {
                                warn!(mat = %mat, "MAT prefix found but no device in database");
                                None
                            }
                            Err(e) => {
                                warn!(mat = %mat, error = %e, "Error looking up device by MAT");
                                None
                            }
                        }
                    } else {
                        None
                    }
                } else {
                    None
                };

                // Skip frames without known IMEI
                let resolved_uid = match resolved_uid {
                    Some(uid) => uid,
                    None => {
                        if let Some(peer) = peer_addr {
                            warn!(%peer, mat = ?mat_prefix, "Dropping frame - no IMEI learned and no valid MAT prefix");
                        } else {
                            warn!("Dropping frame - no device UID available");
                        }
                        return Ok(());
                    }
                };

                let event_key = format!(
                    "{}:{}:{:.6}:{:.6}:{:.1}:{}",
                    resolved_uid,
                    frame.recorded_at,
                    frame.latitude,
                    frame.longitude,
                    frame.speed_kph,
                    frame.send_flag
                );

                // Get device_id for services processing
                let device_id_opt = database.get_device_id(&resolved_uid).await?;

                // Log mat received for easy tracking
                if let Some(device_id) = device_id_opt {
                    if let Ok(Some(mat)) = database.get_device_mat(device_id).await {
                        info!(mat = %mat, "Trame reçue");
                    }
                }

                // Ignition-off throttling: when vehicle is stopped (ignition off + slow speed),
                // only store one position every 30 minutes to reduce database bloat
                // IMPORTANT: Always allow ignition state changes through (ON→OFF or OFF→ON)
                const STOPPED_SPEED_THRESHOLD_KPH: f64 = 20.0;
                const STOPPED_MIN_INTERVAL_SECS: i64 = 30 * 60; // 30 minutes
                
                if let Some(device_id) = device_id_opt {
                    // Only apply throttling when ignition is OFF and speed is low
                    if !frame.ignition_on && frame.speed_kph < STOPPED_SPEED_THRESHOLD_KPH {
                        if let Some(last_position) = database.get_last_position(device_id).await? {
                            // Never throttle ignition state changes (vehicle just stopped or started)
                            let ignition_changed = last_position.ignition_on != frame.ignition_on;
                            if ignition_changed {
                                info!(
                                    device_id,
                                    ignition_on = frame.ignition_on,
                                    "Ignition state changed - bypassing throttle"
                                );
                            } else {
                                // Compare GPS frame timestamps (both are in GPS local time, timezone-consistent)
                                let seconds_since_last_stored =
                                    (frame.recorded_at - last_position.recorded_at).num_seconds();

                                // Skip DB write if less than 30 minutes, but still update Redis
                                // so the vehicle stays visible on the real-time monitoring map
                                if seconds_since_last_stored >= 0 && seconds_since_last_stored < STOPPED_MIN_INTERVAL_SECS {
                                    info!(
                                        device_id,
                                        seconds_since_last = seconds_since_last_stored,
                                        speed_kph = frame.speed_kph,
                                        min_interval_secs = STOPPED_MIN_INTERVAL_SECS,
                                        "Frame throttled for DB, refreshing Redis + last_communication"
                                    );
                                    // Update last_communication so the vehicle stays "online" in monitoring
                                    // even when position writes are throttled
                                    if let Err(err) = database.update_device_last_communication(device_id).await {
                                        warn!(?err, "Failed to update last_communication on throttled frame");
                                    }
                                    // Extend Redis TTL so parked vehicles don't disappear.
                                    // Do NOT overwrite cached data — throttled frames bypass
                                    // GPS validation and odometer spike detection.
                                    if let Some(ref redis) = redis_cache {
                                        let (_, company_id, _) = database.get_device_vehicle_info(device_id).await?;
                                        if let Err(err) = redis.refresh_position_ttl(&resolved_uid, company_id).await {
                                            warn!(?err, "Failed to refresh Redis TTL on throttled frame");
                                        }
                                    }
                                    return Ok(());
                                }
                            }
                        }
                    }
                    // When ignition is ON or speed >= 20 km/h, all frames are stored (no throttling)
                }

                // GPS Validation: Check for aberrant points, invalid fix, low satellites, jumps
                if let Some(device_id) = device_id_opt {
                    let validation = services.gps_validator.validate(device_id, &frame).await;
                    if !validation.should_store() {
                        if let crate::services::gps_validator::ValidationResult::Invalid { reason } = &validation {
                            let flags_hex = format!("0x{:02X}", frame.flags_raw);
                            warn!(
                                device_id,
                                imei = %resolved_uid,
                                lat = frame.latitude,
                                lon = frame.longitude,
                                speed = frame.speed_kph,
                                is_valid = frame.is_valid,
                                flags = %flags_hex,
                                satellites = ?frame.satellites_in_view,
                                send_flag = frame.send_flag,
                                reason = %reason,
                                "Frame REJECTED by GPS validator"
                            );
                        }
                        return Ok(());
                    }
                }

                // Anti-drift stabilization: when vehicle is stopped, anchor position
                // to prevent GPS drift (e.g. vehicle appearing in a lake)
                if let Some(device_id) = device_id_opt {
                    let stabilized = services.gps_stabilizer.stabilize(device_id, &frame).await;
                    if stabilized.was_stabilized {
                        info!(
                            device_id,
                            drift_m = ?stabilized.drift_distance_meters,
                            original_lat = frame.latitude,
                            original_lon = frame.longitude,
                            anchored_lat = stabilized.latitude,
                            anchored_lon = stabilized.longitude,
                            "Anti-drift: position stabilized (vehicle stopped)"
                        );
                        frame.latitude = stabilized.latitude;
                        frame.longitude = stabilized.longitude;
                    }
                }

                // Geocode the position (async, non-blocking)
                if frame.is_valid {
                    frame.address = services.geocoding.reverse_geocode(frame.latitude, frame.longitude).await;
                }

                // ============================================================
                // GISV1 CONDITIONS - From AAP.cs + SaveDynData stored procedure
                // ============================================================
                
                // --- From AAP.cs lines 640-651: Time coherence correction ---
                // If timestamp is before 2016-01-01, apply correction offset
                const THRESHOLD_2016: i64 = 1451606400; // 2016-01-01 00:00:00 UTC
                const TIME_OFFSET: i64 = 619315200;     // ~19.6 years correction
                const LOCAL_TIME_OFFSET_MINUTES: i64 = 0; // GPS already sends local time, no offset needed
                
                let unix_time = frame.recorded_at.and_utc().timestamp();
                if unix_time < THRESHOLD_2016 {
                    let corrected = unix_time + TIME_OFFSET;
                    // Apply correction (no artificial timezone offset)
                    frame.recorded_at = chrono::DateTime::from_timestamp(corrected, 0)
                        .map(|dt| dt.naive_utc())
                        .unwrap_or(frame.recorded_at);
                    info!(
                        imei = %resolved_uid,
                        old_unix = unix_time,
                        corrected_unix = corrected,
                        new_time = %frame.recorded_at,
                        "Atime correction applied (same as GISV1)"
                    );
                }

                // Apply local timezone offset (GISV1 stored TakenAt +1h)
                if LOCAL_TIME_OFFSET_MINUTES != 0 {
                    if let Some(adjusted) = frame
                        .recorded_at
                        .checked_add_signed(chrono::Duration::minutes(LOCAL_TIME_OFFSET_MINUTES))
                    {
                        frame.recorded_at = adjusted;
                    } else {
                        warn!(
                            imei = %resolved_uid,
                            original = %frame.recorded_at,
                            offset_minutes = LOCAL_TIME_OFFSET_MINUTES,
                            "Failed to apply local time offset"
                        );
                    }
                }
                
                // SendFlag values (from ACI protocol documentation):
                // 0: CMDUSER - Command from server
                // 1: SENDP - Periodic transmission (timer)
                // 2: GPSVAL - GPS fix obtained (valid signal)
                // 3: CAPDEV - Cap deviation > 10° (vehicle turning)
                // 4: IOCHANGE - I/O state change (ignition, doors, etc.)
                // 5: OVERSPEED - Overspeed alert
                // 6: JERCK - Accelerometer event (harsh braking, etc.)
                // 7: IBUTTON - iButton key read (ID in added_info)
                // 8-10: I1/I2/I3 Event - Digital input events
                // 11: ALERT - Generic alert (SOS, panic, etc.)
                
                // Condition 0: Reject send_flag == 2 (GPSVAL) - same as GISV1
                if frame.send_flag == 2 {
                    info!(
                        imei = %resolved_uid,
                        send_flag = frame.send_flag,
                        "Frame SKIPPED: send_flag == 2 (GPSVAL) rejected (same as GISV1)"
                    );
                    return Ok(());
                }
                
                // Condition 1: Reject frames too far in the future (> 2 hours)
                // GPS devices may send local time (UTC+1) while server runs UTC,
                // so up to 1h offset is normal. 2h margin accepts UTC+1 devices
                // while rejecting corrupted timestamps (seen at 2h+ offset).
                let now_utc = chrono::Utc::now().naive_utc();
                let max_future = now_utc + chrono::Duration::hours(2);
                if frame.recorded_at > max_future {
                    warn!(
                        imei = %resolved_uid,
                        frame_time = %frame.recorded_at,
                        server_time = %now_utc,
                        "Frame SKIPPED: timestamp > 4h in the future"
                    );
                    return Ok(());
                }

                // ============================================================
                // ADDITIONAL VALIDATIONS
                // ============================================================

                // Coordinates near 0,0 = GPS has no fix (null island)
                // Instead of rejecting, use last known valid position (same as GISV1)
                if frame.latitude.abs() < 1.0 && frame.longitude.abs() < 2.0 {
                    if let Some(device_id) = device_id_opt {
                        if let Some(last_pos) = database.get_last_position(device_id).await? {
                            frame.latitude = last_pos.latitude;
                            frame.longitude = last_pos.longitude;
                            frame.is_valid = false; // Mark as interpolated/no-fix
                            info!(
                                imei = %resolved_uid,
                                lat = frame.latitude,
                                lon = frame.longitude,
                                "GPS no-fix: using last known position"
                            );
                        } else {
                            info!(
                                imei = %resolved_uid,
                                "Frame SKIPPED: No GPS fix and no previous position known"
                            );
                            return Ok(());
                        }
                    } else {
                        info!(
                            imei = %resolved_uid,
                            "Frame SKIPPED: No GPS fix and device not yet registered"
                        );
                        return Ok(());
                    }
                }

                // Longitude must be -180 to +180
                if frame.longitude < -180.0 || frame.longitude > 180.0 {
                    warn!(
                        imei = %resolved_uid,
                        lon = frame.longitude,
                        "Frame SKIPPED: Longitude out of range"
                    );
                    return Ok(());
                }

                // Latitude must be -90 to +90
                if frame.latitude < -90.0 || frame.latitude > 90.0 {
                    warn!(
                        imei = %resolved_uid,
                        lat = frame.latitude,
                        "Frame SKIPPED: Latitude out of range"
                    );
                    return Ok(());
                }

                // Get vehicle and company info first (needed for parallel operations)
                let (vehicle_id, company_id, _firmware_version) = if let Some(device_id) = device_id_opt {
                    database.get_device_vehicle_info(device_id).await?
                } else {
                    (None, 1, None) // Default company_id
                };
                // Auto-detect FMS capability from frame version (V3 = FMS data present)
                let is_fms_frame = matches!(frame.version, crate::telemetry::model::FrameVersion::V3);

                // Speed spike filter: detect and correct physically impossible speed changes
                if let Some(device_id) = device_id_opt {
                    if frame.ignition_on && frame.speed_kph > 0.0 {
                        let result = services.speed_filter.filter(device_id, frame.speed_kph, frame.recorded_at).await;
                        if result.was_filtered {
                            frame.speed_kph = result.corrected_speed;
                        }
                    } else {
                        // When ignition off or speed 0, reset filter state with 0
                        let _ = services.speed_filter.filter(device_id, 0.0, frame.recorded_at).await;
                    }
                }

                // Only push to Redis/pub-sub if the frame is recent (< 3 minutes old).
                // History frames (AA23 batches) have old timestamps and should NOT update
                // the real-time map — otherwise the vehicle "teleports" across the map
                // as the frontend receives 50 rapid position updates from a past trip.
                let frame_age_secs = (chrono::Utc::now().naive_utc() - frame.recorded_at).num_seconds();
                let is_recent_frame = frame_age_secs < 180; // 3 minutes

                // PARALLEL EXECUTION: DB write + conditional Redis cache + RabbitMQ publish
                let db_future = database.ingest_hh_frame(&resolved_uid, protocol, &frame, &event_key);

                let redis_future = async {
                    if is_recent_frame {
                        if let Some(ref redis) = redis_cache {
                            if let Err(err) = redis.cache_position(&resolved_uid, vehicle_id, company_id, &frame).await {
                                warn!(?err, "Failed to cache position in Redis");
                            }
                        }
                    }
                };

                let rabbitmq_future = async {
                    if is_recent_frame {
                        if let Some(ref pub_ref) = publisher {
                            if let Err(err) = pub_ref.publish_hh_frame(&resolved_uid, protocol, &frame).await {
                                warn!(?err, "Failed to publish telemetry event");
                            }
                        }
                    }
                };

                // Execute all three in parallel
                let (db_result, _, _) = tokio::join!(db_future, redis_future, rabbitmq_future);
                db_result?;

                // Log successful ingestion with key frame info for debugging
                info!(
                    imei = %resolved_uid,
                    send_flag = frame.send_flag,
                    speed_kph = frame.speed_kph,
                    heading = frame.heading_deg,
                    lat = frame.latitude,
                    lon = frame.longitude,
                    ignition = frame.ignition_on,
                    is_valid = frame.is_valid,
                    "Position ingested successfully (parallel)"
                );

                // Process services (stop detection, fuel tracking)
                if let Some(device_id) = device_id_opt {
                    // Process stop detection
                    if let Some(completed_stop) = services.stop_detector.process_frame(device_id, &frame).await {
                        if let Err(err) = database.insert_vehicle_stop(&completed_stop, vehicle_id, company_id).await {
                            warn!(?err, device_id, "Failed to insert vehicle stop");
                        } else {
                            info!(device_id, duration = completed_stop.duration_seconds, "Vehicle stop recorded");
                        }
                    }

                    // Process fuel tracking - ONLY for V3 frames which contain FMS data
                    // V1 frames don't have FMS, fuel_raw is base value (often garbage)
                    if is_fms_frame && frame.fuel_raw > 0 {
                        // On first encounter for this device, let fuel_tracker calibrate
                        // from the current (correctly converted) frame rather than seeding
                        // from DB which may contain legacy unconverted values.
                        // The fuel_tracker treats the first frame as a baseline (no event emitted).

                        // Convert fuel_raw → percent based on fuel_sensor_mode
                        let fuel_percent: i16 = match database.get_fuel_config(device_id).await {
                            Ok((mode, tank_cap)) => {
                                let raw = frame.fuel_raw as i16;
                                let tank = tank_cap.unwrap_or(60) as i16; // Default 60L
                                match mode.as_str() {
                                    "percent" => raw,                                                    // Already 0-100%
                                    "raw_255" => ((raw as f64 / 255.0) * 100.0).round() as i16,         // 0-255 → 0-100%
                                    "liters" => if tank > 0 { ((raw as f64 / tank as f64) * 100.0).round() as i16 } else { raw },
                                    "half_liter" => if tank > 0 { ((raw as f64 * 0.5 / tank as f64) * 100.0).round() as i16 } else { (raw as f64 * 0.5).round() as i16 },
                                    _ => raw,
                                }.clamp(0, 100)
                            },
                            Err(err) => {
                                warn!(?err, device_id, "Failed to get fuel config, using raw value");
                                (frame.fuel_raw as i16).clamp(0, 100)
                            }
                        };

                        if let Some(fuel_event) = services.fuel_tracker.process_frame(device_id, &frame, fuel_percent).await {
                            if let Err(err) = database.insert_fuel_record(&fuel_event, vehicle_id, company_id).await {
                                warn!(?err, device_id, "Failed to insert fuel record");
                            } else {
                                info!(
                                    device_id,
                                    event_type = fuel_event.event_type.as_str(),
                                    fuel_percent = fuel_event.fuel_percent,
                                    "Fuel event recorded"
                                );
                            }
                        }
                    }

                    // Process geofence detection (only for valid positions)
                    if frame.is_valid {
                        // Refresh geofences if needed
                        if services.geofence_detector.needs_refresh().await {
                            if let Ok(geofences) = database.load_geofences().await {
                                services.geofence_detector.load_geofences(geofences).await;
                            }
                        }

                        // Check for geofence entry/exit events
                        if let Some(vid) = vehicle_id {
                            let timestamp = chrono::DateTime::<chrono::Utc>::from_naive_utc_and_offset(
                                frame.recorded_at,
                                chrono::Utc,
                            );
                            let geofence_events = services.geofence_detector.process_frame(
                                device_id,
                                vid,
                                company_id,
                                frame.latitude,
                                frame.longitude,
                                Some(frame.speed_kph),
                                timestamp,
                            ).await;

                            for gf_event in geofence_events {
                                // Get duration if this is an exit event
                                let duration = if gf_event.event_type == crate::services::geofence_detector::GeofenceEventType::Exit {
                                    services.geofence_detector.get_duration_inside(device_id, gf_event.geofence_id).await
                                        .map(|d| d as i32)
                                } else {
                                    None
                                };

                                if let Err(err) = database.insert_geofence_event(&gf_event, duration).await {
                                    warn!(?err, device_id, geofence_id = gf_event.geofence_id, "Failed to insert geofence event");
                                } else {
                                    info!(
                                        device_id,
                                        vehicle_id = vid,
                                        geofence_id = gf_event.geofence_id,
                                        geofence_name = %gf_event.geofence_name,
                                        event_type = gf_event.event_type.as_str(),
                                        "Geofence event recorded"
                                    );
                                }
                            }
                        }
                    }

                    // Process trip detection
                    if let Some(completed_trip) = services.trip_detector.process_frame(
                        device_id,
                        vehicle_id,
                        company_id,
                        &frame,
                    ).await {
                        if let Err(err) = database.insert_trip(&completed_trip).await {
                            warn!(?err, device_id, "Failed to insert trip");
                        } else {
                            info!(
                                device_id,
                                vehicle_id = ?completed_trip.vehicle_id,
                                distance_km = completed_trip.distance_km,
                                duration_min = completed_trip.duration_minutes,
                                "Trip recorded"
                            );
                        }
                    }

                    // Process driving events detection
                    let driving_events = services.driving_events_detector.process_frame(
                        device_id,
                        vehicle_id,
                        company_id,
                        &frame,
                    ).await;

                    for event in driving_events {
                        if let Err(err) = database.insert_driving_event(&event).await {
                            warn!(?err, device_id, event_type = event.event_type.as_str(), "Failed to insert driving event");
                        } else {
                            info!(
                                device_id,
                                event_type = event.event_type.as_str(),
                                severity = event.severity.as_str(),
                                g_force = event.g_force,
                                "Driving event recorded"
                            );
                        }
                    }
                }

                info!(protocol, device_uid = %resolved_uid, "Ingested telemetry frame");
            }
        }
        other => {
            warn!(protocol = other, "No decoder registered for protocol");
        }
    }

    Ok(())
}

/// Route a decoded Noron NR024 position frame through the shared ingestion pipeline.
/// This reuses the same DB/Redis/RabbitMQ/services path as NEMS frames.
/// Dedicated handler for Teltonika devices (FMB130, FMB150, FMC, FMU, FMM).
///
/// The flow is stateful, unlike NEMS/Noron which just stream frames:
///   1. Device sends IMEI (length-prefixed, 17 bytes typical).
///   2. We ACK with 0x01 (accept) once we've checked the IMEI maps to a known
///      `gps_devices` row, otherwise 0x00 and close.
///   3. Loop: read one AVL TCP frame (binary, framed by preamble + length),
///      parse with nom-teltonika, persist every record, then write back a
///      4-byte big-endian count of records ACK'd. The device repeats the
///      same packet until we ACK, so persistence MUST happen before the ACK.
///
/// Reuses `route_noron_frame` for the per-frame downstream work (DB write,
/// Redis cache, RabbitMQ publish, services pipeline) — that function is
/// protocol-agnostic once it has an `HhFrame` in hand, we just override the
/// reported protocol label below.
async fn handle_teltonika_connection(
    stream: TcpStream,
    cfg: Arc<ListenerConfig>,
    database: Arc<dyn TelemetryStore>,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
) -> Result<()> {
    let peer = stream.peer_addr().ok().map(|a| a.to_string());
    let peer_str = peer.as_deref().unwrap_or("unknown");

    let (mut reader, mut writer) = tokio::io::split(stream);

    // ── Step 1: read IMEI handshake ────────────────────────────────────────
    // The handshake is small but variable (2-byte length + IMEI). We read up
    // to 32 bytes which is generous — real packets are 17 bytes for a 15-digit
    // IMEI. nom-teltonika handles partial / trailing bytes gracefully.
    let mut buf = [0u8; 32];
    let read = match reader.read(&mut buf).await {
        Ok(0) => {
            debug!(peer = peer_str, "Teltonika: device closed before handshake");
            return Ok(());
        }
        Ok(n) => n,
        Err(e) => {
            warn!(?e, peer = peer_str, "Teltonika: read failed at handshake stage");
            return Err(e.into());
        }
    };

    let imei = match telemetry::teltonika::parse_imei(&buf[..read]) {
        Ok(imei) => imei,
        Err(e) => {
            warn!(
                ?e,
                peer = peer_str,
                bytes = hex::encode(&buf[..read]),
                "Teltonika: malformed IMEI handshake — rejecting connection"
            );
            let _ = writer.write_all(&telemetry::teltonika::build_imei_ack(false)).await;
            return Ok(());
        }
    };

    // Resolve the device in our database. Unknown IMEIs are still ACK'd —
    // operators commonly plug a new boitier before adding it to the fleet
    // table, and dropping the connection costs them a minute of confusion.
    // The downstream `ingest_hh_frame` will reject the records cleanly if
    // the device is not yet registered.
    let device_id = match database.get_device_id(&imei).await {
        Ok(Some(id)) => Some(id),
        Ok(None) => {
            warn!(
                imei = %imei,
                peer = peer_str,
                "Teltonika: IMEI not registered in gps_devices — accepting frames but they will be dropped at insert"
            );
            None
        }
        Err(e) => {
            warn!(
                ?e,
                imei = %imei,
                peer = peer_str,
                "Teltonika: DB lookup failed during handshake — closing connection"
            );
            let _ = writer.write_all(&telemetry::teltonika::build_imei_ack(false)).await;
            return Ok(());
        }
    };

    if let Err(e) = writer.write_all(&telemetry::teltonika::build_imei_ack(true)).await {
        warn!(?e, peer = peer_str, imei = %imei, "Teltonika: failed to write IMEI ACK");
        return Ok(());
    }

    info!(
        peer = peer_str,
        imei = %imei,
        device_id = ?device_id,
        port = cfg.port,
        "Teltonika: IMEI handshake completed"
    );

    // ── Step 2: AVL frame loop ─────────────────────────────────────────────
    // Frames can be up to a few KB (Codec 8E with 50 records). We read into
    // a growing buffer until the parser succeeds, then drain the consumed
    // bytes. This keeps us robust against TCP fragmentation.
    let mut accumulator: Vec<u8> = Vec::with_capacity(4096);
    let mut read_buf = vec![0u8; 4096];

    loop {
        let read = match reader.read(&mut read_buf).await {
            Ok(0) => {
                info!(peer = peer_str, imei = %imei, "Teltonika: device disconnected");
                return Ok(());
            }
            Ok(n) => n,
            Err(e) => {
                warn!(?e, peer = peer_str, imei = %imei, "Teltonika: read failed");
                return Err(e.into());
            }
        };

        accumulator.extend_from_slice(&read_buf[..read]);

        // Try parsing one or more complete AVL frames out of the accumulator.
        // nom-teltonika returns an error if the buffer is incomplete; in that
        // case we leave the bytes in place and wait for the next read.
        loop {
            match telemetry::teltonika::parse_avl_frame(&accumulator) {
                Ok(avl_frame) => {
                    let frames = telemetry::teltonika::avl_frame_to_hh_frames(&avl_frame);
                    let record_count = frames.len() as u32;

                    info!(
                        peer = peer_str,
                        imei = %imei,
                        codec = ?avl_frame.codec,
                        records = record_count,
                        "Teltonika: AVL frame parsed"
                    );

                    // Persist every record through the same pipeline as Noron.
                    // We ACK to the device with the record count regardless of
                    // per-record warnings — Teltonika only cares that we
                    // processed the batch, retries on `0` mean the device
                    // will resend the same packet.
                    for frame in frames {
                        if let Err(err) = route_teltonika_frame(
                            &imei,
                            frame,
                            Arc::clone(&database),
                            publisher.clone(),
                            Arc::clone(&services),
                            redis_cache.clone(),
                        )
                        .await
                        {
                            warn!(?err, imei = %imei, "Teltonika: failed to ingest record (kept ACK to avoid retry storm)");
                        }
                    }

                    // ACK with the record count, BE u32.
                    let ack = telemetry::teltonika::build_avl_ack(record_count);
                    if let Err(e) = writer.write_all(&ack).await {
                        warn!(?e, peer = peer_str, imei = %imei, "Teltonika: failed to send AVL ACK");
                        return Ok(());
                    }

                    // The parser doesn't tell us exactly how many bytes were
                    // consumed (its API returns the parsed value only) — but
                    // a frame always finishes on its trailing CRC, so the
                    // simplest correct strategy is to clear the accumulator
                    // and let the next loop iteration accumulate the next
                    // frame. Teltonika devices wait for our ACK before
                    // sending the next packet, so there is never trailing
                    // data after a successful parse.
                    accumulator.clear();
                    break;
                }
                Err(_) => {
                    // Either incomplete or malformed. If accumulator grew
                    // beyond a sane upper bound we drop everything to avoid
                    // memory exhaustion from a stuck device.
                    if accumulator.len() > 64 * 1024 {
                        warn!(
                            peer = peer_str,
                            imei = %imei,
                            size = accumulator.len(),
                            "Teltonika: dropping oversized accumulator buffer"
                        );
                        accumulator.clear();
                    }
                    break; // wait for more bytes
                }
            }
        }
    }
}

/// Dedicated handler for Concox / GT06 family devices (GT06, GT06N, TR06,
/// JT701, JM-VL01 and OEM rebrands including some Coban units).
///
/// Flow:
///   1. Device sends a 0x01 login packet with its BCD-encoded IMEI.
///   2. We ACK with the canonical CRC-stamped reply (mandatory — device
///      retries until ACK).
///   3. Loop: read 0x78 0x78 / 0x79 0x79 packets. For GPS payloads
///      (0x10/0x12/0x16/0x1A/0x22) we persist through the canonical
///      pipeline. For heartbeats (0x13) we ACK and optionally surface
///      ignition/alarm changes.
async fn handle_gt06_connection(
    stream: TcpStream,
    cfg: Arc<ListenerConfig>,
    database: Arc<dyn TelemetryStore>,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
) -> Result<()> {
    let peer = stream.peer_addr().ok().map(|a| a.to_string());
    let peer_str = peer.as_deref().unwrap_or("unknown").to_string();

    let (mut reader, mut writer) = tokio::io::split(stream);
    let mut imei: Option<String> = None;

    let mut accumulator: Vec<u8> = Vec::with_capacity(4096);
    let mut read_buf = vec![0u8; 4096];

    loop {
        let read = match reader.read(&mut read_buf).await {
            Ok(0) => {
                info!(peer = %peer_str, imei = ?imei, "GT06: device disconnected");
                return Ok(());
            }
            Ok(n) => n,
            Err(e) => {
                warn!(?e, peer = %peer_str, "GT06: read failed");
                return Err(e.into());
            }
        };
        accumulator.extend_from_slice(&read_buf[..read]);

        // Consume as many complete packets as we have buffered.
        loop {
            match telemetry::gt06::decode_one(&accumulator) {
                Ok(Some(decoded)) => {
                    match decoded.result {
                        telemetry::gt06::Gt06DecodeResult::Login { imei: new_imei, serial } => {
                            info!(peer = %peer_str, imei = %new_imei, "GT06 login received");
                            // ACK is mandatory — without it the device retries every few seconds.
                            let ack = telemetry::gt06::build_ack(telemetry::gt06::PROTO_LOGIN, serial);
                            if let Err(e) = writer.write_all(&ack).await {
                                warn!(?e, peer = %peer_str, "GT06: failed to write login ACK");
                                return Ok(());
                            }
                            // Lookup is best-effort: unknown IMEIs are accepted but their frames
                            // get dropped at insert time. Matches the Teltonika handler's behaviour.
                            if let Err(e) = database.get_device_id(&new_imei).await {
                                warn!(?e, imei = %new_imei, "GT06: DB lookup failed (continuing)");
                            } else {
                                debug!(imei = %new_imei, "GT06 IMEI resolved against gps_devices");
                            }
                            imei = Some(new_imei);
                        }
                        telemetry::gt06::Gt06DecodeResult::Position { frame, serial: _, protocol } => {
                            if let Some(ref id) = imei {
                                if let Err(err) = route_gt06_frame(
                                    id, frame,
                                    Arc::clone(&database),
                                    publisher.clone(),
                                    Arc::clone(&services),
                                    redis_cache.clone(),
                                ).await {
                                    warn!(?err, imei = %id, protocol = format!("0x{:02X}", protocol), "GT06: failed to ingest position");
                                }
                            } else {
                                warn!(peer = %peer_str, "GT06: GPS packet received before login — dropped");
                            }
                        }
                        telemetry::gt06::Gt06DecodeResult::Status { frame, serial } => {
                            let ack = telemetry::gt06::build_ack(telemetry::gt06::PROTO_STATUS, serial);
                            if let Err(e) = writer.write_all(&ack).await {
                                warn!(?e, peer = %peer_str, "GT06: failed to write status ACK");
                            }
                            // A status/heartbeat proves the device is connected even
                            // without a GPS fix — keep it "online" while parked.
                            if let Some(id) = imei.as_ref() {
                                touch_device_online(&database, id, "gt06").await;
                            }
                            if let (Some(frame), Some(id)) = (frame, imei.as_ref()) {
                                if let Err(err) = route_gt06_frame(
                                    id, frame,
                                    Arc::clone(&database),
                                    publisher.clone(),
                                    Arc::clone(&services),
                                    redis_cache.clone(),
                                ).await {
                                    warn!(?err, imei = %id, "GT06: failed to ingest status payload");
                                }
                            }
                        }
                        telemetry::gt06::Gt06DecodeResult::Unknown { protocol, .. } => {
                            // Keep the connection open — operators will see the warn log
                            // emitted inside decode_one and decide whether to extend.
                            debug!(peer = %peer_str, protocol = format!("0x{:02X}", protocol), "GT06: ignored unknown protocol");
                        }
                    }
                    accumulator.drain(..decoded.consumed);
                }
                Ok(None) => {
                    // Need more bytes — exit inner loop, wait for next read.
                    if accumulator.len() > 64 * 1024 {
                        warn!(peer = %peer_str, size = accumulator.len(), "GT06: dropping oversized buffer");
                        accumulator.clear();
                    }
                    break;
                }
                Err(e) => {
                    warn!(?e, peer = %peer_str, "GT06: decode error — resetting buffer");
                    accumulator.clear();
                    break;
                }
            }
        }
    }
}

/// Refresh `last_communication` for a device identified by IMEI so the
/// monitoring view keeps it "online". Used for heartbeat / keepalive packets
/// that prove connectivity without carrying a GPS fix (GT06 status, Coban
/// `<IMEI>;` keepalive). No-op (logged at debug) if the IMEI isn't registered.
async fn touch_device_online(database: &Arc<dyn TelemetryStore>, imei: &str, proto: &str) {
    match database.get_device_id(imei).await {
        Ok(Some(device_id)) => {
            if let Err(err) = database.update_device_last_communication(device_id).await {
                warn!(?err, imei = %imei, proto, "failed to refresh last_communication on heartbeat");
            }
        }
        Ok(None) => {
            debug!(imei = %imei, proto, "heartbeat from unregistered device — not marking online");
        }
        Err(err) => warn!(?err, imei = %imei, proto, "device lookup failed on heartbeat"),
    }
}

/// Handler for Coban TK103 / TK303 family ASCII trackers.
///
/// Wire format is line-based. The first packet on a connection is usually
/// either an `imei:<IMEI>` login line or a `*HQ,<IMEI>,V1,...#` position
/// sentence (the latter implicitly registers the IMEI). We ACK the login
/// with `LOAD\r\n` to silence the device's retry timer.
async fn handle_coban_connection(
    stream: TcpStream,
    cfg: Arc<ListenerConfig>,
    database: Arc<dyn TelemetryStore>,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
) -> Result<()> {
    let peer = stream.peer_addr().ok().map(|a| a.to_string());
    let peer_str = peer.as_deref().unwrap_or("unknown").to_string();

    let (mut reader, mut writer) = tokio::io::split(stream);
    let mut imei: Option<String> = None;
    let mut buf = vec![0u8; 4096];
    let mut pending = String::new();

    loop {
        let read = match reader.read(&mut buf).await {
            Ok(0) => {
                info!(peer = %peer_str, imei = ?imei, port = cfg.port, "Coban: device disconnected");
                return Ok(());
            }
            Ok(n) => n,
            Err(e) => {
                warn!(?e, peer = %peer_str, "Coban: read failed");
                return Err(e.into());
            }
        };
        match std::str::from_utf8(&buf[..read]) {
            Ok(s) => pending.push_str(s),
            Err(_) => {
                warn!(peer = %peer_str, "Coban: non-UTF8 bytes received — dropping");
                continue;
            }
        }

        let sentences = telemetry::coban::extract_sentences(&pending);
        // Keep the trailing fragment (if any) for the next read. Easiest:
        // if `pending` ended on a terminator (`#`, `;`, `\n`), clear it; else
        // re-stash the chunk after the last terminator.
        pending = match pending.rfind(|c: char| c == '#' || c == ';' || c == '\n') {
            Some(idx) if idx + 1 == pending.len() => String::new(),
            Some(idx) => pending[idx + 1..].to_string(),
            None => pending.clone(),
        };

        for sentence in sentences {
            match telemetry::coban::parse_sentence(&sentence) {
                Ok(telemetry::coban::TkDecodeResult::Login { imei: new_imei }) => {
                    info!(peer = %peer_str, imei = %new_imei, "Coban login received");
                    if let Err(e) = writer.write_all(telemetry::coban::LOGIN_ACK).await {
                        warn!(?e, peer = %peer_str, "Coban: failed to write LOAD ack");
                        return Ok(());
                    }
                    imei = Some(new_imei);
                    if let Some(ref id) = imei {
                        touch_device_online(&database, id, "coban").await;
                    }
                }
                Ok(telemetry::coban::TkDecodeResult::Position { frame, imei: sentence_imei }) => {
                    // First-position implicit registration: if we didn't see an
                    // explicit `imei:` login but the *HQ sentence carries the
                    // IMEI inline, use that.
                    if imei.is_none() {
                        imei = Some(sentence_imei.clone());
                    }
                    let id = imei.clone().unwrap_or(sentence_imei);
                    if let Err(err) = route_coban_frame(
                        &id, frame,
                        Arc::clone(&database),
                        publisher.clone(),
                        Arc::clone(&services),
                        redis_cache.clone(),
                    ).await {
                        warn!(?err, imei = %id, "Coban: failed to ingest position");
                    }
                }
                Ok(telemetry::coban::TkDecodeResult::Heartbeat { imei: hb_imei }) => {
                    // Reply ON\r\n so the device keeps the link open.
                    let _ = writer.write_all(telemetry::coban::KEEPALIVE_ACK).await;
                    // A bare `<IMEI>;` keepalive carries the IMEI — adopt it if
                    // we haven't seen an explicit login on this connection yet.
                    if imei.is_none() {
                        imei = hb_imei;
                    }
                    // Heartbeat = connected but no GPS. Keep the vehicle "online".
                    if let Some(ref id) = imei {
                        touch_device_online(&database, id, "coban").await;
                    }
                }
                Ok(telemetry::coban::TkDecodeResult::Unknown { head }) => {
                    debug!(peer = %peer_str, head = %head, "Coban: unknown sentence ignored");
                }
                Err(e) => {
                    warn!(?e, peer = %peer_str, sentence = %sentence, "Coban: failed to parse sentence");
                }
            }
        }
    }
}

/// GT06 routing — same downstream pipeline as the other binary handlers, just
/// tagged with `protocol = "gt06"` for log filtering.
async fn route_gt06_frame(
    device_uid: &str,
    mut frame: telemetry::model::HhFrame,
    database: Arc<dyn TelemetryStore>,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
) -> Result<()> {
    route_generic_frame("gt06", device_uid, &mut frame, database, publisher, services, redis_cache).await
}

/// Coban routing.
async fn route_coban_frame(
    device_uid: &str,
    mut frame: telemetry::model::HhFrame,
    database: Arc<dyn TelemetryStore>,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
) -> Result<()> {
    route_generic_frame("coban", device_uid, &mut frame, database, publisher, services, redis_cache).await
}

/// Shared downstream pipeline used by every per-protocol router. Centralises
/// the per-frame work that NEMS / Noron / Teltonika / GT06 / Coban all need:
/// GPS validation, anti-drift, geocoding, speed-spike filter, parallel
/// DB+Redis+RabbitMQ write, stop / trip / driving-events / geofence detection.
async fn route_generic_frame(
    protocol: &'static str,
    device_uid: &str,
    frame: &mut telemetry::model::HhFrame,
    database: Arc<dyn TelemetryStore>,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
) -> Result<()> {
    let device_id_opt = database.get_device_id(device_uid).await?;

    if frame.latitude.abs() < 1.0 && frame.longitude.abs() < 2.0 {
        if let Some(device_id) = device_id_opt {
            if let Some(last_pos) = database.get_last_position(device_id).await? {
                frame.latitude = last_pos.latitude;
                frame.longitude = last_pos.longitude;
                frame.is_valid = false;
                debug!(device_uid, protocol, "no-fix: using last known position");
            } else {
                debug!(device_uid, protocol, "frame skipped: no GPS fix and no previous position");
                return Ok(());
            }
        } else {
            return Ok(());
        }
    }

    if !(-180.0..=180.0).contains(&frame.longitude) || !(-90.0..=90.0).contains(&frame.latitude) {
        warn!(device_uid, protocol, lat = frame.latitude, lon = frame.longitude, "frame skipped: coords out of range");
        return Ok(());
    }

    let tomorrow = chrono::Utc::now().date_naive() + Duration::days(1);
    if frame.recorded_at.date() >= tomorrow {
        warn!(device_uid, protocol, date = %frame.recorded_at, "frame skipped: date in future");
        return Ok(());
    }

    if let Some(device_id) = device_id_opt {
        let validation = services.gps_validator.validate(device_id, frame).await;
        if !validation.should_store() {
            if let crate::services::gps_validator::ValidationResult::Invalid { reason } = &validation {
                debug!(device_uid, protocol, reason = %reason, "frame REJECTED by GPS validator");
            }
            return Ok(());
        }

        let stabilized = services.gps_stabilizer.stabilize(device_id, frame).await;
        if stabilized.was_stabilized {
            frame.latitude = stabilized.latitude;
            frame.longitude = stabilized.longitude;
        }

        if frame.ignition_on && frame.speed_kph > 0.0 {
            let result = services.speed_filter.filter(device_id, frame.speed_kph, frame.recorded_at).await;
            if result.was_filtered {
                frame.speed_kph = result.corrected_speed;
            }
        } else {
            let _ = services.speed_filter.filter(device_id, 0.0, frame.recorded_at).await;
        }
    }

    if frame.is_valid {
        frame.address = services.geocoding.reverse_geocode(frame.latitude, frame.longitude).await;
    }

    let event_key = format!(
        "{}:{}:{:.6}:{:.6}:{:.1}:{}",
        device_uid, frame.recorded_at, frame.latitude, frame.longitude, frame.speed_kph, frame.send_flag
    );

    let (vehicle_id, company_id, _firmware_version) = if let Some(device_id) = device_id_opt {
        database.get_device_vehicle_info(device_id).await?
    } else {
        (None, 1, None)
    };

    let db_future = database.ingest_hh_frame(device_uid, protocol, frame, &event_key);
    let redis_future = async {
        if let Some(ref redis) = redis_cache {
            if let Err(err) = redis.cache_position(device_uid, vehicle_id, company_id, frame).await {
                warn!(?err, protocol, "Failed to cache position in Redis");
            }
        }
    };
    let rabbitmq_future = async {
        if let Some(ref pub_ref) = publisher {
            if let Err(err) = pub_ref.publish_hh_frame(device_uid, protocol, frame).await {
                warn!(?err, protocol, "Failed to publish telemetry event");
            }
        }
    };
    let (db_result, _, _) = tokio::join!(db_future, redis_future, rabbitmq_future);
    db_result?;

    info!(
        device_uid,
        protocol,
        speed_kph = frame.speed_kph,
        lat = frame.latitude,
        lon = frame.longitude,
        ignition = frame.ignition_on,
        is_valid = frame.is_valid,
        "position ingested"
    );

    if let Some(device_id) = device_id_opt {
        if let Some(completed_stop) = services.stop_detector.process_frame(device_id, frame).await {
            if let Err(err) = database.insert_vehicle_stop(&completed_stop, vehicle_id, company_id).await {
                warn!(?err, device_id, protocol, "Failed to insert vehicle stop");
            }
        }

        if let Some(completed_trip) = services.trip_detector.process_frame(
            device_id, vehicle_id, company_id, frame,
        ).await {
            if let Err(err) = database.insert_trip(&completed_trip).await {
                warn!(?err, device_id, protocol, "Failed to insert trip");
            }
        }

        let driving_events = services.driving_events_detector.process_frame(
            device_id, vehicle_id, company_id, frame,
        ).await;
        for event in driving_events {
            if let Err(err) = database.insert_driving_event(&event).await {
                warn!(?err, device_id, protocol, "Failed to insert driving event");
            }
        }

        if frame.is_valid {
            if services.geofence_detector.needs_refresh().await {
                if let Ok(geofences) = database.load_geofences().await {
                    services.geofence_detector.load_geofences(geofences).await;
                }
            }
            if let Some(vid) = vehicle_id {
                let timestamp = chrono::DateTime::<chrono::Utc>::from_naive_utc_and_offset(
                    frame.recorded_at, chrono::Utc,
                );
                let geofence_events = services.geofence_detector.process_frame(
                    device_id, vid, company_id, frame.latitude, frame.longitude,
                    Some(frame.speed_kph), timestamp,
                ).await;
                for gf_event in geofence_events {
                    let duration = if gf_event.event_type == crate::services::geofence_detector::GeofenceEventType::Exit {
                        services.geofence_detector.get_duration_inside(device_id, gf_event.geofence_id).await
                            .map(|d| d as i32)
                    } else {
                        None
                    };
                    if let Err(err) = database.insert_geofence_event(&gf_event, duration).await {
                        warn!(?err, device_id, protocol, "Failed to insert geofence event");
                    }
                }
            }
        }
    }

    Ok(())
}

/// Identical purpose to `route_noron_frame`, but tagged with `protocol = "teltonika"`
/// so monitoring / metrics / debug logs can tell which family produced the data.
/// The downstream services (V7 accident detection, stop detector, geofences…)
/// don't care about the protocol — they only read canonical HhFrame fields.
async fn route_teltonika_frame(
    device_uid: &str,
    mut frame: telemetry::model::HhFrame,
    database: Arc<dyn TelemetryStore>,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
) -> Result<()> {
    let protocol = "teltonika";

    let device_id_opt = database.get_device_id(device_uid).await?;

    if frame.latitude.abs() < 1.0 && frame.longitude.abs() < 2.0 {
        if let Some(device_id) = device_id_opt {
            if let Some(last_pos) = database.get_last_position(device_id).await? {
                frame.latitude = last_pos.latitude;
                frame.longitude = last_pos.longitude;
                frame.is_valid = false;
                debug!(device_uid, "Teltonika no-fix: using last known position");
            } else {
                debug!(device_uid, "Teltonika frame skipped: no GPS fix and no previous position");
                return Ok(());
            }
        } else {
            return Ok(());
        }
    }

    if !(-180.0..=180.0).contains(&frame.longitude) || !(-90.0..=90.0).contains(&frame.latitude) {
        warn!(device_uid, lat = frame.latitude, lon = frame.longitude, "Teltonika frame skipped: coords out of range");
        return Ok(());
    }

    let tomorrow = chrono::Utc::now().date_naive() + Duration::days(1);
    if frame.recorded_at.date() >= tomorrow {
        warn!(device_uid, date = %frame.recorded_at, "Teltonika frame skipped: date in future");
        return Ok(());
    }

    if let Some(device_id) = device_id_opt {
        let validation = services.gps_validator.validate(device_id, &frame).await;
        if !validation.should_store() {
            if let crate::services::gps_validator::ValidationResult::Invalid { reason } = &validation {
                debug!(device_uid, reason = %reason, "Teltonika frame REJECTED by GPS validator");
            }
            return Ok(());
        }

        let stabilized = services.gps_stabilizer.stabilize(device_id, &frame).await;
        if stabilized.was_stabilized {
            frame.latitude = stabilized.latitude;
            frame.longitude = stabilized.longitude;
        }

        if frame.ignition_on && frame.speed_kph > 0.0 {
            let result = services.speed_filter.filter(device_id, frame.speed_kph, frame.recorded_at).await;
            if result.was_filtered {
                frame.speed_kph = result.corrected_speed;
            }
        } else {
            let _ = services.speed_filter.filter(device_id, 0.0, frame.recorded_at).await;
        }
    }

    if frame.is_valid {
        frame.address = services.geocoding.reverse_geocode(frame.latitude, frame.longitude).await;
    }

    let event_key = format!(
        "{}:{}:{:.6}:{:.6}:{:.1}:{}",
        device_uid, frame.recorded_at, frame.latitude, frame.longitude, frame.speed_kph, frame.send_flag
    );

    let (vehicle_id, company_id, _firmware_version) = if let Some(device_id) = device_id_opt {
        database.get_device_vehicle_info(device_id).await?
    } else {
        (None, 1, None)
    };

    // Only push to the live map (Redis pub-sub + RabbitMQ) if the frame is recent.
    // Buffered backlog (old recorded_at, flushed on reconnect) is still persisted to
    // history via db_future, but must NOT animate the real-time map — otherwise the
    // vehicle "teleports" through hours/days of past positions. Mirrors the NEMS handler.
    let frame_age_secs = (chrono::Utc::now().naive_utc() - frame.recorded_at).num_seconds();
    let is_recent_frame = frame_age_secs < 300; // 5 minutes

    let db_future = database.ingest_hh_frame(device_uid, protocol, &frame, &event_key);
    let redis_future = async {
        if is_recent_frame {
            if let Some(ref redis) = redis_cache {
                if let Err(err) = redis.cache_position(device_uid, vehicle_id, company_id, &frame).await {
                    warn!(?err, "Failed to cache Teltonika position in Redis");
                }
            }
        }
    };
    let rabbitmq_future = async {
        if is_recent_frame {
            if let Some(ref pub_ref) = publisher {
                if let Err(err) = pub_ref.publish_hh_frame(device_uid, protocol, &frame).await {
                    warn!(?err, "Failed to publish Teltonika telemetry event");
                }
            }
        }
    };
    let (db_result, _, _) = tokio::join!(db_future, redis_future, rabbitmq_future);
    db_result?;

    info!(
        device_uid,
        speed_kph = frame.speed_kph,
        lat = frame.latitude,
        lon = frame.longitude,
        ignition = frame.ignition_on,
        is_valid = frame.is_valid,
        protocol,
        "Teltonika position ingested"
    );

    if let Some(device_id) = device_id_opt {
        if let Some(completed_stop) = services.stop_detector.process_frame(device_id, &frame).await {
            if let Err(err) = database.insert_vehicle_stop(&completed_stop, vehicle_id, company_id).await {
                warn!(?err, device_id, "Failed to insert Teltonika vehicle stop");
            }
        }

        if let Some(completed_trip) = services.trip_detector.process_frame(
            device_id, vehicle_id, company_id, &frame,
        ).await {
            if let Err(err) = database.insert_trip(&completed_trip).await {
                warn!(?err, device_id, "Failed to insert Teltonika trip");
            }
        }

        let driving_events = services.driving_events_detector.process_frame(
            device_id, vehicle_id, company_id, &frame,
        ).await;
        for event in driving_events {
            if let Err(err) = database.insert_driving_event(&event).await {
                warn!(?err, device_id, "Failed to insert Teltonika driving event");
            }
        }

        if frame.is_valid {
            if services.geofence_detector.needs_refresh().await {
                if let Ok(geofences) = database.load_geofences().await {
                    services.geofence_detector.load_geofences(geofences).await;
                }
            }
            if let Some(vid) = vehicle_id {
                let timestamp = chrono::DateTime::<chrono::Utc>::from_naive_utc_and_offset(
                    frame.recorded_at, chrono::Utc,
                );
                let geofence_events = services.geofence_detector.process_frame(
                    device_id, vid, company_id, frame.latitude, frame.longitude,
                    Some(frame.speed_kph), timestamp,
                ).await;
                for gf_event in geofence_events {
                    let duration = if gf_event.event_type == crate::services::geofence_detector::GeofenceEventType::Exit {
                        services.geofence_detector.get_duration_inside(device_id, gf_event.geofence_id).await
                            .map(|d| d as i32)
                    } else {
                        None
                    };
                    if let Err(err) = database.insert_geofence_event(&gf_event, duration).await {
                        warn!(?err, device_id, "Failed to insert Teltonika geofence event");
                    }
                }
            }
        }
    }

    Ok(())
}

async fn route_noron_frame(
    device_uid: &str,
    mut frame: telemetry::model::HhFrame,
    database: Arc<dyn TelemetryStore>,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
) -> Result<()> {
    let protocol = "noron";

    // Get device_id
    let device_id_opt = database.get_device_id(device_uid).await?;

    // Log device mat if available
    if let Some(device_id) = device_id_opt {
        if let Ok(Some(mat)) = database.get_device_mat(device_id).await {
            info!(mat = %mat, protocol, "Noron trame reçue");
        }
    }

    // ── Validation: coordinates must be valid ──
    if frame.latitude.abs() < 1.0 && frame.longitude.abs() < 2.0 {
        if let Some(device_id) = device_id_opt {
            if let Some(last_pos) = database.get_last_position(device_id).await? {
                frame.latitude = last_pos.latitude;
                frame.longitude = last_pos.longitude;
                frame.is_valid = false;
                info!(device_uid, "Noron GPS no-fix: using last known position");
            } else {
                info!(device_uid, "Noron frame skipped: no GPS fix and no previous position");
                return Ok(());
            }
        } else {
            info!(device_uid, "Noron frame skipped: no GPS fix and device not registered");
            return Ok(());
        }
    }

    if frame.longitude < -180.0 || frame.longitude > 180.0 {
        warn!(device_uid, lon = frame.longitude, "Noron frame skipped: longitude out of range");
        return Ok(());
    }
    if frame.latitude < -90.0 || frame.latitude > 90.0 {
        warn!(device_uid, lat = frame.latitude, "Noron frame skipped: latitude out of range");
        return Ok(());
    }

    // ── Future date rejection ──
    let tomorrow = chrono::Utc::now().date_naive() + Duration::days(1);
    if frame.recorded_at.date() >= tomorrow {
        warn!(device_uid, date = %frame.recorded_at, "Noron frame skipped: date in future");
        return Ok(());
    }

    // ── GPS validation ──
    if let Some(device_id) = device_id_opt {
        let validation = services.gps_validator.validate(device_id, &frame).await;
        if !validation.should_store() {
            if let crate::services::gps_validator::ValidationResult::Invalid { reason } = &validation {
                warn!(device_uid, reason = %reason, "Noron frame REJECTED by GPS validator");
            }
            return Ok(());
        }
    }

    // ── Anti-drift stabilization ──
    if let Some(device_id) = device_id_opt {
        let stabilized = services.gps_stabilizer.stabilize(device_id, &frame).await;
        if stabilized.was_stabilized {
            frame.latitude = stabilized.latitude;
            frame.longitude = stabilized.longitude;
        }
    }

    // ── Geocoding ──
    if frame.is_valid {
        frame.address = services.geocoding.reverse_geocode(frame.latitude, frame.longitude).await;
    }

    // ── Speed filter ──
    if let Some(device_id) = device_id_opt {
        if frame.ignition_on && frame.speed_kph > 0.0 {
            let result = services.speed_filter.filter(device_id, frame.speed_kph, frame.recorded_at).await;
            if result.was_filtered {
                frame.speed_kph = result.corrected_speed;
            }
        } else {
            let _ = services.speed_filter.filter(device_id, 0.0, frame.recorded_at).await;
        }
    }

    let event_key = format!(
        "{}:{}:{:.6}:{:.6}:{:.1}:{}",
        device_uid, frame.recorded_at, frame.latitude, frame.longitude, frame.speed_kph, frame.send_flag
    );

    // Get vehicle and company info
    let (vehicle_id, company_id, _firmware_version) = if let Some(device_id) = device_id_opt {
        database.get_device_vehicle_info(device_id).await?
    } else {
        (None, 1, None)
    };

    // ── PARALLEL: DB (always) + Redis/RabbitMQ (live, only if recent) ──
    // Backlog (old recorded_at) is persisted to history but must not animate the live map.
    let frame_age_secs = (chrono::Utc::now().naive_utc() - frame.recorded_at).num_seconds();
    let is_recent_frame = frame_age_secs < 300; // 5 minutes
    let db_future = database.ingest_hh_frame(device_uid, protocol, &frame, &event_key);
    let redis_future = async {
        if is_recent_frame {
            if let Some(ref redis) = redis_cache {
                if let Err(err) = redis.cache_position(device_uid, vehicle_id, company_id, &frame).await {
                    warn!(?err, "Failed to cache Noron position in Redis");
                }
            }
        }
    };
    let rabbitmq_future = async {
        if is_recent_frame {
            if let Some(ref pub_ref) = publisher {
                if let Err(err) = pub_ref.publish_hh_frame(device_uid, protocol, &frame).await {
                    warn!(?err, "Failed to publish Noron telemetry event");
                }
            }
        }
    };
    let (db_result, _, _) = tokio::join!(db_future, redis_future, rabbitmq_future);
    db_result?;

    info!(
        device_uid,
        speed_kph = frame.speed_kph,
        lat = frame.latitude,
        lon = frame.longitude,
        ignition = frame.ignition_on,
        is_valid = frame.is_valid,
        protocol,
        "Noron position ingested successfully"
    );

    // ── Services: stop detection, trip detection, driving events ──
    if let Some(device_id) = device_id_opt {
        if let Some(completed_stop) = services.stop_detector.process_frame(device_id, &frame).await {
            if let Err(err) = database.insert_vehicle_stop(&completed_stop, vehicle_id, company_id).await {
                warn!(?err, device_id, "Failed to insert Noron vehicle stop");
            }
        }

        if let Some(completed_trip) = services.trip_detector.process_frame(
            device_id, vehicle_id, company_id, &frame,
        ).await {
            if let Err(err) = database.insert_trip(&completed_trip).await {
                warn!(?err, device_id, "Failed to insert Noron trip");
            }
        }

        let driving_events = services.driving_events_detector.process_frame(
            device_id, vehicle_id, company_id, &frame,
        ).await;
        for event in driving_events {
            if let Err(err) = database.insert_driving_event(&event).await {
                warn!(?err, device_id, "Failed to insert Noron driving event");
            }
        }

        // Geofence detection
        if frame.is_valid {
            if services.geofence_detector.needs_refresh().await {
                if let Ok(geofences) = database.load_geofences().await {
                    services.geofence_detector.load_geofences(geofences).await;
                }
            }
            if let Some(vid) = vehicle_id {
                let timestamp = chrono::DateTime::<chrono::Utc>::from_naive_utc_and_offset(
                    frame.recorded_at, chrono::Utc,
                );
                let geofence_events = services.geofence_detector.process_frame(
                    device_id, vid, company_id, frame.latitude, frame.longitude,
                    Some(frame.speed_kph), timestamp,
                ).await;
                for gf_event in geofence_events {
                    let duration = if gf_event.event_type == crate::services::geofence_detector::GeofenceEventType::Exit {
                        services.geofence_detector.get_duration_inside(device_id, gf_event.geofence_id).await
                            .map(|d| d as i32)
                    } else {
                        None
                    };
                    if let Err(err) = database.insert_geofence_event(&gf_event, duration).await {
                        warn!(?err, device_id, "Failed to insert Noron geofence event");
                    }
                }
            }
        }
    }

    Ok(())
}

/// Extract frames from payload, handling binary data that may contain embedded newlines (0x0A)
/// 
/// AA frames can have 0x0A in their binary hex data which gets interpreted as newline.
/// This function reconstructs fragmented frames by:
/// 1. Splitting by newlines
/// 2. Merging lines that don't start with AA/HH with the previous AA/HH line
/// 3. Validating frame lengths (AA23=74 chars, AA33=78+ chars)
fn extract_frames_smart(payload: &str) -> Vec<String> {
    // Remove AAAA/HHHH markers
    let cleaned = payload
        .replace("AAAA", "\n")
        .replace("HHHH", "\n");
    
    // Split by newlines
    let lines: Vec<&str> = cleaned
        .split(|c| c == '\r' || c == '\n')
        .map(|s| s.trim())
        .filter(|s| !s.is_empty())
        .collect();
    
    let mut frames: Vec<String> = Vec::new();
    let mut current_frame: Option<String> = None;
    
    for line in lines {
        // Check if line is a valid frame start:
        // - Starts with AA/HH (standard frame)
        // - Contains " AA" or " HH" (frame with MAT prefix like "NR08G0658 AA00...")
        let is_frame_start = line.starts_with("AA") || line.starts_with("HH") ||
            line.contains(" AA") || line.contains(" HH");
        
        if is_frame_start {
            // Save previous frame if complete
            if let Some(frame) = current_frame.take() {
                if is_frame_complete(&frame) {
                    frames.push(frame);
                } else {
                    tracing::debug!(frame_len = frame.len(), frame_preview = %&frame[..frame.len().min(20)], "Discarding incomplete frame");
                }
            }
            // Start new frame
            current_frame = Some(line.to_string());
        } else if let Some(ref mut frame) = current_frame {
            // Append fragment to current frame (handles 0x0A in binary data)
            frame.push_str(line);
            tracing::debug!(appended = line, new_len = frame.len(), "Reconstructing fragmented frame");
        }
        // Ignore orphan lines that don't belong to any frame
    }
    
    // Don't forget the last frame
    if let Some(frame) = current_frame {
        if is_frame_complete(&frame) {
            frames.push(frame);
        }
    }
    
    frames
}

/// Extract MAT prefix from frame if present (format: "NR08G0664 AA23...")
/// Returns (Some(mat), remaining_frame) if prefix found, (None, original_frame) otherwise
fn extract_mat_prefix(frame: &str) -> (Option<String>, &str) {
    // Look for space followed by AA or HH
    if let Some(pos) = frame.find(" AA").or_else(|| frame.find(" HH")) {
        let prefix = frame[..pos].trim();
        // MAT should be alphanumeric, typically 9-10 chars like "NR08G0664"
        if !prefix.is_empty() && prefix.len() <= 15 && prefix.chars().all(|c| c.is_alphanumeric()) {
            return (Some(prefix.to_string()), &frame[pos + 1..]);
        }
    }
    (None, frame)
}

/// Check if a frame has the expected minimum length based on its header
fn is_frame_complete(frame: &str) -> bool {
    // Handle frames with MAT prefix (e.g., "NR08G0658 AA00...")
    // Extract the actual frame part after MAT prefix
    let actual_frame = if let Some(pos) = frame.find(" AA").or_else(|| frame.find(" HH")) {
        &frame[pos + 1..]
    } else {
        frame
    };
    
    // HH01/AA01 info frames - variable length, must contain IMEI
    if actual_frame.starts_with("HH01") || actual_frame.starts_with("AA01") {
        return frame.contains("IMEI:");
    }
    // AA00/HH00 connect frames - variable length, must contain IMEI
    if actual_frame.starts_with("AA00") || actual_frame.starts_with("HH00") {
        return frame.contains("IMEI:");
    }
    // HH data frames (HH13, etc.) - minimum 74 chars
    if actual_frame.starts_with("HH") {
        return actual_frame.len() >= 74;
    }
    // AA system frames (AA02, AA03, AA06, AA07) - any length OK
    if actual_frame.starts_with("AA02") || actual_frame.starts_with("AA03") || 
       actual_frame.starts_with("AA06") || actual_frame.starts_with("AA07") {
        return true;
    }
    // AA23 history frames - minimum 74 chars
    if actual_frame.starts_with("AA23") {
        return actual_frame.len() >= 74;
    }
    // AA33 realtime frames - minimum 78 chars (can be longer with FMS)
    if actual_frame.starts_with("AA33") {
        return actual_frame.len() >= 78;
    }
    // Generic AA frames - minimum 74 chars
    if actual_frame.starts_with("AA") {
        return actual_frame.len() >= 70;
    }
    false
}

/// Check if payload starts with a valid protocol header (HH or AA) or contains one (for prefixed frames)
fn starts_with_valid_header(s: &str) -> bool {
    s.starts_with("HH") || s.starts_with("AA") || s.contains(" AA") || s.contains(" HH")
}


#[cfg(test)]
mod tests {
    use super::*;
    use async_trait::async_trait;
    use std::sync::Mutex as StdMutex;

    use crate::services::fuel_tracker::FuelEvent;
    use crate::services::stop_detector::CompletedStop;
    use crate::telemetry::model::{HhFrame, HhInfoFrame};

    const HH01_FRAME: &str = "HH011.0.103R10, ICC:8921602050440128136F, IMEI:861001002935274";
    const HH13_FRAME: &str = "HH130094F80228D3D20099CF4F00000A2926FC04FBE780FB00000000010000000016630B17";

    struct MockStore {
        imei: String,
        info_calls: StdMutex<Vec<String>>,
        frame_calls: StdMutex<Vec<String>>,
    }

    impl MockStore {
        fn new(imei: &str) -> Self {
            Self {
                imei: imei.to_string(),
                info_calls: StdMutex::new(Vec::new()),
                frame_calls: StdMutex::new(Vec::new()),
            }
        }
    }

    #[async_trait]
    impl TelemetryStore for MockStore {
        async fn ingest_info_frame(&self, protocol_type: &str, info: &HhInfoFrame) -> anyhow::Result<String> {
            self.info_calls
                .lock()
                .unwrap()
                .push(format!("{}:{}", protocol_type, info.firmware_version));
            Ok(self.imei.clone())
        }

        async fn ingest_hh_frame(
            &self,
            device_uid: &str,
            protocol_type: &str,
            frame: &HhFrame,
            _event_key: &str,
        ) -> anyhow::Result<()> {
            self.frame_calls
                .lock()
                .unwrap()
                .push(format!("{}:{}:{}", protocol_type, device_uid, frame.recorded_at));
            Ok(())
        }

        async fn get_device_id(&self, _imei: &str) -> anyhow::Result<Option<i32>> {
            Ok(Some(1)) // Mock device_id
        }

        async fn get_device_mat(&self, _device_id: i32) -> anyhow::Result<Option<String>> {
            Ok(Some("NR08G0001".to_string())) // Mock mat
        }

        async fn insert_vehicle_stop(&self, _stop: &CompletedStop, _vehicle_id: Option<i32>, _company_id: i32) -> anyhow::Result<i64> {
            Ok(1) // Mock stop_id
        }

        async fn insert_fuel_record(&self, _event: &FuelEvent, _vehicle_id: Option<i32>, _company_id: i32) -> anyhow::Result<i64> {
            Ok(1) // Mock record_id
        }

        async fn get_device_vehicle_info(&self, _device_id: i32) -> anyhow::Result<(Option<i32>, i32, Option<String>)> {
            Ok((Some(1), 1, Some("L".to_string()))) // Mock vehicle_id, company_id, firmware_version
        }

        async fn get_fuel_config(&self, _device_id: i32) -> anyhow::Result<(String, Option<i32>)> {
            Ok(("raw_255".to_string(), Some(60))) // Default: raw_255 mode, 60L tank
        }

        async fn load_geofences(&self) -> anyhow::Result<Vec<crate::services::geofence_detector::Geofence>> {
            Ok(Vec::new()) // No geofences for tests
        }

        async fn insert_geofence_event(&self, _event: &crate::services::geofence_detector::GeofenceEvent, _duration_seconds: Option<i32>) -> anyhow::Result<i32> {
            Ok(1) // Mock event_id
        }

        async fn insert_trip(&self, _trip: &crate::services::trip_detector::CompletedTrip) -> anyhow::Result<i64> {
            Ok(1) // Mock trip_id
        }

        async fn insert_driving_event(&self, _event: &crate::services::driving_events::DrivingEventRecord) -> anyhow::Result<i64> {
            Ok(1) // Mock driving_event_id
        }

        async fn get_last_position(&self, _device_id: i32) -> anyhow::Result<Option<crate::db::LastKnownPosition>> {
            Ok(None) // No last position for tests
        }

        async fn get_device_uid_by_mat(&self, _mat: &str) -> anyhow::Result<Option<String>> {
            Ok(None) // No MAT lookup for tests
        }

        async fn get_last_fuel_record(&self, _device_id: i32) -> anyhow::Result<Option<(i16, u32, chrono::DateTime<chrono::Utc>)>> {
            Ok(None) // No fuel history for tests
        }

        async fn insert_device_event(&self, _event: &crate::services::device_event::DeviceEventRecord) -> anyhow::Result<i64> {
            Ok(1) // Mock device_event_id
        }

        async fn get_immobilization_state(&self, _device_id: i32) -> anyhow::Result<(bool, String)> {
            Ok((false, "AJ+GO#9999\n".to_string())) // Default: not immobilized, standard GO command
        }

        async fn get_pending_command(&self, _device_id: i32) -> anyhow::Result<Option<(i64, String)>> {
            Ok(None) // No pending commands in tests
        }

        async fn update_command_status(&self, _command_id: i64, _status: &str, _error: Option<&str>) -> anyhow::Result<()> {
            Ok(())
        }

        async fn log_auto_recovery(&self, _device_id: i32, _command_text: &str, _flags_hex: &str) -> anyhow::Result<()> {
            Ok(())
        }

        async fn update_device_last_communication(&self, _device_id: i32) -> anyhow::Result<()> {
            Ok(())
        }

        async fn log_frame_debug(
            &self,
            _device_id: Option<i32>,
            _device_uid: Option<&str>,
            _mat: Option<&str>,
            _frame_type: &str,
            _flags_hex: Option<&str>,
            _flags_raw: Option<i16>,
            _raw_frame: &str,
            _reason: &str,
            _peer: Option<&str>,
        ) -> anyhow::Result<()> {
            Ok(())
        }
    }

    struct MockPublisher {
        events: StdMutex<Vec<(String, String)>>,
    }

    impl MockPublisher {
        fn new() -> Self {
            Self {
                events: StdMutex::new(Vec::new()),
            }
        }
    }

    #[async_trait]
    impl TelemetryEventPublisher for MockPublisher {
        async fn publish_hh_frame(&self, device_uid: &str, protocol: &str, _frame: &HhFrame) -> anyhow::Result<()> {
            self.events
                .lock()
                .unwrap()
                .push((protocol.to_string(), device_uid.to_string()));
            Ok(())
        }

        async fn publish_device_event(&self, _event: &crate::services::device_event::DeviceEventRecord) -> anyhow::Result<()> {
            Ok(())
        }
    }

    #[tokio::test]
    async fn route_payload_handles_info_and_data_frames() {
        let protocol = "gps_type_1";
        let store = Arc::new(MockStore::new("861001002935274"));
        let publisher = Arc::new(MockPublisher::new());
        let connection_map: ConnectionMap = Arc::new(Mutex::new(HashMap::new()));
        let services = Arc::new(TelemetryServices {
            stop_detector: StopDetector::new(),
            fuel_tracker: FuelTracker::new(),
            geocoding: GeocodingService::new(None),
            geofence_detector: GeofenceDetector::new(),
            gps_stabilizer: GpsStabilizer::new(),
            gps_validator: GpsValidator::new(),
            trip_detector: TripDetector::new(),
            driving_events_detector: DrivingEventsDetector::new(),
            speed_filter: SpeedFilter::new(),
            device_event_cooldown: Mutex::new(HashMap::new()),
        });
        let peer = "127.0.0.1:1234";

        // Send info frame
        route_payload(
            protocol,
            HH01_FRAME.as_bytes(),
            Arc::clone(&store) as Arc<dyn TelemetryStore>,
            Some(peer),
            Arc::clone(&connection_map),
            Some(Arc::clone(&publisher) as Arc<dyn TelemetryEventPublisher>),
            Arc::clone(&services),
            None,
        )
        .await
        .expect("info frame should succeed");

        {
            let map = connection_map.lock().await;
            assert_eq!(map.get(peer), Some(&store.imei));
        }
        assert_eq!(store.info_calls.lock().unwrap().len(), 1);
        assert!(store.frame_calls.lock().unwrap().is_empty());
        assert!(publisher.events.lock().unwrap().is_empty());

        // Send telemetry frame
        route_payload(
            protocol,
            HH13_FRAME.as_bytes(),
            store.clone(),
            Some(peer),
            connection_map.clone(),
            Some(publisher.clone()),
            Arc::clone(&services),
            None,
        )
        .await
        .expect("data frame should succeed");

        assert_eq!(store.frame_calls.lock().unwrap().len(), 1);
        let events = publisher.events.lock().unwrap();
        assert_eq!(events.len(), 1);
        assert_eq!(events[0].1, store.imei);
    }

    #[tokio::test]
    async fn route_payload_ingests_sample_hh13_frame() {
        let protocol = "gps_type_1";
        let store = Arc::new(MockStore::new("861001002935274"));
        let connection_map: ConnectionMap = Arc::new(Mutex::new(HashMap::new()));
        let services = Arc::new(TelemetryServices {
            stop_detector: StopDetector::new(),
            fuel_tracker: FuelTracker::new(),
            geocoding: GeocodingService::new(None),
            geofence_detector: GeofenceDetector::new(),
            gps_stabilizer: GpsStabilizer::new(),
            gps_validator: GpsValidator::new(),
            trip_detector: TripDetector::new(),
            driving_events_detector: DrivingEventsDetector::new(),
            speed_filter: SpeedFilter::new(),
            device_event_cooldown: Mutex::new(HashMap::new()),
        });
        let peer = "10.0.0.5:5555";

        {
            let mut map = connection_map.lock().await;
            map.insert(peer.to_string(), store.imei.clone());
        }

        route_payload(
            protocol,
            HH13_FRAME.as_bytes(),
            Arc::clone(&store) as Arc<dyn TelemetryStore>,
            Some(peer),
            Arc::clone(&connection_map),
            None,
            Arc::clone(&services),
            None,
        )
        .await
        .expect("sample frame should ingest");

        let calls = store.frame_calls.lock().unwrap();
        assert_eq!(calls.len(), 1);
        assert_eq!(
            calls[0],
            format!("{}:{}:{}", protocol, store.imei, "2015-05-28 10:35:36")
        );
    }
}
