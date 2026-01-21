//! Trip Detection Service
//!
//! Detects vehicle trips from GPS positions. A trip starts when the vehicle
//! begins moving (ignition on + speed > threshold) and ends when it stops
//! for a defined duration.

use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;
use chrono::{DateTime, Utc};
use tracing::{info, debug};

use crate::telemetry::model::HhFrame;

/// Minimum trip duration to be recorded (in seconds)
const MIN_TRIP_DURATION_SECS: i64 = 60;

/// Minimum trip distance to be recorded (in km)
const MIN_TRIP_DISTANCE_KM: f64 = 0.1;

/// Speed threshold to consider vehicle moving (km/h)
const MOVING_SPEED_THRESHOLD: f64 = 5.0;

/// Duration of stop that ends a trip (in seconds)
const TRIP_END_STOP_DURATION_SECS: i64 = 300; // 5 minutes

/// Active trip state for a device
#[derive(Debug, Clone)]
pub struct ActiveTrip {
    pub device_id: i32,
    pub vehicle_id: Option<i32>,
    pub company_id: i32,
    pub start_time: DateTime<Utc>,
    pub start_lat: f64,
    pub start_lng: f64,
    pub last_position_time: DateTime<Utc>,
    pub last_lat: f64,
    pub last_lng: f64,
    pub max_speed: f64,
    pub total_distance_km: f64,
    pub position_count: i32,
    pub last_moving_time: DateTime<Utc>,
    pub start_odometer: Option<u32>,
    pub fuel_start: Option<i32>,
    pub harsh_braking_count: i32,
    pub harsh_accel_count: i32,
    pub overspeeding_count: i32,
}

/// Completed trip ready for database insertion
#[derive(Debug, Clone)]
pub struct CompletedTrip {
    pub device_id: i32,
    pub vehicle_id: Option<i32>,
    pub company_id: i32,
    pub start_time: DateTime<Utc>,
    pub end_time: DateTime<Utc>,
    pub start_lat: f64,
    pub start_lng: f64,
    pub end_lat: f64,
    pub end_lng: f64,
    pub distance_km: f64,
    pub duration_minutes: i32,
    pub max_speed_kph: f64,
    pub avg_speed_kph: f64,
    pub start_odometer: Option<u32>,
    pub end_odometer: Option<u32>,
    pub fuel_consumed: Option<f64>,
    pub harsh_braking_count: i32,
    pub harsh_accel_count: i32,
    pub overspeeding_count: i32,
    pub status: String,
}

/// Service for detecting vehicle trips
pub struct TripDetector {
    /// Active trips by device_id
    active_trips: Arc<RwLock<HashMap<i32, ActiveTrip>>>,
    /// Previous frame data for calculations
    previous_frames: Arc<RwLock<HashMap<i32, FrameData>>>,
}

#[derive(Debug, Clone)]
struct FrameData {
    pub speed_kph: f64,
    pub heading_deg: f64,
    pub timestamp: DateTime<Utc>,
    pub latitude: f64,
    pub longitude: f64,
}

impl TripDetector {
    pub fn new() -> Self {
        Self {
            active_trips: Arc::new(RwLock::new(HashMap::new())),
            previous_frames: Arc::new(RwLock::new(HashMap::new())),
        }
    }

