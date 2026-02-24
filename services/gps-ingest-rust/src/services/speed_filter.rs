use std::collections::HashMap;
use tokio::sync::RwLock;
use chrono::NaiveDateTime;
use tracing::warn;

/// Per-device speed state for spike detection
struct SpeedState {
    last_speed: f64,
    last_time: NaiveDateTime,
}

/// Filters physically impossible speed spikes from GPS data.
///
/// GPS trackers in urban environments can report false speed spikes due to
/// multipath signal reflection off buildings, tunnel exit re-acquisition,
/// or RF interference. This filter detects and corrects those spikes.
pub struct SpeedFilter {
    states: RwLock<HashMap<i32, SpeedState>>,
    /// Maximum acceleration in km/h per second (default: 8.0 ≈ 0-100 in 12.5s)
    max_accel_per_sec: f64,
    /// Maximum deceleration in km/h per second (default: 15.0 ≈ emergency braking)
    max_decel_per_sec: f64,
    /// Absolute speed cap in km/h (default: 200.0 for fleet vehicles)
    absolute_max_speed: f64,
}

pub struct SpeedFilterResult {
    pub corrected_speed: f64,
    pub was_filtered: bool,
    pub original_speed: f64,
}

impl SpeedFilter {
    pub fn new() -> Self {
        Self {
            states: RwLock::new(HashMap::new()),
            max_accel_per_sec: 8.0,   // 0→100 km/h in ~12.5s (fast car)
            max_decel_per_sec: 15.0,  // 100→0 km/h in ~6.7s (emergency brake)
            absolute_max_speed: 200.0,
        }
    }

    /// Filter a speed reading for a device. Returns the corrected speed.
    pub async fn filter(&self, device_id: i32, speed_kph: f64, recorded_at: NaiveDateTime) -> SpeedFilterResult {
        let mut states = self.states.write().await;

        // Step 1: Absolute cap
        if speed_kph > self.absolute_max_speed {
            warn!(
                device_id,
                speed = speed_kph,
                max = self.absolute_max_speed,
                "Speed CAPPED: exceeds absolute maximum"
            );
            let capped = self.absolute_max_speed;
            states.insert(device_id, SpeedState {
                last_speed: capped,
                last_time: recorded_at,
            });
            return SpeedFilterResult {
                corrected_speed: capped,
                was_filtered: true,
                original_speed: speed_kph,
            };
        }

        // Step 2: Check against previous reading
        let result = if let Some(state) = states.get(&device_id) {
            let dt_secs = (recorded_at - state.last_time).num_seconds().max(1) as f64;
            let delta = speed_kph - state.last_speed;
            let abs_delta = delta.abs();

            let max_delta = if delta > 0.0 {
                self.max_accel_per_sec * dt_secs
            } else {
                self.max_decel_per_sec * dt_secs
            };

            if abs_delta > max_delta && dt_secs < 120.0 {
                // Spike detected — only filter if frames are close together (< 2 min)
                // For frames > 2 min apart, the vehicle genuinely could have changed speed
                let corrected = if delta > 0.0 {
                    state.last_speed + max_delta
                } else {
                    (state.last_speed - max_delta).max(0.0)
                };

                warn!(
                    device_id,
                    original = speed_kph,
                    corrected = corrected,
                    last_speed = state.last_speed,
                    dt_secs = dt_secs,
                    "Speed SPIKE filtered (delta {:.0} km/h in {:.0}s exceeds max {:.0})",
                    abs_delta, dt_secs, max_delta
                );

                SpeedFilterResult {
                    corrected_speed: corrected,
                    was_filtered: true,
                    original_speed: speed_kph,
                }
            } else {
                SpeedFilterResult {
                    corrected_speed: speed_kph,
                    was_filtered: false,
                    original_speed: speed_kph,
                }
            }
        } else {
            // First reading for this device — accept as-is
            SpeedFilterResult {
                corrected_speed: speed_kph,
                was_filtered: false,
                original_speed: speed_kph,
            }
        };

        // Update state with the corrected speed
        states.insert(device_id, SpeedState {
            last_speed: result.corrected_speed,
            last_time: recorded_at,
        });

        result
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::NaiveDate;

    fn ts(secs: i64) -> NaiveDateTime {
        NaiveDate::from_ymd_opt(2026, 2, 24).unwrap()
            .and_hms_opt(12, 0, 0).unwrap()
            + chrono::Duration::seconds(secs)
    }

    #[tokio::test]
    async fn normal_acceleration_passes() {
        let filter = SpeedFilter::new();
        let r1 = filter.filter(1, 60.0, ts(0)).await;
        assert!(!r1.was_filtered);
        // 60 → 80 in 60s = +0.33 km/h/s → OK (max 8.0/s)
        let r2 = filter.filter(1, 80.0, ts(60)).await;
        assert!(!r2.was_filtered);
        assert_eq!(r2.corrected_speed, 80.0);
    }

    #[tokio::test]
    async fn spike_is_filtered() {
        let filter = SpeedFilter::new();
        let _ = filter.filter(1, 60.0, ts(0)).await;
        // 60 → 161 in 5s = +20.2 km/h/s → filtered (max 8.0/s)
        let r = filter.filter(1, 161.0, ts(5)).await;
        assert!(r.was_filtered);
        assert!(r.corrected_speed < 161.0);
        // Max allowed: 60 + 8*5 = 100
        assert!((r.corrected_speed - 100.0).abs() < 0.1);
    }

    #[tokio::test]
    async fn absolute_cap_works() {
        let filter = SpeedFilter::new();
        let r = filter.filter(1, 250.0, ts(0)).await;
        assert!(r.was_filtered);
        assert_eq!(r.corrected_speed, 200.0);
    }

    #[tokio::test]
    async fn long_gap_allows_speed_change() {
        let filter = SpeedFilter::new();
        let _ = filter.filter(1, 60.0, ts(0)).await;
        // 60 → 161 after 3 minutes (180s) → dt > 120s → NOT filtered
        let r = filter.filter(1, 161.0, ts(180)).await;
        assert!(!r.was_filtered);
        assert_eq!(r.corrected_speed, 161.0);
    }

    #[tokio::test]
    async fn different_devices_independent() {
        let filter = SpeedFilter::new();
        let _ = filter.filter(1, 60.0, ts(0)).await;
        let _ = filter.filter(2, 120.0, ts(0)).await;
        // Device 2 at 120 → 30 in 5s = decel 18 km/h/s → filtered (max 15/s = 75 drop)
        let r = filter.filter(2, 30.0, ts(5)).await;
        assert!(r.was_filtered);
        // Max decel: 120 - 15*5 = 45
        assert!((r.corrected_speed - 45.0).abs() < 0.1);
    }
}
