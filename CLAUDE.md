# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

GISV2 is a full-stack fleet management SaaS platform (GPS tracking, geofences, maintenance, reports). It is a multi-service monorepo:

- **`services/gis-frontend/`** — Angular 21 web app
- **`services/gis-mobile/`** — Ionic 8 / Capacitor mobile app (Android/iOS)
- **`services/GisAPI/`** — ASP.NET Core 9 REST API (entry point)
- **`services/src/`** — .NET layered architecture (Domain / Application / Infrastructure / Shared)
- **`services/gps-ingest-rust/`** — Rust TCP/UDP GPS telemetry ingest service
- **`services/shared-kernel/`** — Protocol Buffers for inter-service messaging
- **`migrations/`** — PostgreSQL schema migrations

## Commands

### Local Dev (Docker)
```bash
docker-compose up -d          # Start full stack (Postgres, RabbitMQ, Redis, Valhalla, Nominatim, Rust ingest, .NET API, Angular)
docker-compose down
docker-compose logs -f gis-api
```

### Frontend (Angular 21)
```bash
cd services/gis-frontend
npm install
npm start                     # ng serve → http://localhost:4200
npm run build                 # Production build
```

### Backend (.NET 9)
```bash
cd services/GisAPI
dotnet run                    # API on http://localhost:5000
dotnet build
dotnet test ../GisAPI.Tests/  # Run backend tests
```

### EF Core Migrations
```bash
cd services/GisAPI
dotnet ef migrations add <Name> --project ../src/GisAPI.Infrastructure
dotnet ef database update
```

### Rust GPS Ingest
```bash
cd services/gps-ingest-rust
cargo build
cargo run
```

## Architecture Overview

### Backend: CQRS with MediatR
The .NET backend follows a strict layered architecture:

1. **Domain** (`src/GisAPI.Domain/`) — Entities, value objects, domain events (defined but not yet dispatched through MediatR pipeline). Core entities: `Vehicle`, `Position`, `Geofence`, `Notification`, `Maintenance`.
2. **Application** (`src/GisAPI.Application/`) — MediatR Commands/Queries organized under `Features/`. Pipeline behaviors handle validation, logging, and authorization.
3. **Infrastructure** (`src/GisAPI.Infrastructure/`) — EF Core (`GisDbContext`), RabbitMQ consumers, Redis cache/pubsub, multi-tenancy (`TenantService`).
4. **API** (`services/GisAPI/Controllers/`) — 48 thin controllers that dispatch MediatR requests. No business logic here.

Multi-tenancy is enforced via `companyId` query filters on `GisDbContext` — all queries are automatically scoped to the current tenant.

### Frontend: Service-Oriented Angular
No NgRx/Redux. State flows through:
- **`ApiService`** (`services/api.service.ts`, ~112KB) — Single service containing ALL HTTP calls. If adding a new API endpoint, it goes here.
- **`AuthService`** — JWT state via `BehaviorSubject<AuthUser>`. Tokens stored in `localStorage` (`auth_token`, `refresh_token`, `user_data`).
- **`SignalrService`** — Real-time GPS updates via SignalR hub at `/api/hubs/gps`.
- **`PermissionService`** — Subscription-based feature gating. Routes use `FeatureGuard` with `ModuleKey` strings.

The `authInterceptor` handles automatic token refresh on 401 responses and proactive refresh when token is near expiry.

### Real-Time Data Flow
```
GPS Device → Rust Ingest (TCP/UDP) → PostgreSQL + RabbitMQ
                                               ↓
                              GpsTelemetryConsumer (.NET) → Redis PubSub
                                               ↓
                              SignalR Hub → Angular frontend
```

### Route/Permission System
Routes are protected by two guards:
- `AuthGuard` — checks `AuthService.isAuthenticated()`
- `FeatureGuard` — checks `PermissionService.hasFeature(moduleKey)` against subscription features

When adding new routes, follow the pattern in `app.routes.ts` with the appropriate `canActivate` guards and `data: { feature: 'module_key' }`.

## Key Service Ports (Docker)

| Service | Port |
|---------|------|
| Angular frontend | 4200 |
| .NET API | 5000 |
| PostgreSQL | 5432 |
| RabbitMQ management | 15672 |
| Redis | 6379 |
| Valhalla (road routing) | 8002 |
| Nominatim (geocoding) | 8088 |
| GPS Ingest TCP (NEMS L) | 6100 |
| GPS Ingest TCP (NEMS S) | 6200 |
| GPS Ingest UDP (Noron) | 6300 |

## Important Patterns

### Adding a New Backend Feature
1. Add entity to `GisAPI.Domain/Entities/`
2. Add Command/Query + Handler under `GisAPI.Application/Features/<Feature>/`
3. Add EF Core configuration in `GisAPI.Infrastructure/Persistence/`
4. Add a thin controller in `GisAPI/Controllers/`
5. Run EF migration

### Adding a New Frontend Module
1. Create component under `src/components/`
2. Add route(s) to `app.routes.ts` with guards
3. Add HTTP methods to `ApiService`
4. Add `ModuleKey` to `PermissionService` if subscription-gated

### Excel/PDF Exports
Backend uses **ClosedXML** for Excel and jsPDF (frontend) for PDFs. Existing report controllers in `ReportsController.cs` show the pattern.

### AI Chat
`AiChatController.cs` + `GroqLlmService.cs` integrate with Groq API (`llama-3.3-70b-versatile`). Context is built from fleet data before sending to the LLM.