    /// Process a GPS frame and detect trip start/end
    /// Returns Some(CompletedTrip) if a trip just ended
    pub async fn process_frame(
        &self,
        device_id: i32,
        vehicle_id: Option<i32>,
        company_id: i32,
        frame: &HhFrame,
    ) -> Option<CompletedTrip> {
        let is_moving = frame.ignition_on && frame.speed_kph >= MOVING_SPEED_THRESHOLD;
        let now = DateTime::<Utc>::from_naive_utc_and_offset(frame.recorded_at, Utc);

        let mut trips = self.active_trips.write().await;
        let mut prev_frames = self.previous_frames.write().await;

        // Detect driving events (harsh braking, acceleration, overspeeding)
        let (harsh_braking, harsh_accel, overspeeding) = self.detect_driving_events(
            device_id,
            frame,
            prev_frames.get(&device_id),
        );

        // Update previous frame
        prev_frames.insert(device_id, FrameData {
            speed_kph: frame.speed_kph,
            heading_deg: frame.heading_deg,
            timestamp: now,
            latitude: frame.latitude,
            longitude: frame.longitude,
        });

        if let Some(trip) = trips.get_mut(&device_id) {
            // Trip in progress
            if is_moving {
                // Update trip with new position
                let distance = haversine_distance(
                    trip.last_lat, trip.last_lng,
                    frame.latitude, frame.longitude,
                );
                trip.total_distance_km += distance;
                trip.last_lat = frame.latitude;
                trip.last_lng = frame.longitude;
                trip.last_position_time = now;
                trip.last_moving_time = now;
                trip.position_count += 1;
                
                if frame.speed_kph > trip.max_speed {
                    trip.max_speed = frame.speed_kph;
                }

                // Accumulate driving events
                trip.harsh_braking_count += harsh_braking;
                trip.harsh_accel_count += harsh_accel;
                trip.overspeeding_count += overspeeding;

                debug!(device_id, distance_km = trip.total_distance_km, "Trip updated");
                None
            } else {
                // Vehicle stopped - check if trip should end
                let stop_duration = now.signed_duration_since(trip.last_moving_time).num_seconds();
                
                if stop_duration >= TRIP_END_STOP_DURATION_SECS {
                    // Trip ended
                    let trip_data = trips.remove(&device_id).unwrap();
                    let duration_secs = trip_data.last_moving_time
                        .signed_duration_since(trip_data.start_time)
                        .num_seconds();

                    // Only record trip if it meets minimum criteria
                    if duration_secs >= MIN_TRIP_DURATION_SECS 
                        && trip_data.total_distance_km >= MIN_TRIP_DISTANCE_KM 
                    {
                        let duration_minutes = (duration_secs / 60) as i32;
                        let avg_speed = if duration_minutes > 0 {
                            (trip_data.total_distance_km / (duration_minutes as f64 / 60.0))
                        } else {
                            0.0
                        };

                        let fuel_consumed = trip_data.fuel_start.map(|start| {
                            let end = frame.fuel_raw as i32;
                            ((start - end) as f64).max(0.0) * 0.5 // Rough conversion
                        });

                        let completed = CompletedTrip {
                            device_id: trip_data.device_id,
                            vehicle_id: trip_data.vehicle_id,
                            company_id: trip_data.company_id,
                            start_time: trip_data.start_time,
                            end_time: trip_data.last_moving_time,
                            start_lat: trip_data.start_lat,
                            start_lng: trip_data.start_lng,
                            end_lat: trip_data.last_lat,
                            end_lng: trip_data.last_lng,
                            distance_km: trip_data.total_distance_km,
                            duration_minutes,
                            max_speed_kph: trip_data.max_speed,
                            avg_speed_kph: avg_speed,
                            start_odometer: trip_data.start_odometer,
                            end_odometer: Some(frame.odometer_km),
                            fuel_consumed,
                            harsh_braking_count: trip_data.harsh_braking_count,
                            harsh_accel_count: trip_data.harsh_accel_count,
                            overspeeding_count: trip_data.overspeeding_count,
                            status: "completed".to_string(),
                        };

                        info!(
                            device_id,
                            distance_km = completed.distance_km,
                            duration_minutes = completed.duration_minutes,
                            max_speed = completed.max_speed_kph,
                            "Trip completed"
                        );

                        return Some(completed);
                    } else {
                        debug!(
                            device_id,
                            duration_secs,
                            distance_km = trip_data.total_distance_km,
                            "Trip too short, discarding"
                        );
                    }
                }
                None
            }
        } else {
            // No active trip
            if is_moving {
                // Start new trip
                let trip = ActiveTrip {
                    device_id,
                    vehicle_id,
                    company_id,
                    start_time: now,
                    start_lat: frame.latitude,
                    start_lng: frame.longitude,
                    last_position_time: now,
                    last_lat: frame.latitude,
                    last_lng: frame.longitude,
                    max_speed: frame.speed_kph,
                    total_distance_km: 0.0,
                    position_count: 1,
                    last_moving_time: now,
                    start_odometer: Some(frame.odometer_km),
                    fuel_start: Some(frame.fuel_raw as i32),
                    harsh_braking_count: 0,
                    harsh_accel_count: 0,
                    overspeeding_count: 0,
                };
                trips.insert(device_id, trip);
                info!(device_id, "Trip started");
            }
            None
        }
    }

