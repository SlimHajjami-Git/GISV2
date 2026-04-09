use std::{collections::HashMap, sync::Arc};

use anyhow::{anyhow, Context, Result};
use chrono::Duration;
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    net::{TcpListener, TcpStream, UdpSocket},
    signal,
    sync::Mutex,
};
use tracing::{error, info, warn};

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
}

pub async fn run_listeners(
    config: &AppConfig,
    database: Arc<dyn TelemetryStore>,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    redis_cache: Option<Arc<RedisCache>>,
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
                handles.push(tokio::spawn(async move {
                    if let Err(err) = run_tcp_listener(cfg, db, mapping, publisher_clone, services_clone, redis_clone).await {
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
        tokio::spawn(async move {
            if let Err(err) = handle_tcp_connection(stream, cfg_clone, db, map_clone, publisher_clone, services_clone, redis_clone).await {
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
    mut stream: TcpStream,
    cfg: Arc<ListenerConfig>,
    database: Arc<dyn TelemetryStore>,
    connection_map: ConnectionMap,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
    redis_cache: Option<Arc<RedisCache>>,
) -> Result<()> {
    let peer = stream.peer_addr().ok().map(|addr| addr.to_string());

    let mut buffer = vec![0u8; 8192];

    // Rate limit auto-recovery: track last AJ+GO send time per device_id
    // to avoid spamming the same device every frame when bit5 stays 0
    let mut last_auto_recovery: HashMap<i32, std::time::Instant> = HashMap::new();
    const AUTO_RECOVERY_COOLDOWN_SECS: u64 = 300; // 5 minutes

    // Noron protocol uses binary framing — handle separately from ASCII protocols
    let is_noron = cfg.protocol == "noron";

    loop {
        let read = stream.read(&mut buffer).await?;
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
                        if let Err(e) = stream.write_all(&telemetry::noron::HANDSHAKE_ACK).await {
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
                    if let Err(e) = stream.write_all(b"AA06").await {
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
                                    // Only check bit5 if GPS is valid (bit6=1).
                                    // flags=0x00 means corrupted/empty data, NOT real immobilization.
                                    if (flags & 0x40) != 0 && (flags & 0x20) == 0 {
                                        // ── Log the immobilization frame to frame_debug_log ──
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

                                        if let Some(ref imei) = imei_opt {
                                            if let Some(device_id) = device_id_opt {
                                                // Rate limit: skip if we already sent AJ+GO to this device recently
                                                let now = std::time::Instant::now();
                                                let cooldown_elapsed = match last_auto_recovery.get(&device_id) {
                                                    Some(last) => now.duration_since(*last).as_secs() >= AUTO_RECOVERY_COOLDOWN_SECS,
                                                    None => true, // Never sent before
                                                };

                                                if !cooldown_elapsed {
                                                    // Skip — already sent recently
                                                    break;
                                                }

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
                                                        if let Err(e) = stream.write_all(command_go.as_bytes()).await {
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
                if let Some(ref p) = peer {
                    let imei_opt = {
                        let map = connection_map.lock().await;
                        map.get(p).cloned()
                    };
                    if let Some(ref imei) = imei_opt {
                        if let Ok(Some(device_id)) = database.get_device_id(imei).await {
                            if let Ok(Some((cmd_id, cmd_text))) = database.get_pending_command(device_id).await {
                                let peer_str = peer.as_deref().unwrap_or("unknown");
                                info!(peer = peer_str, imei = %imei, cmd_id, cmd = %cmd_text, "Sending pending command from DB");
                                if let Err(e) = stream.write_all(cmd_text.as_bytes()).await {
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

    }

    // Connection closed — clean up
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

                if !is_restart && !is_gsm_reset {
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

                let event_type = if is_gsm_reset {
                    crate::services::device_event::DeviceEventType::GsmReset
                } else {
                    crate::services::device_event::classify_restart(
                        offline_duration_secs,
                        was_moving,
                        ignition_was_on,
                    )
                };

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

                // Always insert for audit
                match database.insert_device_event(&record).await {
                    Ok(id) => info!(
                        device_id, event_id = id, event_type = event_type.as_str(),
                        offline_secs = ?offline_duration_secs,
                        "Device event recorded"
                    ),
                    Err(e) => warn!(device_id, error = %e, "Failed to insert device event"),
                }

                // Publish to RabbitMQ only if offline >= 6h (power cut notification)
                if let Some(duration) = offline_duration_secs {
                    if duration >= crate::services::device_event::POWER_CUT_MIN_OFFLINE_SECS {
                        if let Some(pub_ref) = &publisher {
                            if let Err(e) = pub_ref.publish_device_event(&record).await {
                                warn!(device_id, error = %e, "Failed to publish device event to RabbitMQ");
                            }
                        }
                    }
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
                                        "Frame throttled for DB, refreshing Redis only"
                                    );
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

    // ── PARALLEL: DB + Redis + RabbitMQ ──
    let db_future = database.ingest_hh_frame(device_uid, protocol, &frame, &event_key);
    let redis_future = async {
        if let Some(ref redis) = redis_cache {
            if let Err(err) = redis.cache_position(device_uid, vehicle_id, company_id, &frame).await {
                warn!(?err, "Failed to cache Noron position in Redis");
            }
        }
    };
    let rabbitmq_future = async {
        if let Some(ref pub_ref) = publisher {
            if let Err(err) = pub_ref.publish_hh_frame(device_uid, protocol, &frame).await {
                warn!(?err, "Failed to publish Noron telemetry event");
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
