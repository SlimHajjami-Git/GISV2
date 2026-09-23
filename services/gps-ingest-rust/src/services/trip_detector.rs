//! Trip Detection Service
//!
//! Detects vehicle trips from GPS positions. A trip starts when the vehicle
//! begins moving (ignition on + speed > threshold) and ends when it stops
//! for a defined duration.
//!
//! CONSTAT (tableau de bord à 58 597 km pour 263 TU 6995 là où la mesure réelle
//! donne ~3 000 km sur 30 jours, facteur 19) : deux défauts se cumulaient ici.
//!
//!  1. Les bornes du trajet suivaient l'ordre d'ARRIVÉE des trames et non l'ordre
//!     chronologique. Les boîtiers émettent par rafales — tête = position la plus
//!     récente, puis rejeu du tampon — donc la dernière trame ARRIVÉE est la plus
//!     ANCIENNE. `end_time` reculait, la fenêtre `recorded_at BETWEEN $2 AND $3`
//!     du recalcul SQL (db.rs) ne couvrait plus qu'un SOUS-ENSEMBLE des positions,
//!     le recalcul rendait un chiffre minuscule, et sous 0,05 km db.rs basculait
//!     sur la distance du détecteur — accumulée dans l'ordre d'arrivée, donc avec
//!     un aller-retour fantôme par rafale. C'était le seul chemin d'écriture
//!     gonflée encore ouvert (trajet 502679 du 09/09 : 1 033 km stockés pour
//!     199,6 km réels).
//!
//!  2. Rien ne fermait jamais un trajet : ni durée maximale, ni balayage, ni
//!     expiration. La seule sortie était l'arrivée d'une trame à l'arrêt, or
//!     `stop_duration` devenait négatif en rafale et transport.rs jette justement
//!     les trames à l'arrêt rapprochées. D'où 24 trajets de plus de 24 h sur le
//!     seul véhicule 372 (dont un de 13 jours), dont les fenêtres RECOUVRENT les
//!     trajets normaux : le même déplacement facturé plusieurs fois.

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

// ── Garde-fous d'accumulation ────────────────────────────────────────────────
// Ces trois seuils DOIVENT bouger en même temps que le recalcul SQL de
// `insert_trip` (db.rs:961-992, plafond `LEAST(..., 5.0)`) et que
// scripts/fix-trips-distance.sql : le détecteur et la base doivent compter la
// même chose, sinon le repli de db.rs réintroduit l'écart qu'on vient de fermer.

/// Segment maximal retenu entre deux trames (km). ALIGNÉ sur db.rs:980.
/// Au-delà c'est un saut GPS franc, pas un déplacement.
const MAX_SEGMENT_KM: f64 = 5.0;

/// Plancher sous lequel un segment n'est que du bruit GPS à l'arrêt (km).
/// = le plancher de db.rs:227.
const MIN_SEGMENT_KM: f64 = 0.01;

/// Vitesse implicite maximale acceptée pour un segment (km/h).
/// = GpsDistanceCalculator.cs:26, le seul garde-fou de vitesse du produit.
const MAX_IMPLIED_KPH: f64 = 250.0;

// ── Bornes de durée ──────────────────────────────────────────────────────────

/// Durée maximale d'un trajet avant coupure (s).
/// RÉGLAGE MÉTIER, pas technique : 12 h laisse passer entière une longue journée
/// de conduite et coupe le trajet de 13 jours observé sur le véhicule 372.
/// Ce chiffre est fait pour être discuté et changé.
const MAX_TRIP_DURATION_SECS: i64 = 12 * 3600;

/// Silence maximal entre deux trames d'un même trajet (s). Au-delà, la reprise
/// ouvre un trajet neuf plutôt que de rallonger l'ancien.
const MAX_FRAME_GAP_SECS: i64 = 2 * 3600;

/// Silence au-delà duquel le balayage de fond clôture un trajet resté ouvert (s).
pub const STALE_TRIP_SILENCE_SECS: i64 = 1800;

/// Période du balayage de fond (s).
pub const STALE_TRIP_SWEEP_INTERVAL_SECS: u64 = 300;

