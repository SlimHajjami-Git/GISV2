# GIS_V2 Progress Log

## 2025-11-25 (Session 1)
### Actions completed
1. **Repo bootstrap**
   - Created `GIS_V2/` structure with `services`, `frontend`, `infra`, `docs`, `tooling`.
   - Initialised Git repository.
   - Added root `README.md` outlining project vision + layout.

2. **Documentation**
   - `docs/architecture/overview.md`: macro architecture, service boundaries, cross-cutting concerns.
   - `docs/architecture/roadmap.md`: phase-by-phase plan (foundations → ingestion → .NET services → Angular → DevOps).
   - `docs/db-schema/initial-model.md`: first draft of entities (vehicles, trips, stops, fuel, telemetry, projections).
   - `docs/protocols/gps-decoding.md`: Rust plugin strategy, RabbitMQ topics, observability plan.

3. **Shared kernel scaffolding**
   - Created folders `services/shared-kernel/{proto,avro,dotnet,rust}`.
   - Added `proto/telemetry.proto` defining `NormalizedTelemetry`, `Position`, `VehicleState`, `Alert` messages.

4. **Rust GPS ingest service**
   - Since `cargo` unavailable on current machine, manually created `services/gps-ingest-rust/` with `Cargo.toml` and `src/main.rs` (Tokio async main + tracing setup). Ready to run on machine with Rust installed.

### Pending / Next steps
1. Install Rust toolchain on dev machine (or switch to machine where it exists) and run `cargo check` inside `services/gps-ingest-rust`.
2. Add `.gitignore`, `.editorconfig`, and tooling scripts (e.g., Makefile, lint hooks).
3. Create `infra/docker-compose.dev.yaml` for PostgreSQL, RabbitMQ, Keycloak, Jaeger, Prometheus/Grafana.
4. Define code generation workflow for shared kernel (protoc/Buf, Avro schemas) and expose packages to .NET/Rust services.
5. Expand `gps-ingest-rust` with configuration loader, RabbitMQ publisher, and decoder registry.
