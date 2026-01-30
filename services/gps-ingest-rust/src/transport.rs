use std::{collections::HashMap, sync::Arc};

use anyhow::{anyhow, Context, Result};
use chrono::Duration;
use tokio::{
    io::AsyncReadExt,
    net::{TcpListener, TcpStream},
    signal,
    sync::Mutex,
};
use tracing::{error, info, warn};

use crate::{
    config::{AppConfig, ListenerConfig, TransportKind},
    ports::{TelemetryEventPublisher, TelemetryStore},
    services::{
        driving_events::DrivingEventsDetector,
        fuel_tracker::FuelTracker,
        geofence_detector::GeofenceDetector,
        geocoding::GeocodingService,
        gps_stabilizer::GpsStabilizer,
        gps_validator::GpsValidator,
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
}

pub async fn run_listeners(
    config: &AppConfig,
    database: Arc<dyn TelemetryStore>,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
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
    });
    info!("Telemetry services initialized (StopDetector, FuelTracker, Geocoding, GeofenceDetector, GpsStabilizer, GpsValidator, TripDetector, DrivingEventsDetector)");

    let mut handles = Vec::new();
    for listener in &config.listeners {
        match listener.transport {
            TransportKind::Tcp => {
                let cfg = listener.clone();
                let db = Arc::clone(&database);
                let mapping: ConnectionMap = Arc::new(Mutex::new(HashMap::new()));
                let publisher_clone = publisher.clone();
                let services_clone = Arc::clone(&services);
                handles.push(tokio::spawn(async move {
                    if let Err(err) = run_tcp_listener(cfg, db, mapping, publisher_clone, services_clone).await {
                        error!(?err, "TCP listener terminated unexpectedly");
                    }
                }));
            }
            TransportKind::Udp => {
                warn!(port = listener.port, "UDP transport not implemented yet");
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
        tokio::spawn(async move {
            if let Err(err) = handle_tcp_connection(stream, cfg_clone, db, map_clone, publisher_clone, services_clone).await {
                error!(?err, "TCP connection handler exited with error");
            }
        });
        info!(protocol = %cfg.protocol, peer = %peer_addr, "Accepted TCP connection");
    }
}

async fn handle_tcp_connection(
    mut stream: TcpStream,
    cfg: Arc<ListenerConfig>,
    database: Arc<dyn TelemetryStore>,
    connection_map: ConnectionMap,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
    services: Arc<TelemetryServices>,
) -> Result<()> {
    let mut buffer = vec![0u8; 4096];
    let peer = stream.peer_addr().ok().map(|addr| addr.to_string());
    loop {
        let read = stream.read(&mut buffer).await?;
        if read == 0 {
            break;
        }

        let payload = &buffer[..read];
        let hex_dump = hex::encode(payload);
        info!(protocol = %cfg.protocol, port = cfg.port, size = read, payload = %hex_dump, "Received raw payload");

        if let Err(err) = route_payload(
            &cfg.protocol,
            payload,
            Arc::clone(&database),
            peer.as_deref(),
            Arc::clone(&connection_map),
            publisher.clone(),
            Arc::clone(&services),
        )
        .await
        {
            warn!(?err, "Failed to process payload");
        }
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
) -> Result<()> {
    let ascii_payload = String::from_utf8(raw_payload.to_vec()).context("payload is not UTF-8")?;
    
    // Split payload into individual frames (separated by newlines)
    let all_lines: Vec<&str> = ascii_payload
        .split(|c| c == '\r' || c == '\n')
        .map(|s| s.trim())
        .filter(|s| !s.is_empty() && *s != "AAAA" && *s != "HHHH")
        .collect();

    // Log rejected frames for debugging
    for line in &all_lines {
        if !starts_with_valid_header(line) {
            warn!(
                rejected_frame = %line,
                frame_len = line.len(),
                "Frame rejected: does not start with valid HH/AA header"
            );
        }
    }

    let frames: Vec<&str> = all_lines
        .into_iter()
        .filter(|s| starts_with_valid_header(s))
        .collect();

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

    for frame_str in frames {
        if let Err(err) = process_single_frame(
            protocol,
            frame_str,
            Arc::clone(&database),
            peer_addr,
            Arc::clone(&connection_map),
            publisher.clone(),
            Arc::clone(&services),
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
) -> Result<()> {
    match protocol {
        "gps_type_1" => {
            // Check for system frames (AA02/AA03/AA07) - just log and skip
            if telemetry::hh::is_system_frame(frame_str) {
                info!(protocol, frame = %frame_str, "Received system frame (ignored)");
                return Ok(());
            }

            // Check for info/connect frames (AA00/AA01/HH00/HH01) or frames with prefix like "NR08G0663 AA00..."
            let is_info_frame = frame_str.starts_with("HH01") || frame_str.starts_with("AA01") ||
                frame_str.starts_with("HH00") || frame_str.starts_with("AA00") ||
                frame_str.contains("AA00") || frame_str.contains("HH00");

            if is_info_frame {
                let info = telemetry::hh::parse_info_frame(frame_str)?;
                let imei = database.ingest_info_frame(protocol, &info).await?;
                if let Some(peer) = peer_addr {
                    let mut map = connection_map.lock().await;
                    map.insert(peer.to_string(), imei.clone());
                }
                info!(protocol, imei, "Registered device via info frame");
            } else {
                let mut frame = telemetry::hh::parse_frame(frame_str)?;
                let resolved_uid = if let Some(peer) = peer_addr {
                    let map = connection_map.lock().await;
                    map.get(peer)
                        .cloned()
                        .unwrap_or_else(|| {
                            warn!(%peer, "No learned IMEI for connection; using placeholder");
                            "UNKNOWN_DEVICE".to_string()
                        })
                } else {
                    warn!("No device UID available; using placeholder");
                    "UNKNOWN_DEVICE".to_string()
                };

                let event_key = format!(
                    "{}:{}:{:.6}:{:.6}",
                    resolved_uid,
                    frame.recorded_at,
                    frame.latitude,
                    frame.longitude
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
                // This replaces the old "duplicate immobile sample" logic
                const STOPPED_SPEED_THRESHOLD_KPH: f64 = 20.0;
                const STOPPED_MIN_INTERVAL_SECS: i64 = 30 * 60; // 30 minutes
                
                if let Some(device_id) = device_id_opt {
                    // Only apply throttling when ignition is OFF and speed is low
                    if !frame.ignition_on && frame.speed_kph < STOPPED_SPEED_THRESHOLD_KPH {
                        if let Some(last_position) = database.get_last_position(device_id).await? {
                            // Use current time (not frame timestamp) to handle historical frames correctly
                            // Historical frames have close timestamps but arrive much later
                            let now = chrono::Utc::now().naive_utc();
                            let seconds_since_last_stored =
                                (now - last_position.recorded_at).num_seconds();
                            
                            // Skip if less than 30 minutes have passed since last stored position (real time)
                            if seconds_since_last_stored.abs() < STOPPED_MIN_INTERVAL_SECS {
                                info!(
                                    device_id,
                                    seconds_since_last = seconds_since_last_stored,
                                    speed_kph = frame.speed_kph,
                                    min_interval_secs = STOPPED_MIN_INTERVAL_SECS,
                                    "Frame skipped: ignition-off throttling (30 min interval not reached)"
                                );
                                return Ok(());
                            }
                        }
                    }
                    // When ignition is ON or speed >= 20 km/h, all frames are stored (no throttling)
                }

                // GPS Stabilization: Prevent drift when vehicle is stopped
                // This mimics GISV1 behavior where coordinates are kept stable at anchor position
                if let Some(device_id) = device_id_opt {
                    let stabilized = services.gps_stabilizer.stabilize(device_id, &frame).await;
                    if stabilized.was_stabilized {
                        info!(
                            device_id,
                            original_lat = frame.latitude,
                            original_lon = frame.longitude,
                            stabilized_lat = stabilized.latitude,
                            stabilized_lon = stabilized.longitude,
                            drift_m = ?stabilized.drift_distance_meters,
                            "GPS drift filtered - using stable anchor position"
                        );
                        frame.latitude = stabilized.latitude;
                        frame.longitude = stabilized.longitude;
                    }
                }

                // GPS Validation: Check for aberrant points (jumps, speed incoherence)
                if let Some(device_id) = device_id_opt {
                    let validation = services.gps_validator.validate(device_id, &frame).await;
                    if !validation.should_store() {
                        if let crate::services::gps_validator::ValidationResult::Invalid { reason } = &validation {
                            warn!(
                                device_id,
                                imei = %resolved_uid,
                                lat = frame.latitude,
                                lon = frame.longitude,
                                reason = %reason,
                                "Frame REJECTED by GPS validator"
                            );
                        }
                        return Ok(());
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
                const LOCAL_TIME_OFFSET_MINUTES: i64 = 60; // GISV1 AddHours(1) => UTC+1 (Tunisia)
                
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
                
                // --- From AAP.cs lines 840-844 ---
                // Condition 1: SendFlag != 2 (skip heartbeat/duplicate frames)
                if frame.send_flag == 2 {
                    info!(
                        imei = %resolved_uid,
                        send_flag = frame.send_flag,
                        "Frame SKIPPED: SendFlag == 2 (same as GISV1)"
                    );
                    return Ok(());
                }
                
                // Condition 2: Date must be before tomorrow (not in future)
                let tomorrow = chrono::Utc::now().date_naive() + chrono::Duration::days(1);
                let frame_date = frame.recorded_at.date();
                if frame_date >= tomorrow {
                    warn!(
                        imei = %resolved_uid,
                        frame_date = %frame.recorded_at,
                        "Frame SKIPPED: Date in future (same as GISV1)"
                    );
                    return Ok(());
                }

                // --- From SaveDynData stored procedure ---
                // Condition 3: Angle must be 0-360
                if frame.heading_deg < 0.0 || frame.heading_deg > 360.0 {
                    info!(
                        imei = %resolved_uid,
                        heading = frame.heading_deg,
                        "Frame SKIPPED: Invalid angle (must be 0-360, same as GISV1 SaveDynData)"
                    );
                    return Ok(());
                }

                // Condition 4: Speed must be 0-300
                if frame.speed_kph < 0.0 || frame.speed_kph > 300.0 {
                    info!(
                        imei = %resolved_uid,
                        speed = frame.speed_kph,
                        "Frame SKIPPED: Invalid speed (must be 0-300, same as GISV1 SaveDynData)"
                    );
                    return Ok(());
                }

                // Condition 5: Coordinates must not be too close to 0 (invalid GPS)
                if frame.latitude.abs() < 0.05 && frame.longitude.abs() < 0.05 {
                    info!(
                        imei = %resolved_uid,
                        lat = frame.latitude,
                        lon = frame.longitude,
                        "Frame SKIPPED: Coordinates near 0,0 (same as GISV1 SaveDynData)"
                    );
                    return Ok(());
                }

                // Condition 6: If GPS invalid, coords must not be near 0
                if !frame.is_valid && frame.latitude.abs() < 0.3 && frame.longitude.abs() < 0.3 {
                    info!(
                        imei = %resolved_uid,
                        lat = frame.latitude,
                        lon = frame.longitude,
                        is_valid = frame.is_valid,
                        "Frame SKIPPED: Invalid GPS with coords near 0 (same as GISV1 SaveDynData)"
                    );
                    return Ok(());
                }

                // Condition 7: Longitude must be -180 to +180
                if frame.longitude < -180.0 || frame.longitude > 180.0 {
                    warn!(
                        imei = %resolved_uid,
                        lon = frame.longitude,
                        "Frame SKIPPED: Longitude out of range (same as GISV1 SaveDynData)"
                    );
                    return Ok(());
                }

                // Condition 8: Latitude must be -90 to +90
                if frame.latitude < -90.0 || frame.latitude > 90.0 {
                    warn!(
                        imei = %resolved_uid,
                        lat = frame.latitude,
                        "Frame SKIPPED: Latitude out of range (same as GISV1 SaveDynData)"
                    );
                    return Ok(());
                }

                // ============================================================
                // END GISV1 CONDITIONS
                // ============================================================

                // Ingest the frame into the database
                database
                    .ingest_hh_frame(&resolved_uid, protocol, &frame, &event_key)
                    .await?;

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
                    "Position ingested successfully"
                );

                // Process services (stop detection, fuel tracking)
                if let Some(device_id) = device_id_opt {
                    // Get vehicle and company info
                    let (vehicle_id, company_id) = database.get_device_vehicle_info(device_id).await?;

                    // Process stop detection
                    if let Some(completed_stop) = services.stop_detector.process_frame(device_id, &frame).await {
                        if let Err(err) = database.insert_vehicle_stop(&completed_stop, vehicle_id, company_id).await {
                            warn!(?err, device_id, "Failed to insert vehicle stop");
                        } else {
                            info!(device_id, duration = completed_stop.duration_seconds, "Vehicle stop recorded");
                        }
                    }

                    // Process fuel tracking - store raw value as received from frame
                    if let Some(fuel_event) = services.fuel_tracker.process_frame(device_id, &frame).await {
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

                // Publish to RabbitMQ for real-time updates
                if let Some(publisher) = publisher {
                    if let Err(err) = publisher.publish_hh_frame(&resolved_uid, protocol, &frame).await {
                        warn!(?err, "Failed to publish telemetry event");
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

        async fn get_device_vehicle_info(&self, _device_id: i32) -> anyhow::Result<(Option<i32>, i32)> {
            Ok((Some(1), 1)) // Mock vehicle_id and company_id
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
