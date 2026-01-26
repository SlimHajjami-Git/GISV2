//! GPS Gap Filler Service
//!
//! Detects gaps in GPS data (missing frames) and fills them with
//! interpolated positions using OSRM road routing.
//! 
//! GPS devices send frames every ~1 minute. When frames are lost due to
//! network issues, this service reconstructs the missing positions.

use anyhow::{Context, Result};
use chrono::{DateTime, Duration, Utc};
use reqwest::Client;
use serde::Deserialize;
use sqlx::PgPool;
use tracing::{info, warn, debug};

/// Minimum gap duration to trigger interpolation (in seconds)
const MIN_GAP_DURATION_SECS: i64 = 120; // 2 minutes = 2+ missing frames

/// Minimum distance to trigger interpolation (in meters)
const MIN_GAP_DISTANCE_M: f64 = 500.0;

/// Maximum implicit speed to consider gap valid (km/h)
/// Filters out GPS "teleportation" errors
const MAX_IMPLICIT_SPEED_KPH: f64 = 150.0;

/// Expected frame interval (in seconds)
const FRAME_INTERVAL_SECS: i64 = 60; // 1 minute

/// Last known position for a device
#[derive(Debug, Clone)]
pub struct LastPosition {
    pub device_id: i32,
    pub latitude: f64,
    pub longitude: f64,
    pub recorded_at: DateTime<Utc>,
    pub speed_kph: f64,
    pub ignition_on: bool,
}

/// Interpolated position to insert
#[derive(Debug, Clone)]
pub struct InterpolatedPosition {
    pub device_id: i32,
    pub latitude: f64,
    pub longitude: f64,
    pub recorded_at: DateTime<Utc>,
    pub speed_kph: f64,
    pub course_deg: f64,
    pub ignition_on: bool,
}

/// OSRM route response
#[derive(Debug, Deserialize)]
struct OsrmResponse {
    code: String,
    routes: Option<Vec<OsrmRoute>>,
}

#[derive(Debug, Deserialize)]
struct OsrmRoute {
    distance: f64, // meters
    duration: f64, // seconds
    geometry: OsrmGeometry,
}

#[derive(Debug, Deserialize)]
struct OsrmGeometry {
    coordinates: Vec<Vec<f64>>, // [lng, lat] pairs
}

/// GPS Gap Filler service
pub struct GapFiller {
    http_client: Client,
    osrm_base_url: String,
}

impl GapFiller {
    pub fn new(osrm_base_url: String) -> Self {
        Self {
            http_client: Client::new(),
            osrm_base_url,
        }
    }

    /// Check if there's a gap and fill it with interpolated positions
    /// Returns the positions to insert and the total distance of the gap
    pub async fn fill_gap(
        &self,
        device_id: i32,
        last_pos: &LastPosition,
        new_lat: f64,
        new_lng: f64,
        new_time: DateTime<Utc>,
    ) -> Result<(Vec<InterpolatedPosition>, f64)> {
        // Calculate gap duration
        let gap_duration = new_time.signed_duration_since(last_pos.recorded_at);
        let gap_secs = gap_duration.num_seconds();

        // Check if gap is significant
        if gap_secs < MIN_GAP_DURATION_SECS {
            return Ok((vec![], 0.0));
        }

        // Calculate linear distance
        let linear_distance_m = haversine_distance_m(
            last_pos.latitude, last_pos.longitude,
            new_lat, new_lng,
        );

        // Check minimum distance
        if linear_distance_m < MIN_GAP_DISTANCE_M {
            debug!(
                device_id,
                gap_secs,
                linear_distance_m,
                "Gap too short in distance, skipping interpolation"
            );
            return Ok((vec![], 0.0));
        }

        // Calculate implicit speed
        let gap_hours = gap_secs as f64 / 3600.0;
        let implicit_speed_kph = (linear_distance_m / 1000.0) / gap_hours;

        // Filter unrealistic speeds (GPS teleportation)
        if implicit_speed_kph > MAX_IMPLICIT_SPEED_KPH {
            warn!(
                device_id,
                gap_secs,
                linear_distance_m,
                implicit_speed_kph,
                "Gap speed too high, likely GPS error - skipping"
            );
            return Ok((vec![], 0.0));
        }

        info!(
            device_id,
            gap_secs,
            linear_distance_m,
            implicit_speed_kph,
            "Detected gap, fetching OSRM route for interpolation"
        );

        // Fetch OSRM route
        let route = self.fetch_osrm_route(
            last_pos.latitude, last_pos.longitude,
            new_lat, new_lng,
        ).await?;

        let Some(route) = route else {
            warn!(device_id, "OSRM route not found, using linear interpolation");
            return self.linear_interpolation(device_id, last_pos, new_lat, new_lng, new_time, linear_distance_m);
        };

        let route_distance_km = route.distance / 1000.0;
        
        // Calculate number of points to insert (1 per minute)
        let num_points = (gap_secs / FRAME_INTERVAL_SECS) as usize;
        if num_points < 1 {
            return Ok((vec![], route_distance_km));
        }

        // Interpolate points along the OSRM route
        let positions = self.interpolate_along_route(
            device_id,
            last_pos,
            new_time,
            &route.geometry.coordinates,
            num_points,
        );

        info!(
            device_id,
            num_points = positions.len(),
            route_distance_km,
            "Generated interpolated positions"
        );

        Ok((positions, route_distance_km))
    }

