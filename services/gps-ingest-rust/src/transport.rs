use std::{collections::HashMap, sync::Arc};

use anyhow::{anyhow, Context, Result};
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
    telemetry,
};
type ConnectionMap = Arc<Mutex<HashMap<String, String>>>;

pub async fn run_listeners(
    config: &AppConfig,
    database: Arc<dyn TelemetryStore>,
    publisher: Option<Arc<dyn TelemetryEventPublisher>>,
) -> Result<()> {
    if config.listeners.is_empty() {
        info!("No listeners configured; nothing to start");
        return Ok(());
    }

    let mut handles = Vec::new();
    for listener in &config.listeners {
        match listener.transport {
            TransportKind::Tcp => {
                let cfg = listener.clone();
                let db = Arc::clone(&database);
                let mapping: ConnectionMap = Arc::new(Mutex::new(HashMap::new()));
                let publisher_clone = publisher.clone();
                handles.push(tokio::spawn(async move {
                    if let Err(err) = run_tcp_listener(cfg, db, mapping, publisher_clone).await {
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
        tokio::spawn(async move {
            if let Err(err) = handle_tcp_connection(stream, cfg_clone, db, map_clone, publisher_clone).await {
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
) -> Result<()> {
    let ascii_payload = String::from_utf8(raw_payload.to_vec()).context("payload is not UTF-8")?;
    
    // Split payload into individual frames (separated by newlines)
    let frames: Vec<&str> = ascii_payload
        .split(|c| c == '\r' || c == '\n')
        .map(|s| s.trim())
        .filter(|s| !s.is_empty() && *s != "AAAA" && *s != "HHHH") // Skip delimiters
        .filter(|s| starts_with_valid_header(s)) // Only keep valid frames
        .collect();

    if frames.is_empty() {
        return Err(anyhow!("no valid HH/AA frames found in payload"));
    }

    info!(protocol, frame_count = frames.len(), "Processing batch of frames");

    for frame_str in frames {
        if let Err(err) = process_single_frame(
            protocol,
            frame_str,
            Arc::clone(&database),
            peer_addr,
            Arc::clone(&connection_map),
            publisher.clone(),
        )
        .await
        {
            warn!(?err, frame = %frame_str, "Failed to process individual frame");
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
                let frame = telemetry::hh::parse_frame(frame_str)?;
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

                database
                    .ingest_hh_frame(&resolved_uid, protocol, &frame)
                    .await?;

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
        ) -> anyhow::Result<()> {
            self.frame_calls
                .lock()
                .unwrap()
                .push(format!("{}:{}:{}", protocol_type, device_uid, frame.recorded_at));
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
    }

    #[tokio::test]
    async fn route_payload_handles_info_and_data_frames() {
        let protocol = "gps_type_1";
        let store = Arc::new(MockStore::new("861001002935274"));
        let publisher = Arc::new(MockPublisher::new());
        let connection_map: ConnectionMap = Arc::new(Mutex::new(HashMap::new()));
        let peer = "127.0.0.1:1234";

        // Send info frame
        route_payload(
            protocol,
            HH01_FRAME.as_bytes(),
            Arc::clone(&store) as Arc<dyn TelemetryStore>,
            Some(peer),
            Arc::clone(&connection_map),
            Some(Arc::clone(&publisher) as Arc<dyn TelemetryEventPublisher>),
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
