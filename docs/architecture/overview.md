# GIS_V2 Architecture Overview

## Vision
GIS_V2 modernises the legacy BeliveGis platform with modular services, real-time telemetry processing, and an open, extensible GPS ingestion pipeline. The goal is to support any tracker model, expose reliable APIs for fleet operations, and streamline deployments through containerisation and GitFlow-driven delivery.

## High-level topology
```
┌──────────┐      ┌───────────────────────┐      ┌────────────────────┐
│ GPS HW   │ ---> │ Rust GPS Ingest Layer │ ---> │ RabbitMQ Exchange  │
└──────────┘      └───────────────────────┘      └────────────────────┘
       │                         │                         │
       │                         ▼                         ▼
       │                ┌────────────────┐        ┌────────────────────┐
       │                │ Command APIs   │        │ Query APIs         │
       │                │ (.NET 8, CQRS) │        │ (.NET 8, CQRS)     │
       │                └────────────────┘        └────────────────────┘
       │                         │                         │
       │                         ▼                         ▼
       │                 PostgreSQL Cluster        Projections/Views
       │                         │                         │
       ▼                         ▼                         ▼
   Angular 17+ Frontend  <----  API Gateway  ---->  Observability Stack
```

- **Rust GPS Ingest Layer**: protocol-agnostic decoder with plugin traits per device family, publishing normalized telemetry events.
- **RabbitMQ**: separates ingestion from business services; enables scaling per workload.
- **.NET 8 Microservices**: CQRS split between command stacks (writes, validations) and query stacks (read-optimized projections).
- **Angular Frontend**: dashboards, planning, analytics; communicates via secured APIs/SignalR.
- **Observability**: OpenTelemetry (traces), Prometheus (metrics), Loki/ELK (logs).

## Service boundaries
| Service | Responsibility | Tech |
|---------|----------------|------|
| `gps-ingest-rust` | Multi-protocol frame decoding, validation, enrichment, publishing to RabbitMQ | Rust, Tokio, tonic/axum for control plane |
| `tracking-command-api` | Commands for trips, stops, vehicle telemetry, alerts | .NET 8, ASP.NET Core MVC, MediatR |
| `tracking-query-api` | Read models for dashboards, historical queries | .NET 8, Dapper/EF Core |
| `fuel-command-api` | Fuel events (consumption, refuel, anomalies) | .NET 8 |
| `fuel-query-api` | Fuel analytics & reporting views | .NET 8 |
| `shared-kernel` | Contracts, shared domain primitives, message schemas | .NET class library + protobuf/avro defs |
| `angular-app` | UX: monitoring, reporting, admin | Angular 17+, NgRx/Signals |

## Cross-cutting concerns
- **Security**: OAuth2/OIDC (Keycloak or Entra ID) with API gateway enforcing scopes.
- **Resilience**: Retry policies, circuit breakers (Polly), idempotent command handlers.
- **Data**: PostgreSQL primary store, timescale partitions for telemetry, separate OLAP warehouse (future).
- **DevOps**: Docker Compose for dev parity, GitFlow branching, CI pipelines building containers and publishing Helm charts.
- **SLA targets** (draft): 99.5% uptime for APIs, <5s ingestion-to-availability latency for telemetry, <60s for analytics updates.
