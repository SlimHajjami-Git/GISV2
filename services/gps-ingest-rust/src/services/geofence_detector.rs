use std::collections::HashMap;
use std::sync::Arc;
use tokio::sync::RwLock;
use chrono::{DateTime, Utc, Timelike, Weekday, Datelike};
use serde::{Deserialize, Serialize};
use tracing::{info, debug};

/// Geofence types
#[derive(Debug, Clone, PartialEq)]
pub enum GeofenceType {
    Polygon,
    Circle,
}

/// A point coordinate
#[derive(Debug, Clone, Deserialize, Serialize)]
pub struct Point {
    pub lat: f64,
    pub lng: f64,
}

/// Geofence definition loaded from database
#[derive(Debug, Clone)]
pub struct Geofence {
    pub id: i32,
    pub name: String,
    pub geofence_type: GeofenceType,
    pub coordinates: Vec<Point>,  // For polygon
    pub center_lat: Option<f64>,  // For circle
    pub center_lng: Option<f64>,  // For circle
    pub radius: Option<f64>,      // For circle (meters)
    pub alert_on_entry: bool,
    pub alert_on_exit: bool,
    pub notification_cooldown_minutes: i32,
    pub active_start_time: Option<chrono::NaiveTime>,
    pub active_end_time: Option<chrono::NaiveTime>,
    pub active_days: Option<Vec<String>>,
    pub company_id: i32,
    pub assigned_vehicle_ids: Vec<i32>,
}

/// Event type for geofence crossing
#[derive(Debug, Clone, PartialEq)]
pub enum GeofenceEventType {
    Entry,
    Exit,
}

impl GeofenceEventType {
    pub fn as_str(&self) -> &str {
        match self {
            GeofenceEventType::Entry => "entry",
            GeofenceEventType::Exit => "exit",
        }
    }
}

/// A detected geofence event
#[derive(Debug, Clone)]
pub struct GeofenceEvent {
    pub geofence_id: i32,
    pub geofence_name: String,
    pub vehicle_id: i32,
    pub device_id: i32,
    pub event_type: GeofenceEventType,
    pub latitude: f64,
    pub longitude: f64,
    pub speed: Option<f64>,
    pub timestamp: DateTime<Utc>,
    pub company_id: i32,
}

/// State tracking for a device in relation to geofences
#[derive(Debug, Clone)]
struct DeviceGeofenceState {
    inside_geofences: HashMap<i32, DateTime<Utc>>,  // geofence_id -> entry_time
    last_notification: HashMap<i32, DateTime<Utc>>, // geofence_id -> last_notified_at
}

impl Default for DeviceGeofenceState {
    fn default() -> Self {
        Self {
            inside_geofences: HashMap::new(),
            last_notification: HashMap::new(),
        }
    }
}

/// Geofence detector service
pub struct GeofenceDetector {
    geofences: Arc<RwLock<Vec<Geofence>>>,
    device_states: Arc<RwLock<HashMap<i32, DeviceGeofenceState>>>,
    last_refresh: Arc<RwLock<DateTime<Utc>>>,
    refresh_interval_secs: i64,
}

impl GeofenceDetector {
    pub fn new() -> Self {
        Self {
            geofences: Arc::new(RwLock::new(Vec::new())),
            device_states: Arc::new(RwLock::new(HashMap::new())),
            last_refresh: Arc::new(RwLock::new(DateTime::<Utc>::MIN_UTC)),
            refresh_interval_secs: 60, // Refresh geofences every 60 seconds
        }
    }

    /// Load geofences from database
    pub async fn load_geofences(&self, geofences: Vec<Geofence>) {
        let mut gf = self.geofences.write().await;
        *gf = geofences;
        let mut lr = self.last_refresh.write().await;
        *lr = Utc::now();
        info!(count = gf.len(), "Geofences loaded");
    }

    /// Check if geofences need refresh
    pub async fn needs_refresh(&self) -> bool {
        let lr = self.last_refresh.read().await;
        let elapsed = Utc::now().signed_duration_since(*lr).num_seconds();
        elapsed > self.refresh_interval_secs
    }