/// Avance maximale tolérée d'une horloge boîtier sur l'horloge murale (s).
/// Voir `sweep_stale` : des horloges de boîtier sont corrompues (gps_alerts
/// contient des horodatages en 2004), il faut aussi se protéger du sens inverse.
const MAX_CLOCK_AHEAD_SECS: i64 = 24 * 3600;

/// Active trip state for a device
#[derive(Debug, Clone)]
pub struct ActiveTrip {
    pub device_id: i32,
    pub vehicle_id: Option<i32>,
    pub company_id: i32,
    /// Borne BASSE chronologique du trajet (le plus petit `recorded_at` vu).
    pub start_time: DateTime<Utc>,
    pub start_lat: f64,
    pub start_lng: f64,
    /// ANCRE de calcul de distance : le dernier point RETENU, dans l'ordre
    /// d'ARRIVÉE. Distinct de `last_moving_time`, qui est la borne HAUTE
    /// chronologique. Les deux se confondaient avant le correctif, d'où le
    /// double comptage en rafale.
    pub last_position_time: DateTime<Utc>,
    pub last_lat: f64,
    pub last_lng: f64,
    pub max_speed: f64,
    pub total_distance_km: f64,
    pub position_count: i32,
    /// Borne HAUTE chronologique du trajet (le plus grand `recorded_at` vu).
    pub last_moving_time: DateTime<Utc>,
    /// Position associée à la borne HAUTE — donc le vrai point d'arrivée, qui
    /// n'est pas forcément la dernière trame reçue.
    pub end_lat: f64,
    pub end_lng: f64,
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
    ///
    /// Une trame ne peut clôturer QU'UN trajet à la fois : la coupure sur durée
    /// vit dans la branche « en mouvement » et la clôture sur arrêt dans la
    /// branche « à l'arrêt », elles s'excluent. La signature reste donc
    /// `Option<CompletedTrip>` et les quatre appelants de transport.rs sont
    /// inchangés. Les trajets clôturés SANS trame (boîtier devenu muet) sortent
    /// par `sweep_stale`.
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

        // ── Coupure d'un trajet qui s'éternise ou qui reprend après un silence ──
        // Sans elle, un boîtier qui ne renvoie jamais de trame à l'arrêt garde
        // son trajet ouvert des jours durant et sa fenêtre de recalcul RECOUVRE
        // celles des trajets normaux : le même déplacement est facturé plusieurs
        // fois. On clôture AVANT de traiter la trame ; la trame en cours rouvre
        // ensuite un trajet neuf par le chemin normal.
        let needs_cut = if is_moving {
            trips
                .get(&device_id)
                .map(|trip| {
                    let trip_age = now.signed_duration_since(trip.start_time).num_seconds();
                    // Le silence se mesure sur `last_moving_time` (la borne HAUTE
                    // chronologique), JAMAIS sur `last_position_time` : cette
                    // dernière est l'ancre de distance, posée dans l'ordre
                    // d'ARRIVÉE, donc une rafale la ramène sur la trame la plus
                    // ancienne et le silence calculé serait faux.
                    let silence = now
                        .signed_duration_since(trip.last_moving_time)
                        .num_seconds();
                    trip_age > MAX_TRIP_DURATION_SECS || silence > MAX_FRAME_GAP_SECS
                })
                .unwrap_or(false)
        } else {
            false
        };

        let mut cut: Option<CompletedTrip> = None;
        if needs_cut {
            if let Some(trip_data) = trips.remove(&device_id) {
                info!(
                    device_id,
                    distance_km = trip_data.total_distance_km,
                    "Trajet trop long ou reprise après silence : coupure et réouverture"
                );
                cut = finalize_trip(
                    trip_data,
                    Some(frame.odometer_km),
                    Some(frame.fuel_raw as i32),
                );
            }
        }

