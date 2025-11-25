# Implementation Roadmap

## Phase 0 – Foundations
1. **Repository hygiene**
   - Initialise Git (GitFlow: `main`, `develop`, feature branches).
   - Configure `.editorconfig`, commit hooks, CI skeleton.
2. **Tooling**
   - Docker Compose stack (PostgreSQL + Timescale, RabbitMQ, Keycloak, Jaeger, Prometheus, Grafana).
   - Base `Makefile` / scripts for build, test, run.
3. **Shared kernel**
   - Define protobuf/Avro schemas for telemetry events.
   - Publish NuGet/crate packages for domain primitives.
4. **Security & Observability**
   - Standup Keycloak realm, define OAuth2 clients/scopes.
   - Configure OpenTelemetry exporters + tracing IDs conventions.

## Phase 1 – GPS Ingestion Service
- Scaffold `gps-ingest-rust` (Tokio runtime, structured logging, OTEL).
- Implement plugin registry + first decoders (NR024, AAP, GT60, TK102/103).
- Integrate RabbitMQ publisher, dead-letter handling, health endpoints.
- Add integration tests with Dockerised dependencies.

## Phase 2 – Tracking Domain (.NET 8)
- Scaffold `tracking-command-api` and `tracking-query-api` (DDD + CQRS + MediatR).
- Implement EF Core context, migrations (vehicles, devices, trips, telemetry points, stops).
- Command handlers for trip lifecycle, stop detection jobs (background consumers from telemetry topics).
- Query projections exposed via REST (with pagination, filtering).

## Phase 3 – Fuel Domain Services
- Mirror command/query architecture for fuel events.
- Ingest sensor data from telemetry stream, detect anomalies.
- Provide dashboards for consumption KPIs.

## Phase 4 – Frontend (Angular 17+)
- Bootstrap workspace (Nx optional) with module separation: monitoring, planning, admin.
- Implement authentication (OAuth2 PKCE) + role-based routing.
- Real-time dashboards via SignalR/WebSocket to query services.

## Phase 5 – DevOps & Deployment
- CI/CD pipeline: lint/test/build containers, push to registry, Helm chart packaging.
- Kubernetes manifests (namespace per environment, secrets, autoscaling, HPA on telemetry consumers).
- Observability dashboards, alerting rules aligned with SLA (<5s ingestion-to-visibility, 99.5% uptime).

## Phase 6 – Enhancements
- Advanced analytics (geofencing, machine learning anomaly detection).
- Data lake replication for BI tools.
- Support for OTA firmware updates + device command orchestration.
