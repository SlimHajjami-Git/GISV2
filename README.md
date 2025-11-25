# GIS_V2

Modern reimplementation of the BeliveGis platform using microservices, Rust-based GPS ingestion, .NET 8 backends (CQRS), Angular 17+ frontend, and cloud-ready DevOps practices.

## Repository layout
- `services/` – backend services (Rust ingest, .NET APIs, shared kernel).
- `frontend/` – Angular workspace.
- `infra/` – Docker Compose, Kubernetes manifests, IaC.
- `docs/` – architecture, data models, protocol specs, roadmap.
- `tooling/` – shared config, scripts, CI pipelines.

## Quick start (coming soon)
1. Install prerequisites: Rust stable, .NET SDK 8, Node 20, Docker.
2. Run `docker compose -f infra/docker-compose.dev.yaml up -d`.
3. Launch services via `make dev`.