    /// Fetch route from OSRM
    async fn fetch_osrm_route(
        &self,
        from_lat: f64,
        from_lng: f64,
        to_lat: f64,
        to_lng: f64,
    ) -> Result<Option<OsrmRoute>> {
        let url = format!(
            "{}/route/v1/driving/{},{};{},{}?overview=full&geometries=geojson",
            self.osrm_base_url, from_lng, from_lat, to_lng, to_lat
        );

        let response = self.http_client
            .get(&url)
            .timeout(std::time::Duration::from_secs(5))
            .send()
            .await
            .context("Failed to call OSRM")?;

        let osrm: OsrmResponse = response.json().await
            .context("Failed to parse OSRM response")?;

        if osrm.code != "Ok" {
            return Ok(None);
        }

        Ok(osrm.routes.and_then(|r| r.into_iter().next()))
    }

    /// Interpolate positions along OSRM route coordinates
    fn interpolate_along_route(
        &self,
        device_id: i32,
        last_pos: &LastPosition,
        end_time: DateTime<Utc>,
        coordinates: &[Vec<f64>],
        num_points: usize,
    ) -> Vec<InterpolatedPosition> {
        if coordinates.is_empty() || num_points == 0 {
            return vec![];
        }

        // Calculate total route length
        let mut segment_lengths: Vec<f64> = Vec::new();
        let mut total_length = 0.0;

        for i in 1..coordinates.len() {
            let prev = &coordinates[i - 1];
            let curr = &coordinates[i];
            let len = haversine_distance_m(prev[1], prev[0], curr[1], curr[0]);
            segment_lengths.push(len);
            total_length += len;
        }

        if total_length == 0.0 {
            return vec![];
        }

        // Calculate time step
        let total_duration = end_time.signed_duration_since(last_pos.recorded_at);
        let time_step = total_duration / (num_points as i32 + 1);

        // Calculate average speed
        let avg_speed_kph = (total_length / 1000.0) / (total_duration.num_seconds() as f64 / 3600.0);

        let mut positions = Vec::new();

        for i in 1..=num_points {
            let progress = i as f64 / (num_points + 1) as f64;
            let target_distance = total_length * progress;

            // Find position along route at this distance
            let (lat, lng, heading) = self.get_position_at_distance(
                coordinates,
                &segment_lengths,
                target_distance,
            );

            let recorded_at = last_pos.recorded_at + time_step * i as i32;

            positions.push(InterpolatedPosition {
                device_id,
                latitude: lat,
                longitude: lng,
                recorded_at,
                speed_kph: avg_speed_kph,
                course_deg: heading,
                ignition_on: true, // Vehicle was moving during gap
            });
        }

        positions
    }

    /// Get position at a specific distance along the route
    fn get_position_at_distance(
        &self,
        coordinates: &[Vec<f64>],
        segment_lengths: &[f64],
        target_distance: f64,
    ) -> (f64, f64, f64) {
        let mut accumulated = 0.0;

        for (i, &seg_len) in segment_lengths.iter().enumerate() {
            if accumulated + seg_len >= target_distance {
                let from = &coordinates[i];
                let to = &coordinates[i + 1];
                
                let segment_progress = if seg_len > 0.0 {
                    (target_distance - accumulated) / seg_len
                } else {
                    0.0
                };

                let lat = from[1] + (to[1] - from[1]) * segment_progress;
                let lng = from[0] + (to[0] - from[0]) * segment_progress;
                let heading = calculate_heading(from[1], from[0], to[1], to[0]);

                return (lat, lng, heading);
            }
            accumulated += seg_len;
        }

        // Return last point if we exceeded
        let last = coordinates.last().unwrap();
        let prev = &coordinates[coordinates.len().saturating_sub(2)];
        let heading = calculate_heading(prev[1], prev[0], last[1], last[0]);
        (last[1], last[0], heading)
    }