    /// Process a GPS frame and detect geofence events
    pub async fn process_frame(
        &self,
        device_id: i32,
        vehicle_id: i32,
        company_id: i32,
        lat: f64,
        lng: f64,
        speed: Option<f64>,
        timestamp: DateTime<Utc>,
    ) -> Vec<GeofenceEvent> {
        let mut events = Vec::new();
        let geofences = self.geofences.read().await;
        
        // Get or create device state
        let mut states = self.device_states.write().await;
        let state = states.entry(device_id).or_default();
        
        for geofence in geofences.iter() {
            // Check if this geofence applies to this vehicle
            if !geofence.assigned_vehicle_ids.is_empty() 
                && !geofence.assigned_vehicle_ids.contains(&vehicle_id) {
                continue;
            }
            
            // Check if geofence belongs to same company
            if geofence.company_id != company_id {
                continue;
            }
            
            // Check if geofence is active at current time
            if !self.is_geofence_active(geofence, &timestamp) {
                continue;
            }
            
            let is_inside = self.is_point_inside_geofence(lat, lng, geofence);
            let was_inside = state.inside_geofences.contains_key(&geofence.id);
            
            // Detect entry
            if is_inside && !was_inside {
                state.inside_geofences.insert(geofence.id, timestamp);
                
                if geofence.alert_on_entry && self.can_notify(state, geofence.id, geofence.notification_cooldown_minutes, &timestamp) {
                    state.last_notification.insert(geofence.id, timestamp);
                    events.push(GeofenceEvent {
                        geofence_id: geofence.id,
                        geofence_name: geofence.name.clone(),
                        vehicle_id,
                        device_id,
                        event_type: GeofenceEventType::Entry,
                        latitude: lat,
                        longitude: lng,
                        speed,
                        timestamp,
                        company_id,
                    });
                    debug!(
                        device_id,
                        geofence_id = geofence.id,
                        geofence_name = %geofence.name,
                        "Vehicle entered geofence"
                    );
                }
            }
            // Detect exit
            else if !is_inside && was_inside {
                let entry_time = state.inside_geofences.remove(&geofence.id);
                
                if geofence.alert_on_exit && self.can_notify(state, geofence.id, geofence.notification_cooldown_minutes, &timestamp) {
                    state.last_notification.insert(geofence.id, timestamp);
                    events.push(GeofenceEvent {
                        geofence_id: geofence.id,
                        geofence_name: geofence.name.clone(),
                        vehicle_id,
                        device_id,
                        event_type: GeofenceEventType::Exit,
                        latitude: lat,
                        longitude: lng,
                        speed,
                        timestamp,
                        company_id,
                    });
                    
                    if let Some(entry) = entry_time {
                        let duration = timestamp.signed_duration_since(entry).num_seconds();
                        debug!(
                            device_id,
                            geofence_id = geofence.id,
                            geofence_name = %geofence.name,
                            duration_seconds = duration,
                            "Vehicle exited geofence"
                        );
                    }
                }
            }
        }
        
        events
    }

    /// Check if geofence is active at the given time
    fn is_geofence_active(&self, geofence: &Geofence, timestamp: &DateTime<Utc>) -> bool {
        // Check active days
        if let Some(ref days) = geofence.active_days {
            if !days.is_empty() {
                let weekday = timestamp.weekday();
                let day_name = match weekday {
                    Weekday::Mon => "monday",
                    Weekday::Tue => "tuesday",
                    Weekday::Wed => "wednesday",
                    Weekday::Thu => "thursday",
                    Weekday::Fri => "friday",
                    Weekday::Sat => "saturday",
                    Weekday::Sun => "sunday",
                };
                
                if !days.iter().any(|d| d.to_lowercase() == day_name) {
                    return false;
                }
            }
        }
        
        // Check active time range
        if let (Some(start), Some(end)) = (geofence.active_start_time, geofence.active_end_time) {
            let current_time = timestamp.time();
            let naive_current = chrono::NaiveTime::from_hms_opt(
                current_time.hour(),
                current_time.minute(),
                current_time.second()
            ).unwrap_or_default();
            
            // Handle overnight ranges (e.g., 22:00 - 06:00)
            if start <= end {
                if naive_current < start || naive_current > end {
                    return false;
                }
            } else {
                // Overnight range
                if naive_current < start && naive_current > end {
                    return false;
                }
            }
        }
        
        true
    }

