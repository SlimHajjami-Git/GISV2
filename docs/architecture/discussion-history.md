# Discussion History & Project Understanding

## Session timeline (2025-11-25)
1. **Context check** – Confirmed knowledge of legacy BeliveGis application (WebForms, procedures, GPS listener).
2. **Goal definition** – User requested a full reboot named `GIS_V2` under `trunk/`, leveraging best practices (Git, Docker, microservices, Rust ingestion, .NET 8 CQRS, Angular 17+, RabbitMQ, OAuth2, OpenTelemetry, GitFlow, Docker Compose → Kubernetes).
3. **Initial planning** – Captured domain expectations: new tables for trips, stops, fuel, extensible GPS decoders for all device families, CQRS microservices, DevOps strategy.
4. **Repository scaffolding** – Created directory structure, documentation (architecture overview, data model, GPS decoding strategy, roadmap).
5. **Shared kernel** – Added Proto schema (`NormalizedTelemetry`) for cross-service contracts.
6. **Rust service** – Bootstrapped `gps-ingest-rust` (manual Cargo files due to missing toolchain on first machine).
7. **Progress log** – Authored `progress-log.md` for cross-machine continuity.
8. **GitHub setup** – Initial commit pushed to `https://github.com/SlimHajjami-Git/GISV2.git`.

## Understanding of project GIS_V2
### Business goals
- Replace the monolithic BeliveGis with a modern, scalable platform handling any GPS tracker model worldwide.
- Provide reliable tracking of trips, stops, fuel usage, alerts, and analytics with low-latency access for monitoring dashboards.

### Technical pillars
1. **Rust ingestion layer**
   - Multi-protocol decoders via plugin traits.
   - Normalize telemetry and publish to RabbitMQ topics (`telemetry.position`, `telemetry.alert`, etc.).
   - Observability baked in (OpenTelemetry traces, metrics, dead-letter queues).
2. **.NET 8 microservices (CQRS)**
   - Command services handle writes, validations, domain rules (trips, stops, fuel events).
   - Query services serve projections for dashboards, built from RabbitMQ consumers.
   - Security via OAuth2/OIDC (Keycloak) and GitFlow for code management.
3. **Angular 17+ frontend**
   - Modern UI with standalone components, NgRx/Signals, realtime updates via SignalR/WebSockets.
4. **Data model**
   - Core tables: vehicles, devices, drivers, clients, users.
   - Operational tables: gps_messages (raw), telemetry_points, trips, trip_segments, stops, fuel_operations, commands, alerts.
   - Projections: vehicle_latest_status_view, trip_timeline_view, fuel_kpi_view.
   - PostgreSQL/Timescale with partitioning and strict constraints.
5. **DevOps & Observability**
   - Docker Compose for dev (PostgreSQL, RabbitMQ, Keycloak, Jaeger, Prometheus/Grafana).
   - Future Kubernetes deployment with Helm/Kustomize, GitFlow branching, CI/CD pipelines.
   - SLAs: 99.5% API uptime, <5s ingestion-to-visibility latency, <60s analytics refresh.

### Next focus areas (for future sessions/machines)
- Install Rust where needed and run `cargo check` in `gps-ingest-rust`.
- Add repo tooling (.gitignore, .editorconfig, Makefile) and Docker Compose stack.
- Define schema generation workflows (protoc/Avro) and integrate into .NET/Rust builds.
- Flesh out decoder registry, RabbitMQ publisher, and initial .NET service templates.

This document should be kept up to date so any machine/teammate can grasp the context and continue the implementation plan seamlessly.
