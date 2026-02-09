use anyhow::{Context, Result};
use chrono::{DateTime, Utc};
use reqwest::Client;
use serde::{Deserialize, Serialize};
use std::sync::Arc;
use tokio::sync::RwLock;
use tracing::{debug, info, warn};

/// Valhalla map-matching service for GPS road snapping
/// Uses the trace_route API which is specifically designed for GPS tracking
pub struct ValhallaService {
    client: Client,
    base_url: String,
    is_available: Arc<RwLock<bool>>,
}

/// Input point for Valhalla trace_route
#[derive(Debug, Clone, Serialize)]
pub struct TracePoint {
    pub lat: f64,
    pub lon: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub time: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub radius: Option<f64>,
}

/// Valhalla trace_route request
#[derive(Debug, Serialize)]
pub struct TraceRouteRequest {
    pub shape: Vec<TracePoint>,
    pub costing: String,
    pub shape_match: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub filters: Option<TraceFilters>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub trace_options: Option<TraceOptions>,
}

#[derive(Debug, Serialize)]
pub struct TraceFilters {
    pub attributes: Vec<String>,
    pub action: String,
}

#[derive(Debug, Serialize)]
pub struct TraceOptions {
    pub search_radius: f64,
    pub gps_accuracy: f64,
    pub breakage_distance: f64,
    pub interpolation_distance: f64,
}

/// Valhalla trace_route response
#[derive(Debug, Deserialize)]
pub struct TraceRouteResponse {
    pub trip: Option<TripResponse>,
    pub matched_points: Option<Vec<MatchedPoint>>,
    pub shape: Option<String>,
}

#[derive(Debug, Deserialize)]
pub struct TripResponse {
    pub legs: Vec<TripLeg>,
    pub summary: TripSummary,
    pub shape: Option<String>,
}

#[derive(Debug, Deserialize)]
pub struct TripLeg {
    pub shape: String,
    pub summary: LegSummary,
}

#[derive(Debug, Deserialize)]
pub struct TripSummary {
    pub length: f64,
    pub time: f64,
}

#[derive(Debug, Deserialize)]
pub struct LegSummary {
    pub length: f64,
    pub time: f64,
}

#[derive(Debug, Deserialize)]
pub struct MatchedPoint {
    pub lat: f64,
    pub lon: f64,
    #[serde(rename = "type")]
    pub point_type: Option<String>,
    pub edge_index: Option<i32>,
    pub distance_from_trace_point: Option<f64>,
}

/// Snapped GPS point result
#[derive(Debug, Clone)]
pub struct SnappedPoint {
    pub original_lat: f64,
    pub original_lon: f64,
    pub snapped_lat: f64,
    pub snapped_lon: f64,
    pub distance_from_road: f64,
    pub timestamp: Option<DateTime<Utc>>,
    pub is_matched: bool,
}

/// Snapped route result
#[derive(Debug, Clone)]
pub struct SnappedRoute {
    pub points: Vec<SnappedPoint>,
    pub total_distance_km: f64,
    pub total_time_seconds: f64,
    pub encoded_polyline: Option<String>,
}

impl ValhallaService {
    pub fn new(base_url: Option<String>) -> Self {
        let url = base_url.unwrap_or_else(|| {
            std::env::var("VALHALLA_URL").unwrap_or_else(|_| "http://valhalla:8002".to_string())
        });
        
        info!(base_url = %url, "Initializing Valhalla service");
        
        Self {
            client: Client::builder()
                .timeout(std::time::Duration::from_secs(30))
                .build()
                .expect("Failed to create HTTP client"),
            base_url: url,
            is_available: Arc::new(RwLock::new(true)),
        }
    }

    /// Create from environment variables
    pub fn from_env() -> Option<Self> {
        let url = std::env::var("VALHALLA_URL").ok()?;
        Some(Self::new(Some(url)))
    }

    /// Check if Valhalla service is healthy
    pub async fn health_check(&self) -> bool {
        let url = format!("{}/status", self.base_url);
        match self.client.get(&url).send().await {
            Ok(resp) => {
                let available = resp.status().is_success();
                let mut is_available = self.is_available.write().await;
                *is_available = available;
                available
            }
            Err(e) => {
                warn!(?e, "Valhalla health check failed");
                let mut is_available = self.is_available.write().await;
                *is_available = false;
                false
            }
        }
    }