        let outcome = if let Some(trip) = trips.get_mut(&device_id) {
            // Trip in progress
            if is_moving {
                // `last_position_time` était déclaré mais lu NULLE PART : c'est
                // exactement l'ancre qui manquait pour distinguer une trame neuve
                // d'un rejeu de tampon.
                let dt_secs = now
                    .signed_duration_since(trip.last_position_time)
                    .num_seconds();

                // Vitesse maximale et compteurs d'événements : toujours mis à
                // jour, même sur un rejeu — une trame rejouée reste une mesure
                // réelle, seule sa CONTRIBUTION EN DISTANCE est en double.
                if frame.speed_kph > trip.max_speed {
                    trip.max_speed = frame.speed_kph;
                }
                trip.harsh_braking_count += harsh_braking;
                trip.harsh_accel_count += harsh_accel;
                trip.overspeeding_count += overspeeding;

                if dt_secs <= 0 {
                    // Trame rejouée ou doublon. On n'accumule RIEN et on NE
                    // DÉPLACE PAS l'ancre : la déplacer ferait repartir la trame
                    // suivante d'un point faux et remplacerait la surestimation
                    // par une sous-estimation.
                    debug!(device_id, dt_secs, "Trame rejouée, distance ignorée");
                } else {
                    let segment = haversine_distance(
                        trip.last_lat, trip.last_lng,
                        frame.latitude, frame.longitude,
                    );
                    let implied_kph = segment / (dt_secs as f64 / 3600.0);

                    if segment > MAX_SEGMENT_KM {
                        // Saut GPS franc : le segment est faux, mais le point de
                        // référence a bel et bien bougé — on réancre.
                        debug!(device_id, segment, "Saut GPS : segment rejeté, réancrage");
                    } else if implied_kph > MAX_IMPLIED_KPH {
                        debug!(
                            device_id, segment, implied_kph,
                            "Vitesse implicite aberrante : segment rejeté, réancrage"
                        );
                    } else if segment < MIN_SEGMENT_KM {
                        // Bruit GPS à l'arrêt : rien à compter, mais l'ancre avance.
                    } else {
                        trip.total_distance_km += segment;
                    }

                    trip.last_lat = frame.latitude;
                    trip.last_lng = frame.longitude;
                    trip.last_position_time = now;
                    trip.position_count += 1;
                }

                // ── Bornes monotones ──
                // Les bornes du trajet sont le vrai MIN/MAX des `recorded_at`,
                // jamais la dernière trame ARRIVÉE. C'est ce qui garantit que la
                // fenêtre BETWEEN du recalcul SQL (db.rs) couvre TOUJOURS les
                // positions du trajet, donc que le repli gonflé ne se déclenche
                // plus. Effet de bord voulu : `duration_secs` ne peut plus être
                // négatif, donc des trajets réels cessent de disparaître en
                // « Trip too short ».
                if now > trip.last_moving_time {
                    trip.last_moving_time = now;
                    trip.end_lat = frame.latitude;
                    trip.end_lng = frame.longitude;
                }
                if now < trip.start_time {
                    trip.start_time = now;
                    trip.start_lat = frame.latitude;
                    trip.start_lng = frame.longitude;
                }

                debug!(device_id, distance_km = trip.total_distance_km, "Trip updated");
                None
            } else {
                // Vehicle stopped - check if trip should end.
                // Clampé à zéro : une trame rejouée (antérieure à la borne haute)
                // donnait une durée d'arrêt NÉGATIVE.
                let stop_duration = now
                    .signed_duration_since(trip.last_moving_time)
                    .num_seconds()
                    .max(0);

                if stop_duration >= TRIP_END_STOP_DURATION_SECS {
                    let trip_data = trips.remove(&device_id).unwrap();
                    finalize_trip(
                        trip_data,
                        Some(frame.odometer_km),
                        Some(frame.fuel_raw as i32),
                    )
                } else {
                    None
                }
            }
        } else {
            // No active trip
            if is_moving {
                trips.insert(device_id, start_trip(device_id, vehicle_id, company_id, frame, now));
                info!(device_id, "Trip started");
            }
            None
        };

        // Les deux sorties s'excluent : si une coupure a eu lieu, la trame en
        // cours vient d'ouvrir un trajet neuf et `outcome` vaut None.
        cut.or(outcome)
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