    /// Detect driving events from frame data
    fn detect_driving_events(
        &self,
        _device_id: i32,
        frame: &HhFrame,
        prev_frame: Option<&FrameData>,
    ) -> (i32, i32, i32) {
        let mut harsh_braking = 0;
        let mut harsh_accel = 0;
        let mut overspeeding = 0;

        // Check for overspeeding (> 120 km/h)
        if frame.speed_kph > 120.0 {
            overspeeding = 1;
        }

        if let Some(prev) = prev_frame {
            let time_delta = (frame.recorded_at - prev.timestamp.naive_utc()).num_seconds() as f64;
            if time_delta > 0.0 && time_delta < 60.0 {
                let speed_delta = frame.speed_kph - prev.speed_kph;
                let accel = speed_delta / 3.6 / time_delta; // m/s²

                // Harsh braking: deceleration > 4 m/s²
                if accel < -4.0 {
                    harsh_braking = 1;
                }
                // Harsh acceleration: acceleration > 3.5 m/s²
                else if accel > 3.5 {
                    harsh_accel = 1;
                }
            }
        }

        // Also check MEMS data if available
        let accel_x = frame.mems_x as f64 / 256.0; // Convert to G
        let accel_y = frame.mems_y as f64 / 256.0;

        if accel_x < -0.4 {
            harsh_braking = 1;
        } else if accel_x > 0.4 {
            harsh_accel = 1;
        }
        
        // Harsh cornering
        if accel_y.abs() > 0.4 {
            // Could add cornering event here
        }

        (harsh_braking, harsh_accel, overspeeding)
    }

    /// Get count of active trips being tracked
    pub async fn active_trip_count(&self) -> usize {
        self.active_trips.read().await.len()
    }
}

impl Default for TripDetector {
    fn default() -> Self {
        Self::new()
    }
}

/// Calculate haversine distance between two points in kilometers
fn haversine_distance(lat1: f64, lng1: f64, lat2: f64, lng2: f64) -> f64 {
    const EARTH_RADIUS_KM: f64 = 6371.0;

    let lat1_rad = lat1.to_radians();
    let lat2_rad = lat2.to_radians();
    let delta_lat = (lat2 - lat1).to_radians();
    let delta_lng = (lng2 - lng1).to_radians();

    let a = (delta_lat / 2.0).sin().powi(2)
        + lat1_rad.cos() * lat2_rad.cos() * (delta_lng / 2.0).sin().powi(2);
    let c = 2.0 * a.sqrt().asin();

    EARTH_RADIUS_KM * c
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::NaiveDateTime;
    use crate::telemetry::model::{FrameKind, FrameVersion};

    fn make_frame(speed: f64, ignition: bool, time_str: &str, lat: f64, lng: f64) -> HhFrame {
        let recorded_at = NaiveDateTime::parse_from_str(time_str, "%Y-%m-%d %H:%M:%S").unwrap();
        HhFrame {
            kind: FrameKind::RealTime,
            version: FrameVersion::V3,
            recorded_at,
            latitude: lat,
            longitude: lng,
            speed_kph: speed,
            heading_deg: 0.0,
            power_voltage: 12,
            power_source_rescue: false,
            fuel_raw: 50,
            ignition_on: ignition,
            mems_x: 0,
            mems_y: 0,
            mems_z: 0,
            temperature_raw: 80,
            odometer_km: 10000,
            send_flag: 0,
            added_info: 0,
            signal_quality: Some(20),
            satellites_in_view: Some(10),
            rpm: None,
            is_valid: true,
            is_real_time: true,
            flags_raw: 0,
            raw_payload: String::new(),
            remaining_payload: None,
            address: None,
        }
    }

    #[tokio::test]
    async fn test_trip_detection() {
        let detector = TripDetector::new();

        // Vehicle starts moving
        let frame1 = make_frame(50.0, true, "2025-01-01 10:00:00", 36.8, 10.1);
        let result = detector.process_frame(1, Some(1), 1, &frame1).await;
        assert!(result.is_none()); // Trip started, not completed

        // Vehicle moves to new location
        let frame2 = make_frame(60.0, true, "2025-01-01 10:05:00", 36.81, 10.11);
        let result = detector.process_frame(1, Some(1), 1, &frame2).await;
        assert!(result.is_none());

        // Vehicle stops
        let frame3 = make_frame(0.0, false, "2025-01-01 10:10:00", 36.82, 10.12);
        let result = detector.process_frame(1, Some(1), 1, &frame3).await;
        assert!(result.is_none()); // Not yet ended (< 5 min stop)

        // After 5+ minutes of being stopped
        let frame4 = make_frame(0.0, false, "2025-01-01 10:16:00", 36.82, 10.12);
        let result = detector.process_frame(1, Some(1), 1, &frame4).await;
        assert!(result.is_some()); // Trip completed

        let trip = result.unwrap();
        assert!(trip.distance_km > 0.0);
        assert_eq!(trip.status, "completed");
    }
}