    /// Snap GPS points to road using Valhalla trace_route API
    /// This is the main method for GPS playback road snapping
    pub async fn snap_to_road(&self, points: &[(f64, f64, Option<DateTime<Utc>>)]) -> Result<SnappedRoute> {
        if points.is_empty() {
            return Ok(SnappedRoute {
                points: vec![],
                total_distance_km: 0.0,
                total_time_seconds: 0.0,
                encoded_polyline: None,
            });
        }

        // Check availability
        let is_available = *self.is_available.read().await;
        if !is_available {
            // Try health check
            if !self.health_check().await {
                anyhow::bail!("Valhalla service is not available");
            }
        }

        // Build trace points
        let shape: Vec<TracePoint> = points
            .iter()
            .map(|(lat, lon, time)| TracePoint {
                lat: *lat,
                lon: *lon,
                time: time.map(|t| t.timestamp()),
                radius: Some(50.0), // 50m search radius for GPS accuracy
            })
            .collect();

        let request = TraceRouteRequest {
            shape,
            costing: "auto".to_string(),
            shape_match: "map_snap".to_string(),
            trace_options: Some(TraceOptions {
                search_radius: 50.0,      // Search within 50m of GPS point
                gps_accuracy: 10.0,       // Assume 10m GPS accuracy
                breakage_distance: 2000.0, // Allow 2km gaps
                interpolation_distance: 10.0, // Interpolate every 10m
            }),
            filters: Some(TraceFilters {
                attributes: vec![
                    "edge.names".to_string(),
                    "edge.speed".to_string(),
                    "matched.point".to_string(),
                    "matched.type".to_string(),
                ],
                action: "include".to_string(),
            }),
        };

        let url = format!("{}/trace_route", self.base_url);
        debug!(url = %url, points_count = points.len(), "Calling Valhalla trace_route");

        let response = self
            .client
            .post(&url)
            .json(&request)
            .send()
            .await
            .context("Failed to send request to Valhalla")?;

        if !response.status().is_success() {
            let status = response.status();
            let error_text = response.text().await.unwrap_or_default();
            anyhow::bail!("Valhalla returned error {}: {}", status, error_text);
        }

        let trace_response: TraceRouteResponse = response
            .json()
            .await
            .context("Failed to parse Valhalla response")?;

        // Build snapped points from response
        let mut snapped_points = Vec::with_capacity(points.len());

        if let Some(matched) = trace_response.matched_points {
            for (i, (original_lat, original_lon, time)) in points.iter().enumerate() {
                if let Some(mp) = matched.get(i) {
                    snapped_points.push(SnappedPoint {
                        original_lat: *original_lat,
                        original_lon: *original_lon,
                        snapped_lat: mp.lat,
                        snapped_lon: mp.lon,
                        distance_from_road: mp.distance_from_trace_point.unwrap_or(0.0),
                        timestamp: *time,
                        is_matched: mp.point_type.as_deref() != Some("unmatched"),
                    });
                } else {
                    // No match found, use original point
                    snapped_points.push(SnappedPoint {
                        original_lat: *original_lat,
                        original_lon: *original_lon,
                        snapped_lat: *original_lat,
                        snapped_lon: *original_lon,
                        distance_from_road: 0.0,
                        timestamp: *time,
                        is_matched: false,
                    });
                }
            }
        } else {
            // Fallback: use original points if no matched_points
            for (original_lat, original_lon, time) in points {
                snapped_points.push(SnappedPoint {
                    original_lat: *original_lat,
                    original_lon: *original_lon,
                    snapped_lat: *original_lat,
                    snapped_lon: *original_lon,
                    distance_from_road: 0.0,
                    timestamp: *time,
                    is_matched: false,
                });
            }
        }

        // Extract trip summary
        let (total_distance_km, total_time_seconds, encoded_polyline) = if let Some(trip) = trace_response.trip {
            (
                trip.summary.length,
                trip.summary.time,
                trip.shape,
            )
        } else {
            (0.0, 0.0, trace_response.shape)
        };

        Ok(SnappedRoute {
            points: snapped_points,
            total_distance_km,
            total_time_seconds,
            encoded_polyline,
        })
    }

    /// Simple route snapping for two points (origin -> destination)
    pub async fn get_route(&self, origin: (f64, f64), destination: (f64, f64)) -> Result<SnappedRoute> {
        self.snap_to_road(&[
            (origin.0, origin.1, None),
            (destination.0, destination.1, None),
        ])
        .await
    }

    /// Decode Valhalla's encoded polyline (uses 6 decimal precision)
    pub fn decode_polyline(encoded: &str) -> Vec<(f64, f64)> {
        let mut points = Vec::new();
        let mut lat = 0i64;
        let mut lng = 0i64;
        let mut idx = 0;
        let chars: Vec<char> = encoded.chars().collect();

        while idx < chars.len() {
            // Decode latitude
            let (delta_lat, new_idx) = Self::decode_value(&chars, idx);
            idx = new_idx;
            lat += delta_lat;

            // Decode longitude
            let (delta_lng, new_idx) = Self::decode_value(&chars, idx);
            idx = new_idx;
            lng += delta_lng;

            // Valhalla uses 6 decimal places (1e6 precision)
            points.push((lat as f64 / 1e6, lng as f64 / 1e6));
        }

        points
    }

    fn decode_value(chars: &[char], mut idx: usize) -> (i64, usize) {
        let mut result = 0i64;
        let mut shift = 0;

        loop {
            let b = (chars[idx] as u32) - 63;
            idx += 1;
            result |= ((b & 0x1f) as i64) << shift;
            shift += 5;
            if b < 0x20 {
                break;
            }
        }

        if result & 1 != 0 {
            result = !(result >> 1);
        } else {
            result >>= 1;
        }

        (result, idx)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_decode_polyline() {
        // Simple test polyline
        let encoded = "_p~iF~ps|U_ulLnnqC_mqNvxq`@";
        let points = ValhallaService::decode_polyline(encoded);
        assert!(!points.is_empty());
    }

    #[tokio::test]
    #[ignore] // Requires running Valhalla service
    async fn test_snap_to_road() {
        let service = ValhallaService::new(Some("http://localhost:8002".to_string()));
        
        // Test points in Tunisia (Tunis area)
        let points = vec![
            (36.8065, 10.1815, None),
            (36.8070, 10.1820, None),
            (36.8075, 10.1825, None),
        ];

        let result = service.snap_to_road(&points).await;
        assert!(result.is_ok());
        
        let snapped = result.unwrap();
        assert_eq!(snapped.points.len(), 3);
    }
}