    /// Fallback: linear interpolation when OSRM fails
    fn linear_interpolation(
        &self,
        device_id: i32,
        last_pos: &LastPosition,
        new_lat: f64,
        new_lng: f64,
        new_time: DateTime<Utc>,
        distance_m: f64,
    ) -> Result<(Vec<InterpolatedPosition>, f64)> {
        let gap_duration = new_time.signed_duration_since(last_pos.recorded_at);
        let gap_secs = gap_duration.num_seconds();
        let num_points = (gap_secs / FRAME_INTERVAL_SECS) as usize;

        if num_points < 1 {
            return Ok((vec![], distance_m / 1000.0));
        }

        let time_step = gap_duration / (num_points as i32 + 1);
        let avg_speed_kph = (distance_m / 1000.0) / (gap_secs as f64 / 3600.0);
        let heading = calculate_heading(last_pos.latitude, last_pos.longitude, new_lat, new_lng);

        let mut positions = Vec::new();

        for i in 1..=num_points {
            let progress = i as f64 / (num_points + 1) as f64;
            let lat = last_pos.latitude + (new_lat - last_pos.latitude) * progress;
            let lng = last_pos.longitude + (new_lng - last_pos.longitude) * progress;
            let recorded_at = last_pos.recorded_at + time_step * i as i32;

            positions.push(InterpolatedPosition {
                device_id,
                latitude: lat,
                longitude: lng,
                recorded_at,
                speed_kph: avg_speed_kph,
                course_deg: heading,
                ignition_on: true,
            });
        }

        Ok((positions, distance_m / 1000.0))
    }

    /// Insert interpolated positions into the database
    pub async fn insert_interpolated_positions(
        &self,
        pool: &PgPool,
        positions: &[InterpolatedPosition],
    ) -> Result<()> {
        if positions.is_empty() {
            return Ok(());
        }

        for pos in positions {
            sqlx::query(
                r#"
                INSERT INTO gps_positions (
                    device_id, latitude, longitude, recorded_at,
                    speed_kph, course_deg, ignition_on, is_interpolated
                )
                VALUES ($1, $2, $3, $4, $5, $6, $7, true)
                ON CONFLICT DO NOTHING
                "#,
            )
            .bind(pos.device_id)
            .bind(pos.latitude)
            .bind(pos.longitude)
            .bind(pos.recorded_at)
            .bind(pos.speed_kph)
            .bind(pos.course_deg)
            .bind(pos.ignition_on)
            .execute(pool)
            .await?;
        }

        info!(
            count = positions.len(),
            device_id = positions[0].device_id,
            "Inserted interpolated GPS positions"
        );

        Ok(())
    }
}

/// Calculate haversine distance in meters
fn haversine_distance_m(lat1: f64, lng1: f64, lat2: f64, lng2: f64) -> f64 {
    const EARTH_RADIUS_M: f64 = 6_371_000.0;

    let lat1_rad = lat1.to_radians();
    let lat2_rad = lat2.to_radians();
    let delta_lat = (lat2 - lat1).to_radians();
    let delta_lng = (lng2 - lng1).to_radians();

    let a = (delta_lat / 2.0).sin().powi(2)
        + lat1_rad.cos() * lat2_rad.cos() * (delta_lng / 2.0).sin().powi(2);
    let c = 2.0 * a.sqrt().asin();

    EARTH_RADIUS_M * c
}

/// Calculate heading/bearing between two points in degrees
fn calculate_heading(lat1: f64, lng1: f64, lat2: f64, lng2: f64) -> f64 {
    let d_lng = (lng2 - lng1).to_radians();
    let lat1_rad = lat1.to_radians();
    let lat2_rad = lat2.to_radians();

    let y = d_lng.sin() * lat2_rad.cos();
    let x = lat1_rad.cos() * lat2_rad.sin() - lat1_rad.sin() * lat2_rad.cos() * d_lng.cos();

    let heading = y.atan2(x).to_degrees();
    (heading + 360.0) % 360.0
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_haversine_distance() {
        // Test distance between two points in Tunis
        let dist = haversine_distance_m(36.738506, 10.298675, 36.732733, 10.323175);
        assert!(dist > 2000.0 && dist < 3000.0); // Should be ~2.3 km
    }

    #[test]
    fn test_calculate_heading() {
        // North
        let heading = calculate_heading(0.0, 0.0, 1.0, 0.0);
        assert!((heading - 0.0).abs() < 1.0);

        // East
        let heading = calculate_heading(0.0, 0.0, 0.0, 1.0);
        assert!((heading - 90.0).abs() < 1.0);
    }
}
