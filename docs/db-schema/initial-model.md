# Initial Domain Data Model

## Core entities
1. **vehicles**
   - `id`, `vin`, `plate_number`, `device_id`, `client_id`, `status`, `type`, `fuel_type`.
2. **devices**
   - `id`, `model`, `protocol_family`, `imei`, `firmware_version`, `activation_date`, `status`.
3. **drivers**
   - `id`, `full_name`, `license_number`, `contact`, `employment_status`.
4. **users**
   - `id`, `username`, `email`, `role`, `permissions` (mapped via OAuth scopes / Keycloak realm roles).
5. **clients**
   - `id`, `name`, `billing_plan`, `timezone`.

## Telemetry & operations
- **gps_messages** (immutable log)
  - raw payload + metadata (device, protocol, received_at, decode_status, error_code). Retained per SLA.
- **telemetry_points** (normalized position stream)
  - `vehicle_id`, `timestamp`, `lat`, `lng`, `speed`, `heading`, `odometer`, `fuel_level`, `engine_status`.
- **trips**
  - planned vs actual start/end, total distance, duration, consumption, driver, client.
- **trip_segments**
  - contiguous movement sections within a trip; used for analytics and replay.
- **stops**
  - detected stop events with geofence references, scheduled vs actual times, reason codes.
- **fuel_operations**
  - refuel/consumption/anomaly records, linking sensor data to business rules.
- **commands**
  - outbound actions (engine cut, reset), tracking status/responses per device.
- **alerts**
  - threshold breaches (speeding, geofence, tamper) with acknowledgement workflow.

## CQRS projections (examples)
- `vehicle_latest_status_view`: cached last telemetry per vehicle for dashboards (<5s latency target).
- `trip_timeline_view`: enriched timeline for UI playback.
- `fuel_kpi_view`: aggregated daily/weekly fuel stats per fleet.

## Database technology choices
- **PostgreSQL / TimescaleDB** for transactional + time-series features.
- Partitioning by `client_id` and date (month) for telemetry tables.
- Logical replication hooks for future data lake/offloading.
- Strict foreign keys + soft-delete columns to preserve history.

## Migration strategy
- EF Core migrations stored under `/services/<service>/Migrations`.
- Liquibase/Atlas optional for shared schemas, documented per ADR.