    /// Clôture les trajets dont plus aucune trame n'arrive.
    ///
    /// Sans ce balayage, un boîtier qui se tait (coupure GSM, véhicule au garage,
    /// boîtier débranché) laisse son trajet ouvert POUR TOUJOURS : la coupure sur
    /// durée de `process_frame` n'agit qu'à l'arrivée d'une trame, et il n'en
    /// vient plus. Un trajet retiré n'est rendu QU'UNE fois, même s'il est ensuite
    /// écarté par les critères minimaux.
    pub async fn sweep_stale(
        &self,
        now: DateTime<Utc>,
        max_silence_secs: i64,
    ) -> Vec<CompletedTrip> {
        let mut trips = self.active_trips.write().await;

        // PIÈGE DU PROJET : `now` est une heure MURALE (Utc::now) alors que
        // `last_moving_time` vient de l'HORLOGE DU BOÎTIER, et des horloges
        // corrompues existent en production (gps_alerts contient des horodatages
        // remontant à 2004). Deux sens de dérive, tous deux traités par une
        // clôture PROPRE, jamais par une panique :
        //   - horloge en retard (2004) : le silence calculé est énorme, on
        //     clôture — c'est le bon geste, ce trajet ne se fermerait jamais ;
        //   - horloge en avance : le silence est NÉGATIF et le trajet ne serait
        //     JAMAIS balayé, il fuirait à perpétuité — au-delà de
        //     MAX_CLOCK_AHEAD_SECS d'avance on clôture aussi.
        // Le calcul passe par `signed_duration_since(...).num_seconds()`, qui ne
        // déborde pas, plutôt que par une addition de date qui pourrait paniquer.
        let stale_ids: Vec<i32> = trips
            .iter()
            .filter(|(_, trip)| {
                let silence = now
                    .signed_duration_since(trip.last_moving_time)
                    .num_seconds();
                silence > max_silence_secs || silence < -MAX_CLOCK_AHEAD_SECS
            })
            .map(|(device_id, _)| *device_id)
            .collect();

        let mut completed = Vec::new();
        for device_id in stale_ids {
            if let Some(trip_data) = trips.remove(&device_id) {
                if let Some(trip) = finalize_trip(trip_data, None, None) {
                    info!(
                        device_id,
                        distance_km = trip.distance_km,
                        duration_minutes = trip.duration_minutes,
                        "Trajet muet clôturé par le balayage"
                    );
                    completed.push(trip);
                }
            }
        }
        completed
    }
}

impl Default for TripDetector {
    fn default() -> Self {
        Self::new()
    }
}

/// Ouvre un trajet sur la trame courante.
fn start_trip(
    device_id: i32,
    vehicle_id: Option<i32>,
    company_id: i32,
    frame: &HhFrame,
    now: DateTime<Utc>,
) -> ActiveTrip {
    ActiveTrip {
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
        end_lat: frame.latitude,
        end_lng: frame.longitude,
        start_odometer: Some(frame.odometer_km),
        fuel_start: Some(frame.fuel_raw as i32),
        harsh_braking_count: 0,
        harsh_accel_count: 0,
        overspeeding_count: 0,
    }
}

