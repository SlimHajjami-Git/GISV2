use axum::{
    extract::{Path, Query, State},
    http::StatusCode,
    routing::get,
    Json, Router,
};
use serde::{Deserialize, Serialize};
use sqlx::PgPool;
use tower_http::cors::{Any, CorsLayer};
use chrono::{DateTime, NaiveDateTime, Utc};

#[derive(Debug, Deserialize)]
pub struct DateRangeQuery {
    pub from: Option<String>,
    pub to: Option<String>,
}

#[derive(Clone)]
pub struct ApiState {
    pub pool: PgPool,
}

#[derive(Debug, Serialize, sqlx::FromRow)]
pub struct VehicleResponse {
    pub id: i32,
    pub name: String,
    pub plate_number: Option<String>,
    pub brand: Option<String>,
    pub model: Option<String>,
    pub color: Option<String>,
    pub driver_name: Option<String>,
    pub driver_phone: Option<String>,
}

#[derive(Debug, Serialize, sqlx::FromRow)]
pub struct DeviceResponse {
    pub id: i32,
    pub device_uid: String,
    pub label: Option<String>,
    pub protocol_type: Option<String>,
    pub vehicle_id: Option<i32>,
    pub updated_at: Option<DateTime<Utc>>,
}

#[derive(Debug, Serialize, sqlx::FromRow)]
pub struct PositionResponse {
    pub id: i32,
    pub device_id: i32,
    pub device_uid: String,
    pub vehicle_id: Option<i32>,
    pub vehicle_name: Option<String>,
    pub recorded_at: Option<NaiveDateTime>,
    pub lat: Option<f64>,
    pub lng: Option<f64>,
    pub speed_kph: Option<f64>,
    pub course_deg: Option<f64>,
    pub ignition_on: Option<bool>,
    pub fuel_raw: Option<i16>,
    pub power_voltage: Option<i16>,
}

pub fn create_router(state: ApiState) -> Router {
    let cors = CorsLayer::new()
        .allow_origin(Any)
        .allow_methods(Any)
        .allow_headers(Any);

    Router::new()
        .route("/api/health", get(health_check))
        .route("/api/vehicles", get(get_vehicles))
        .route("/api/vehicles/:id/positions", get(get_vehicle_positions))
        .route("/api/devices", get(get_devices))
        .route("/api/positions", get(get_positions))
        .route("/api/positions/latest", get(get_latest_positions))
        .layer(cors)
        .with_state(state)
}

async fn health_check() -> &'static str {
    "OK"
}

async fn get_vehicles(
    State(state): State<ApiState>,
) -> Result<Json<Vec<VehicleResponse>>, (StatusCode, String)> {
    let vehicles = sqlx::query_as::<_, VehicleResponse>(
        r#"
        SELECT id, name, plate_number, brand, model, color, driver_name, driver_phone
        FROM vehicles
        ORDER BY name
        "#,
    )
    .fetch_all(&state.pool)
    .await
    .map_err(|e| (StatusCode::INTERNAL_SERVER_ERROR, e.to_string()))?;

    Ok(Json(vehicles))
}

async fn get_vehicle_positions(
    State(state): State<ApiState>,
    Path(vehicle_id): Path<i32>,
    Query(params): Query<DateRangeQuery>,
) -> Result<Json<Vec<PositionResponse>>, (StatusCode, String)> {
    // Parse date filters
    let from_date = params.from.as_ref().and_then(|s| 
        NaiveDateTime::parse_from_str(&format!("{} 00:00:00", s), "%Y-%m-%d %H:%M:%S").ok()
    );
    let to_date = params.to.as_ref().and_then(|s| 
        NaiveDateTime::parse_from_str(&format!("{} 23:59:59", s), "%Y-%m-%d %H:%M:%S").ok()
    );

    let positions = sqlx::query_as::<_, PositionResponse>(
        r#"
        SELECT 
            p.id,
            p.device_id,
            d.device_uid,
            d.vehicle_id,
            v.name as vehicle_name,
            p.recorded_at,
            p.lat,
            p.lng,
            p.speed_kph,
            p.course_deg,
            p.ignition_on,
            p.fuel_raw,
            p.power_voltage
        FROM positions p
        JOIN devices d ON d.id = p.device_id
        LEFT JOIN vehicles v ON v.id = d.vehicle_id
        WHERE d.vehicle_id = $1
          AND ($2::timestamp IS NULL OR p.recorded_at >= $2)
          AND ($3::timestamp IS NULL OR p.recorded_at <= $3)
        ORDER BY p.recorded_at ASC
        LIMIT 500
        "#,
    )
    .bind(vehicle_id)
    .bind(from_date)
    .bind(to_date)
    .fetch_all(&state.pool)
    .await
    .map_err(|e| (StatusCode::INTERNAL_SERVER_ERROR, e.to_string()))?;

    Ok(Json(positions))
}

async fn get_devices(
    State(state): State<ApiState>,
) -> Result<Json<Vec<DeviceResponse>>, (StatusCode, String)> {
    let devices = sqlx::query_as::<_, DeviceResponse>(
        r#"
        SELECT id, device_uid, label, protocol_type, vehicle_id, updated_at
        FROM devices
        ORDER BY updated_at DESC
        "#,
    )
    .fetch_all(&state.pool)
    .await
    .map_err(|e| (StatusCode::INTERNAL_SERVER_ERROR, e.to_string()))?;

    Ok(Json(devices))
}

async fn get_positions(
    State(state): State<ApiState>,
) -> Result<Json<Vec<PositionResponse>>, (StatusCode, String)> {
    let positions = sqlx::query_as::<_, PositionResponse>(
        r#"
        SELECT 
            p.id,
            p.device_id,
            d.device_uid,
            d.vehicle_id,
            v.name as vehicle_name,
            p.recorded_at,
            p.lat,
            p.lng,
            p.speed_kph,
            p.course_deg,
            p.ignition_on,
            p.fuel_raw,
            p.power_voltage
        FROM positions p
        JOIN devices d ON d.id = p.device_id
        LEFT JOIN vehicles v ON v.id = d.vehicle_id
        ORDER BY p.recorded_at DESC
        LIMIT 1000
        "#,
    )
    .fetch_all(&state.pool)
    .await
    .map_err(|e| (StatusCode::INTERNAL_SERVER_ERROR, e.to_string()))?;

    Ok(Json(positions))
}

async fn get_latest_positions(
    State(state): State<ApiState>,
) -> Result<Json<Vec<PositionResponse>>, (StatusCode, String)> {
    // Retourne la dernière position de chaque véhicule
    let positions = sqlx::query_as::<_, PositionResponse>(
        r#"
        SELECT DISTINCT ON (d.vehicle_id)
            p.id,
            p.device_id,
            d.device_uid,
            d.vehicle_id,
            v.name as vehicle_name,
            p.recorded_at,
            p.lat,
            p.lng,
            p.speed_kph,
            p.course_deg,
            p.ignition_on,
            p.fuel_raw,
            p.power_voltage
        FROM positions p
        JOIN devices d ON d.id = p.device_id
        LEFT JOIN vehicles v ON v.id = d.vehicle_id
        WHERE d.vehicle_id IS NOT NULL
        ORDER BY d.vehicle_id, p.recorded_at DESC
        "#,
    )
    .fetch_all(&state.pool)
    .await
    .map_err(|e| (StatusCode::INTERNAL_SERVER_ERROR, e.to_string()))?;

    Ok(Json(positions))
}