    /// Check if notification cooldown has passed
    fn can_notify(&self, state: &DeviceGeofenceState, geofence_id: i32, cooldown_minutes: i32, now: &DateTime<Utc>) -> bool {
        if let Some(last) = state.last_notification.get(&geofence_id) {
            let elapsed = now.signed_duration_since(*last).num_minutes();
            elapsed >= cooldown_minutes as i64
        } else {
            true
        }
    }

    /// Check if a point is inside a geofence
    fn is_point_inside_geofence(&self, lat: f64, lng: f64, geofence: &Geofence) -> bool {
        match geofence.geofence_type {
            GeofenceType::Circle => {
                if let (Some(center_lat), Some(center_lng), Some(radius)) = 
                    (geofence.center_lat, geofence.center_lng, geofence.radius) {
                    let distance = haversine_distance(lat, lng, center_lat, center_lng);
                    distance <= radius
                } else {
                    false
                }
            }
            GeofenceType::Polygon => {
                if geofence.coordinates.len() < 3 {
                    return false;
                }
                point_in_polygon(lat, lng, &geofence.coordinates)
            }
        }
    }

    /// Get duration inside a geofence for a device
    pub async fn get_duration_inside(&self, device_id: i32, geofence_id: i32) -> Option<i64> {
        let states = self.device_states.read().await;
        if let Some(state) = states.get(&device_id) {
            if let Some(entry_time) = state.inside_geofences.get(&geofence_id) {
                let duration = Utc::now().signed_duration_since(*entry_time).num_seconds();
                return Some(duration);
            }
        }
        None
    }
}

/// Calculate haversine distance between two points in meters
fn haversine_distance(lat1: f64, lng1: f64, lat2: f64, lng2: f64) -> f64 {
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

/// Ray casting algorithm for point-in-polygon test
fn point_in_polygon(lat: f64, lng: f64, polygon: &[Point]) -> bool {
    let n = polygon.len();
    if n < 3 {
        return false;
    }
    
    let mut inside = false;
    let mut j = n - 1;
    
    for i in 0..n {
        let xi = polygon[i].lng;
        let yi = polygon[i].lat;
        let xj = polygon[j].lng;
        let yj = polygon[j].lat;
        
        let intersect = ((yi > lat) != (yj > lat))
            && (lng < (xj - xi) * (lat - yi) / (yj - yi) + xi);
        
        if intersect {
            inside = !inside;
        }
        
        j = i;
    }
    
    inside
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_haversine_distance() {
        // Paris to London ~ 344 km
        let distance = haversine_distance(48.8566, 2.3522, 51.5074, -0.1278);
        assert!((distance - 343_000.0).abs() < 5000.0);
    }

    #[test]
    fn test_point_in_polygon() {
        let polygon = vec![
            Point { lat: 0.0, lng: 0.0 },
            Point { lat: 0.0, lng: 10.0 },
            Point { lat: 10.0, lng: 10.0 },
            Point { lat: 10.0, lng: 0.0 },
        ];
        
        assert!(point_in_polygon(5.0, 5.0, &polygon));
        assert!(!point_in_polygon(15.0, 5.0, &polygon));
        assert!(!point_in_polygon(-1.0, 5.0, &polygon));
    }

    #[test]
    fn test_point_in_circle() {
        let detector = GeofenceDetector::new();
        let geofence = Geofence {
            id: 1,
            name: "Test Circle".to_string(),
            geofence_type: GeofenceType::Circle,
            coordinates: vec![],
            center_lat: Some(48.8566),
            center_lng: Some(2.3522),
            radius: Some(1000.0), // 1km
            alert_on_entry: true,
            alert_on_exit: true,
            notification_cooldown_minutes: 5,
            active_start_time: None,
            active_end_time: None,
            active_days: None,
            company_id: 1,
            assigned_vehicle_ids: vec![],
        };
        
        // Point inside (very close to center)
        assert!(detector.is_point_inside_geofence(48.8566, 2.3522, &geofence));
        
        // Point outside (far from center)
        assert!(!detector.is_point_inside_geofence(48.9, 2.5, &geofence));
    }
}