/// Transforme un trajet actif en trajet terminé, ou l'écarte s'il n'atteint pas
/// les critères minimaux. Partagé par les trois sorties (arrêt prolongé, coupure
/// sur durée, balayage) pour qu'elles ne puissent pas diverger.
///
/// `end_odometer` et `end_fuel_raw` valent None quand la clôture ne vient pas
/// d'une trame (balayage) : on ne fabrique pas de valeur.
fn finalize_trip(
    trip: ActiveTrip,
    end_odometer: Option<u32>,
    end_fuel_raw: Option<i32>,
) -> Option<CompletedTrip> {
    let duration_secs = trip
        .last_moving_time
        .signed_duration_since(trip.start_time)
        .num_seconds();

    if duration_secs < MIN_TRIP_DURATION_SECS || trip.total_distance_km < MIN_TRIP_DISTANCE_KM {
        debug!(
            device_id = trip.device_id,
            duration_secs,
            distance_km = trip.total_distance_km,
            "Trip too short, discarding"
        );
        return None;
    }

    let duration_minutes = (duration_secs / 60) as i32;
    let avg_speed = if duration_minutes > 0 {
        trip.total_distance_km / (duration_minutes as f64 / 60.0)
    } else {
        0.0
    };

    let fuel_consumed = match (trip.fuel_start, end_fuel_raw) {
        (Some(start), Some(end)) => Some(((start - end) as f64).max(0.0) * 0.5), // Rough conversion
        _ => None,
    };

    let completed = CompletedTrip {
        device_id: trip.device_id,
        vehicle_id: trip.vehicle_id,
        company_id: trip.company_id,
        start_time: trip.start_time,
        end_time: trip.last_moving_time,
        start_lat: trip.start_lat,
        start_lng: trip.start_lng,
        end_lat: trip.end_lat,
        end_lng: trip.end_lng,
        distance_km: trip.total_distance_km,
        duration_minutes,
        max_speed_kph: trip.max_speed,
        avg_speed_kph: avg_speed,
        start_odometer: trip.start_odometer,
        end_odometer,
        fuel_consumed,
        harsh_braking_count: trip.harsh_braking_count,
        harsh_accel_count: trip.harsh_accel_count,
        overspeeding_count: trip.overspeeding_count,
        status: "completed".to_string(),
    };

    info!(
        device_id = completed.device_id,
        distance_km = completed.distance_km,
        duration_minutes = completed.duration_minutes,
        max_speed = completed.max_speed_kph,
        "Trip completed"
    );

    Some(completed)
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

    /// 1° de latitude avec R = 6371 km. Les trajets de test se déplacent le long
    /// d'un MÉRIDIEN : le haversine y vaut exactement R·Δφ, donc les segments
    /// s'additionnent au chiffre près et les valeurs attendues sont calculables
    /// à la main plutôt que copiées depuis la sortie du code testé.
    const KM_PER_DEG_LAT: f64 = 111.194_926_64;

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
            battery_raw: 0,
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
            fuel_rate_l_per_100km: None,
            fms_temperature_c: None,
            is_valid: true,
            is_real_time: true,
            flags_raw: 0,
            raw_payload: String::new(),
            remaining_payload: None,
            address: None,
        }
    }

    fn at(time_str: &str) -> DateTime<Utc> {
        DateTime::<Utc>::from_naive_utc_and_offset(
            NaiveDateTime::parse_from_str(time_str, "%Y-%m-%d %H:%M:%S").unwrap(),
            Utc,
        )
    }

    /// Écart relatif accepté sur une distance attendue : 1 %.
    fn assert_km(actual: f64, expected: f64, what: &str) {
        let tolerance = (expected * 0.01).max(0.001);
        assert!(
            (actual - expected).abs() <= tolerance,
            "{what} : attendu {expected:.4} km, obtenu {actual:.4} km"
        );
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

        // Vehicle stops. 10:08 et non 10:10 : à 10:10 l'arrêt durait 300 s PILE
        // contre un test `>= 300`, le trajet se clôturait donc dès cette trame et
        // le test échouait sur son propre `assert!(result.is_none())`.
        let frame3 = make_frame(0.0, false, "2025-01-01 10:08:00", 36.82, 10.12);
        let result = detector.process_frame(1, Some(1), 1, &frame3).await;
        assert!(result.is_none()); // Not yet ended (< 5 min stop)

        // After 5+ minutes of being stopped
        let frame4 = make_frame(0.0, false, "2025-01-01 10:16:00", 36.82, 10.12);
        let result = detector.process_frame(1, Some(1), 1, &frame4).await;
        assert!(result.is_some()); // Trip completed

        let trip = result.unwrap();
        // Assertion sur la VALEUR : un `> 0.0` passerait aussi avec un chiffre
        // 19 fois trop grand, ce qui est précisément le défaut qu'on corrige.
        assert_km(trip.distance_km, 1.4244, "distance du trajet");
        assert_eq!(trip.start_time, at("2025-01-01 10:00:00"));
        assert_eq!(trip.end_time, at("2025-01-01 10:05:00"));
        assert_eq!(trip.status, "completed");
    }

    /// Rafale : la tête est la trame la plus RÉCENTE, suivie du rejeu du tampon.
    /// Avant correctif le détecteur ajoutait |P0→P3| puis |P3→P1| puis |P1→P2|,
    /// soit 6 d pour 3 d réels (l'aller-retour fantôme), et `end_time` reculait
    /// à 10:02 au lieu de 10:03.
    #[tokio::test]
    async fn rafale_aller_retour_bornes_exactes_et_distance_chronologique() {
        let detector = TripDetector::new();
        let lng = 10.1;

        // P0 ouvre le trajet.
        let p0 = make_frame(50.0, true, "2025-01-01 10:00:00", 36.80, lng);
        assert!(detector.process_frame(1, Some(1), 1, &p0).await.is_none());

        // Tête de rafale : P3, la position la plus récente.
        let p3 = make_frame(50.0, true, "2025-01-01 10:03:00", 36.83, lng);
        assert!(detector.process_frame(1, Some(1), 1, &p3).await.is_none());

        // Rejeu du tampon : P1 puis P2, chronologiquement ANTÉRIEURS à P3.
        let p1 = make_frame(50.0, true, "2025-01-01 10:01:00", 36.81, lng);
        assert!(detector.process_frame(1, Some(1), 1, &p1).await.is_none());
        let p2 = make_frame(50.0, true, "2025-01-01 10:02:00", 36.82, lng);
        assert!(detector.process_frame(1, Some(1), 1, &p2).await.is_none());

        // Arrêt prolongé : clôture.
        let stop = make_frame(0.0, false, "2025-01-01 10:10:00", 36.83, lng);
        let trip = detector
            .process_frame(1, Some(1), 1, &stop)
            .await
            .expect("le trajet doit se clôturer");

        assert_eq!(trip.start_time, at("2025-01-01 10:00:00"));
        assert_eq!(trip.end_time, at("2025-01-01 10:03:00"));
        // Somme chronologique : 3 × 0,01° de latitude.
        assert_km(trip.distance_km, 3.0 * 0.01 * KM_PER_DEG_LAT, "somme chronologique");
    }

    /// Un saut GPS franc est rejeté, MAIS le point de référence est réancré :
    /// la trame suivante ne doit pas produire un second segment aberrant.
    #[tokio::test]
    async fn saut_gps_rejete_mais_point_reancre() {
        let detector = TripDetector::new();
        let lng = 10.1;

        let p0 = make_frame(50.0, true, "2025-01-01 10:00:00", 36.80, lng);
        detector.process_frame(1, Some(1), 1, &p0).await;

        // +0,45° de latitude ≈ 50 km : au-delà de MAX_SEGMENT_KM, rejeté.
        let saut = make_frame(50.0, true, "2025-01-01 10:10:00", 37.25, lng);
        detector.process_frame(1, Some(1), 1, &saut).await;

        // +0,01° depuis le point du saut. Si le réancrage n'avait pas eu lieu,
        // ce segment vaudrait 0,46° ≈ 51 km et serait rejeté à son tour : la
        // distance finale serait nulle et le trajet écarté.
        let suite = make_frame(50.0, true, "2025-01-01 10:20:00", 37.26, lng);
        detector.process_frame(1, Some(1), 1, &suite).await;

        let stop = make_frame(0.0, false, "2025-01-01 10:30:00", 37.26, lng);
        let trip = detector
            .process_frame(1, Some(1), 1, &stop)
            .await
            .expect("le trajet doit se clôturer");

        assert_km(trip.distance_km, 0.01 * KM_PER_DEG_LAT, "segment après réancrage");
    }

    /// 4 km en 10 s = 1 440 km/h : sous MAX_SEGMENT_KM, donc seul le garde-fou de
    /// vitesse implicite peut l'attraper.
    #[tokio::test]
    async fn vitesse_implicite_aberrante_rejetee() {
        let detector = TripDetector::new();
        let lng = 10.1;

        let p0 = make_frame(50.0, true, "2025-01-01 10:00:00", 36.80, lng);
        detector.process_frame(1, Some(1), 1, &p0).await;

        // +0,03597° ≈ 4,00 km en 10 s.
        let teleport = make_frame(50.0, true, "2025-01-01 10:00:10", 36.83597, lng);
        detector.process_frame(1, Some(1), 1, &teleport).await;

        // Segment plausible depuis le point réancré.
        let suite = make_frame(50.0, true, "2025-01-01 10:02:00", 36.84597, lng);
        detector.process_frame(1, Some(1), 1, &suite).await;

        let stop = make_frame(0.0, false, "2025-01-01 10:10:00", 36.84597, lng);
        let trip = detector
            .process_frame(1, Some(1), 1, &stop)
            .await
            .expect("le trajet doit se clôturer");

        // Seul le second segment compte : les 4 km du téléport sont écartés.
        assert_km(trip.distance_km, 0.01 * KM_PER_DEG_LAT, "distance hors téléport");
    }

    /// Un trajet de 14 h est coupé en deux à la 12ᵉ heure. Avant correctif rien
    /// ne fermait jamais un trajet : il en serait sorti UN seul, de 14 h.
    #[tokio::test]
    async fn trajet_de_quatorze_heures_coupe_en_deux() {
        let detector = TripDetector::new();
        let lng = 10.1;
        let mut trips = Vec::new();

        // Une trame par heure, +0,01° de latitude à chaque fois. L'écart d'1 h
        // reste sous MAX_FRAME_GAP_SECS : c'est bien la DURÉE qui coupe, pas le
        // silence.
        for h in 0..=14 {
            let frame = make_frame(
                50.0,
                true,
                &format!("2025-01-01 {h:02}:00:00"),
                36.80 + 0.01 * h as f64,
                lng,
            );
            if let Some(trip) = detector.process_frame(1, Some(1), 1, &frame).await {
                trips.push(trip);
            }
        }

        // Arrêt prolongé pour clôturer le second trajet.
        let stop = make_frame(0.0, false, "2025-01-01 14:30:00", 36.94, lng);
        if let Some(trip) = detector.process_frame(1, Some(1), 1, &stop).await {
            trips.push(trip);
        }

        assert_eq!(trips.len(), 2, "le trajet de 14 h doit être coupé en deux");

        // Premier tronçon : 00:00 → 12:00, soit 12 segments.
        assert_eq!(trips[0].start_time, at("2025-01-01 00:00:00"));
        assert_eq!(trips[0].end_time, at("2025-01-01 12:00:00"));
        assert_km(trips[0].distance_km, 12.0 * 0.01 * KM_PER_DEG_LAT, "premier tronçon");

        // Second tronçon : rouvert sur la trame de 13:00, donc 13:00 → 14:00.
        assert_eq!(trips[1].start_time, at("2025-01-01 13:00:00"));
        assert_eq!(trips[1].end_time, at("2025-01-01 14:00:00"));
        assert_km(trips[1].distance_km, 0.01 * KM_PER_DEG_LAT, "second tronçon");

        // Les deux tronçons s'additionnent. Le segment 12:00 → 13:00 n'est
        // attribué à aucun des deux : la coupure rouvre SUR la trame en cours,
        // qui devient l'ancre du nouveau trajet. C'est cohérent avec le recalcul
        // SQL, dont les fenêtres [00:00,12:00] et [13:00,14:00] ne se recouvrent
        // pas — mieux vaut un inter-trame non attribué qu'un double comptage.
        assert_km(
            trips[0].distance_km + trips[1].distance_km,
            13.0 * 0.01 * KM_PER_DEG_LAT,
            "somme des deux tronçons",
        );
    }

    /// Une trame de mouvement REJOUÉE ne doit plus faire reculer la borne haute.
    /// Avant correctif elle ramenait `last_moving_time` à 10:01, si bien qu'une
    /// trame d'arrêt à 10:07 donnait une durée d'arrêt de 360 s et clôturait le
    /// trajet à tort, avec une heure de fin fausse (10:01 au lieu de 10:05).
    #[tokio::test]
    async fn trame_rejouee_ne_perturbe_plus_la_cloture() {
        let detector = TripDetector::new();
        let lng = 10.1;

        let p0 = make_frame(50.0, true, "2025-01-01 10:00:00", 36.80, lng);
        detector.process_frame(1, Some(1), 1, &p0).await;
        let p1 = make_frame(50.0, true, "2025-01-01 10:05:00", 36.81, lng);
        detector.process_frame(1, Some(1), 1, &p1).await;

        // Rejeu d'une trame de 10:01.
        let rejeu = make_frame(50.0, true, "2025-01-01 10:01:00", 36.802, lng);
        assert!(detector.process_frame(1, Some(1), 1, &rejeu).await.is_none());

        // 10:07 : seulement 2 min après la vraie fin de mouvement (10:05).
        let stop_tot = make_frame(0.0, false, "2025-01-01 10:07:00", 36.81, lng);
        assert!(
            detector.process_frame(1, Some(1), 1, &stop_tot).await.is_none(),
            "le trajet ne doit pas se clôturer 2 min après le dernier mouvement"
        );

        // 10:11 : 6 min après la vraie fin de mouvement, la clôture a bien lieu.
        let stop = make_frame(0.0, false, "2025-01-01 10:11:00", 36.81, lng);
        let trip = detector
            .process_frame(1, Some(1), 1, &stop)
            .await
            .expect("le trajet doit se clôturer");

        assert_eq!(trip.end_time, at("2025-01-01 10:05:00"));
        assert_km(trip.distance_km, 0.01 * KM_PER_DEG_LAT, "distance hors rejeu");
    }

    /// Le balayage ferme un trajet dont le boîtier s'est tu, et ne le rend
    /// QU'UNE fois. Il ferme aussi un trajet dont l'horloge est aberrante dans
    /// l'autre sens (en avance), qui ne serait sinon jamais balayé.
    #[tokio::test]
    async fn sweep_stale_cloture_un_trajet_muet_une_seule_fois() {
        let detector = TripDetector::new();
        let lng = 10.1;

        let p0 = make_frame(50.0, true, "2025-01-01 10:00:00", 36.80, lng);
        detector.process_frame(1, Some(1), 1, &p0).await;
        let p1 = make_frame(50.0, true, "2025-01-01 10:05:00", 36.81, lng);
        detector.process_frame(1, Some(1), 1, &p1).await;

        // Boîtier n° 2 dont l'horloge est en avance d'un an : silence NÉGATIF.
        let futur0 = make_frame(50.0, true, "2026-01-01 10:00:00", 36.80, lng);
        detector.process_frame(2, Some(2), 1, &futur0).await;
        let futur1 = make_frame(50.0, true, "2026-01-01 10:05:00", 36.81, lng);
        detector.process_frame(2, Some(2), 1, &futur1).await;

        assert_eq!(detector.active_trip_count().await, 2);

        // 10:40 heure murale : 35 min de silence pour le boîtier n° 1.
        let closed = detector.sweep_stale(at("2025-01-01 10:40:00"), 1800).await;
        assert_eq!(closed.len(), 2, "les deux trajets aberrants doivent être clôturés");
        assert_eq!(detector.active_trip_count().await, 0);

        let muet = closed.iter().find(|t| t.device_id == 1).expect("trajet muet");
        assert_eq!(muet.start_time, at("2025-01-01 10:00:00"));
        assert_eq!(muet.end_time, at("2025-01-01 10:05:00"));
        assert_km(muet.distance_km, 0.01 * KM_PER_DEG_LAT, "distance du trajet muet");

        // Second passage : plus rien à rendre.
        let encore = detector.sweep_stale(at("2025-01-01 11:40:00"), 1800).await;
        assert!(encore.is_empty(), "un trajet clôturé ne doit pas être rendu deux fois");
    }
}
