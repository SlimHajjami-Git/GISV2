# Analyse Complète du Projet GISV2

## Vue d'ensemble

**GISV2** est un **système de gestion de flotte véhiculaire (Fleet Management System)** multi-tenant, conçu pour les entreprises de transport et de location au Maghreb (Tunisie/Maroc). Il couvre le suivi GPS temps réel, la maintenance préventive, la gestion des coûts, le comportement de conduite, le géofencing, et la gestion administrative complète.

---

## 1. Architecture Globale

```
┌─────────────────────────────────────────────────────────────────┐
│                        CLIENTS                                   │
│   Angular Frontend (Nginx :4200)  ←→  Admin Panel (SPA)         │
└──────────────┬──────────────────────────────────────────────────┘
               │ HTTP / WebSocket (SignalR)
               ▼
┌──────────────────────────────────────────────────────────────────┐
│                    .NET 8 API (:5000)                             │
│  ┌──────────┐ ┌──────────┐ ┌────────────┐ ┌──────────────────┐  │
│  │Controllers│ │ MediatR  │ │ SignalR Hub│ │   Middleware      │  │
│  │ (44 ctrl) │ │  CQRS    │ │  GpsHub    │ │ Tenant/Auth/Perm │  │
│  └──────────┘ └──────────┘ └────────────┘ └──────────────────┘  │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │                  Clean Architecture                       │    │
│  │  Domain → Application (Features) → Infrastructure         │    │
│  └──────────────────────────────────────────────────────────┘    │
└──────┬───────────┬──────────────┬───────────────┬───────────────┘
       │           │              │               │
       ▼           ▼              ▼               ▼
┌──────────┐ ┌──────────┐ ┌──────────┐    ┌──────────────┐
│PostgreSQL│ │  Redis   │ │ RabbitMQ │    │  Valhalla    │
│  PostGIS │ │  Cache   │ │ Messaging│    │  Nominatim   │
│  (:5433) │ │ (:6379)  │ │ (:5673)  │    │  (Geo svc)   │
└──────────┘ └──────────┘ └────┬─────┘    └──────────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │   Rust GPS Ingest   │
                    │  TCP :6100/6200/6210│
                    │  HH + AAP Protocols │
                    └─────────────────────┘
                               ▲
                          GPS Trackers
                        (Concox, ACI...)
```

### Stack Technologique
| Couche | Technologie | Version |
|--------|-------------|---------|
| **Backend API** | .NET 8, C# | ASP.NET Core |
| **Frontend** | Angular 17+ | Standalone Components |
| **GPS Ingestion** | Rust | Tokio async runtime |
| **Base de données** | PostgreSQL 16 | + PostGIS (spatial) |
| **Cache temps réel** | Redis 7 | Pub/Sub + Cache |
| **Messaging** | RabbitMQ 3.13 | Topic/Fanout exchanges |
| **Road Matching** | Valhalla | Map matching GPS → routes |
| **Geocoding** | Nominatim | Reverse geocoding (coords → adresses) |
| **Conteneurisation** | Docker Compose | 7 services |

---

## 2. Backend .NET — Clean Architecture

### 2.1 Structure des couches

```
services/
├── GisAPI/                          # Couche Présentation
│   ├── Controllers/ (44 fichiers)   # Endpoints REST
│   ├── Hubs/GpsHub.cs               # SignalR temps réel
│   ├── Middleware/                   # Auth, Tenant, Permissions, Exceptions
│   ├── Services/                    # Services d'infrastructure (Redis, RabbitMQ, Geocoding)
│   └── Program.cs                   # Bootstrap, DI, Seed
│
└── src/
    ├── GisAPI.Domain/               # Couche Domaine (0 dépendance externe)
    │   ├── Entities/ (38 entités)   # Modèles métier
    │   ├── Enums/                   # Types énumérés
    │   ├── Events/                  # Domain Events (définis mais pas dispatchés)
    │   ├── Common/                  # Entity, AuditableEntity, TenantEntity
    │   └── Interfaces/              # Abstractions (IGeocodingService, etc.)
    │
    ├── GisAPI.Application/          # Couche Application (CQRS)
    │   ├── Features/ (27 modules)   # Commands & Queries via MediatR
    │   ├── Common/                  # Behaviours (Validation, Logging, Auth)
    │   │   └── Interfaces/          # IGisDbContext, IJwtService, IPasswordHasher...
    │   └── Services/                # MaintenanceSchedulerService
    │
    └── GisAPI.Infrastructure/       # Couche Infrastructure
        ├── Persistence/             # GisDbContext, Configurations EF Core
        │   ├── GisDbContext.cs       # 100+ DbSets, query filters multi-tenant
        │   ├── Configurations/       # Fluent API entity configs
        │   └── Migrations/           # EF Core migrations
        ├── MultiTenancy/            # CurrentTenantService
        ├── Messaging/               # RabbitMQ (MessageBus, Consumer)
        └── Services/                # JWT, PasswordHasher, Permissions
```

### 2.2 Entités Principales (Domain)

**Hiérarchie de base** : `Entity` (Id) → `AuditableEntity` (CreatedAt/UpdatedAt) → `TenantEntity` (CompanyId)

| Entité | Rôle | Relations clés |
|--------|------|----------------|
| **Societe** | Tenant racine (multi-entreprise) | Users, Vehicles, Roles, SubscriptionType |
| **User** | Utilisateur (admin, chauffeur, etc.) | Role, Company, AssignedVehicles |
| **Driver** | Chauffeur (entité séparée) | User (FK), AssignedVehicle, Permit info |
| **Role** | Rôle avec permissions JSONB | Societe, Users, IsCompanyAdmin |
| **Vehicle** | Véhicule avec documents | GpsDevice, Driver, Department, Costs |
| **GpsDevice** | Boîtier GPS (IMEI) | Vehicle, Positions, Alerts |
| **GpsPosition** | Position GPS (BIGINT id) | Device, Lat/Lng, Speed, Ignition, MEMS, FMS |
| **Trip** | Trajet calculé | Vehicle, Waypoints, Distance, Fuel, Events |
| **Geofence** | Zone géographique | Polygon/Circle, Vehicles, Events, AlertRules |
| **MaintenanceTemplate** | Modèle d'entretien | Intervalles km/mois, Seuils alerte, Parts |
| **VehicleMaintenanceSchedule** | Planning entretien/véhicule | Template, Vehicle, Status, NextDue |
| **VehicleCost** | Coût (carburant, assurance...) | Vehicle, Type, Amount |
| **AccidentClaim** | Sinistre | Vehicle, ThirdParties, Documents |
| **Notification** | Notification multi-canal | Type, Priority, Channel, IsRead, Metadata |
| **SubscriptionType** | Plan d'abonnement | Limites (véhicules, users), Modules, Features |
| **VehicleStop** | Arrêt véhicule | Duration, FuelLevels, Geofence |
| **FuelRecord** | Historique carburant | Vehicle, Consumption, Anomaly detection |
| **DailyStatistics** | Stats journalières | Vehicle, Distance, Fuel, Events count |

### 2.3 CQRS avec MediatR

**27 modules Features**, chacun avec Commands et Queries séparés :

```
Features/
├── Auth/           → Login, Register, RefreshToken
├── Users/          → CRUD users, AssignVehicles
├── Drivers/        → CreateDriver (User+Driver atomique), Update, Delete
├── Vehicles/       → CRUD véhicules, Documents, AssignGPS
├── Gps/            → GetPositions, GetHistory, GetRealTimePositions (Redis)
├── Reports/        → TripReport, FuelReport, SpeedReport, MileageReport, DailyActivity, MonthlyFleet
├── Dashboard/      → GetDashboardStats (KPIs)
├── Alerts/         → GetAlerts, CreateAlert
├── Geofences/      → CRUD zones, AssignVehicles
├── MaintenanceTemplates/ → CRUD modèles entretien
├── VehicleMaintenance/   → Schedules, Logs, Notifications
├── FuelEntries/    → Saisie carburant manuelle
├── FuelExpenses/   → Statistiques dépenses carburant
├── AccidentClaims/ → Sinistres avec documents
├── FleetManagement/→ Vue flotte globale
├── Subscriptions/  → Gestion abonnements
├── Admin/          → Gestion système (sociétés, users globaux)
└── ... (Suppliers, Documents, Repairs, VehicleStops, etc.)
```

**Pipeline Behaviours MediatR** :
1. **ValidationBehaviour** — Validation FluentValidation avant exécution
2. **LoggingBehaviour** — Logging de chaque commande/query
3. **AuthorizationBehaviour** — Vérification permissions via `IRequiresPermissions`

### 2.4 Controllers (44)

Les controllers les plus importants :

| Controller | Endpoints | Description |
|-----------|-----------|-------------|
| **AuthController** | Login, Register, Refresh | JWT auth, BCrypt password |
| **GpsController** | Positions temps réel, historique, playback | Redis cache + DB fallback |
| **VehiclesController** | CRUD complet + documents | Multi-tenant filtré |
| **DriversController** | CRUD (crée User+Driver atomique) | Auto-création rôle "Chauffeur" |
| **GeofencesController** | Zones + véhicules assignés | Polygon/Circle |
| **MaintenanceTemplatesController** | Modèles + plannings | Intervalles km/mois |
| **ReportsController** | 10+ types de rapports | Trips, fuel, speed, mileage |
| **DashboardController** | KPIs, stats | Résumé flotte |
| **AdminController** | Gestion système | Sociétés, abonnements |
| **NotificationsController** | CRUD notifications | Multi-canal |

### 2.5 Middleware Pipeline

```
Request → ExceptionHandling → CORS → Auth → Authorization → PermissionMiddleware → TenantMiddleware → Controller
```

1. **ExceptionHandlingMiddleware** — Catch global, mappe DomainException → HTTP codes
2. **PermissionMiddleware** — Vérifie accès system admin pour `/api/admin`
3. **TenantMiddleware** — Extrait `companyId` du JWT et set `ICurrentTenantService`

### 2.6 Services d'Infrastructure

| Service | Rôle |
|---------|------|
| **RedisCacheService** | Cache positions temps réel, pub/sub |
| **RedisPubSubConsumer** | Écoute Redis → push SignalR (faible latence) |
| **GpsTelemetryConsumer** | Écoute RabbitMQ → MediatR → SignalR |
| **GeocodingService** | Nominatim reverse geocoding avec cache |
| **ValhallaService** | Road matching (GPS → route réelle) |
| **GpsInterpolationService** | Interpolation intelligente des points GPS |
| **DrivingBehaviorService** | Détection freinage brusque, accélération, etc. |
| **NotificationService** | Envoi notifications multi-canal |
| **MaintenanceSchedulerService** | Calcul échéances entretien |

### 2.7 SignalR Hub (Temps Réel)

Le `GpsHub` gère le push temps réel avec isolation multi-tenant :

- **Groupes** : `company_{id}`, `vehicle_{id}`, `geofence_{id}`, `user_{id}`
- **Events émis** : `PositionUpdate`, `VehiclePosition`, `Alert`, `GeofenceEvent`, `NewNotification`, `UnreadCountChanged`
- **Auth** : JWT via query string pour WebSocket

### 2.8 Multi-Tenancy

Isolation automatique par `CompanyId` :
- **JWT Claims** : `companyId`, `userId`, `permissions`
- **ICurrentTenantService** : Injecté dans chaque request, set par TenantMiddleware
- **Query Filters EF Core** : `HasQueryFilter(e => e.CompanyId == tenantService.CompanyId)` sur ~30 entités
- **Résultat** : Aucune donnée d'une société visible par une autre

### 2.9 Système de Permissions

Architecture à 3 niveaux :
1. **SubscriptionType** — Plan d'abonnement (limites : maxVehicles, maxUsers, modules activés, features)
2. **Role.Permissions** — JSONB avec permissions granulaires par module
3. **IsCompanyAdmin / IsSystemRole** — Bypass permissions pour admins

---

## 3. Frontend Angular

### 3.1 Architecture

- **Angular 17+** avec **Standalone Components** (pas de NgModules)
- **Pas de state management global** (RxJS BehaviorSubjects dans les services)
- **Styling** : CSS inline dans chaque composant (pas de framework CSS global)

### 3.2 Structure

```
gis-frontend/src/
├── components/              # 40+ composants standalone
│   ├── landing.component.ts     # Page d'accueil publique
│   ├── login.component.ts       # Authentification
│   ├── dashboard.component.ts   # KPIs et graphiques
│   ├── monitoring.component.ts  # 90KB — Carte Leaflet, playback GPS, road matching
│   ├── vehicles.component.ts    # 70KB — Gestion véhicules + documents
│   ├── reports.component.ts     # 170KB — 10+ types de rapports
│   ├── geofences.component.ts   # 54KB — Dessin zones sur carte
│   ├── maintenance-templates.component.ts  # 82KB — Entretiens programmables
│   ├── user-management.component.ts  # 58KB — Gestion users et rôles
│   ├── employees.component.ts   # Page chauffeurs (Driver API)
│   ├── fleet-management.component.ts  # Gestion flotte globale
│   ├── expenses.component.ts    # Dépenses
│   ├── carburant.component.ts   # Gestion carburant
│   ├── accident-claims.component.ts  # Sinistres
│   └── shared/                  # Composants partagés (popups, layout)
│       ├── app-layout.component.ts  # Navigation + layout principal
│       ├── employee-popup.component.ts  # Popup ajout/édition chauffeur
│       └── maintenance-popup.component.ts
│
├── admin/                   # Panel admin système (séparé)
│   ├── admin.routes.ts
│   └── pages/ (15 pages)   # Gestion sociétés, subscriptions, users globaux
│
├── services/
│   ├── api.service.ts       # 99KB — TOUTES les API calls (~300+ méthodes)
│   ├── auth.service.ts      # JWT auth + mock user + refresh token
│   ├── signalr.service.ts   # WebSocket GPS temps réel
│   ├── permission.service.ts # Vérification accès modules/features
│   └── mock-data.service.ts  # Données mock pour dev
│
├── guards/
│   ├── auth.guard.ts        # Vérifie authentification
│   ├── feature.guard.ts     # Vérifie accès module (subscription)
│   └── system-admin.guard.ts # Vérifie admin système
│
├── models/types.ts          # 320+ lignes — Interfaces TypeScript
└── app.routes.ts            # 118 lignes — Toutes les routes
```

### 3.3 Routes (30+ pages)

Routes protégées par `AuthGuard` + `FeatureGuard` avec feature data :
- `/dashboard` — Tableau de bord KPIs
- `/monitoring` — Carte temps réel + playback GPS
- `/vehicles` — Gestion véhicules
- `/drivers` — Gestion chauffeurs
- `/geofences` — Zones géographiques
- `/reports` — Rapports complets (10+ types)
- `/maintenance-templates` — Entretiens programmables
- `/costs`, `/expenses`, `/carburant` — Gestion financière
- `/users` — Gestion utilisateurs et rôles
- `/suppliers` — Fournisseurs
- `/documents` — Échéances documents véhicules
- `/accidents` — Sinistres
- `/fleet-management` — Vue flotte globale
- `/admin/*` — Panel admin système (15 pages)

### 3.4 Services Clés

**ApiService** (~99KB, ~300 méthodes) :
- Gestion automatique des headers JWT
- Mock user detection (pas d'appels API en mode mock)
- Endpoints pour chaque module : vehicles, drivers, GPS, reports, maintenance, etc.

**AuthService** :
- Login avec JWT + refresh token
- Mock user (`admin@test.com` / `admin`) pour développement sans backend
- Stockage localStorage (token, refresh_token, user data)
- Subscription features incluses dans le token

**SignalRService** :
- WebSocket via `/api/hubs/gps` avec JWT auth
- Auto-reconnexion avec backoff exponentiel (max 10 tentatives)
- Subjects RxJS : `positionUpdate$`, `alert$`, `geofenceEvent$`, `notification$`
- Subscription par véhicule avec resubscription automatique après reconnexion

**PermissionService** :
- Map modules → features d'abonnement
- Vérification accès basée sur : rôle user, subscription company, type company

### 3.5 Composants Majeurs

**MonitoringComponent (90KB)** — Le cœur du système :
- Carte Leaflet avec marqueurs véhicules en temps réel
- Playback GPS avec timeline et contrôles (vitesse, pause)
- Road matching Valhalla (GPS → route réelle)
- Cluster de marqueurs pour vue globale
- Panneau latéral avec détails véhicule

**ReportsComponent (170KB)** — Le plus gros composant :
- 10+ types de rapports : Trips, Fuel, Speed, Mileage, Stops, Costs, Maintenance, Daily Activity, Monthly Fleet, Speed Infraction, Driving Behavior
- Export PDF/Excel
- Filtres par véhicule, période, type

---

## 4. Service Rust — GPS Ingestion

### 4.1 Rôle

Service **haute performance** dédié à l'ingestion de trames GPS brutes depuis les boîtiers tracker. Écrit en Rust pour :
- Performance TCP (milliers de connexions simultanées)
- Parsing binaire de protocoles GPS propriétaires
- Faible latence de bout en bout

### 4.2 Architecture

```
gps-ingest-rust/src/
├── main.rs           # Bootstrap, CLI (--calculate-daily)
├── config.rs         # Chargement listeners.yaml
├── transport.rs      # 40KB — TCP listeners, frame parsing
├── telemetry/
│   ├── hh.rs         # 24KB — Protocole HH (Concox GT06N)
│   └── aap.rs        # 36KB — Protocole AAP/ACI (V1, V2, V3)
├── db.rs             # 30KB — PostgreSQL persistence (sqlx)
├── redis_cache.rs    # Cache positions temps réel + pub/sub
├── publisher.rs      # RabbitMQ publishing
├── ports.rs          # Traits (TelemetryStore, TelemetryEventPublisher)
└── services/
    └── daily_statistics.rs  # Calcul stats journalières
```

### 4.3 Protocoles GPS Supportés

**Protocole HH (gps_type_1)** — Port 6100 :
- Boîtiers Concox GT06N
- Trames info (IMEI, SIM) + trames télémétrie (position, vitesse, ignition)
- Format hexadécimal

**Protocole AAP/ACI (gps_type_2/2_1)** — Ports 6200, 6210 :
- Versions V1, V2, V3
- Coordonnées NMEA, vitesse, heading
- MEMS accéléromètre (X/Y/Z)
- FMS data : fuel, RPM, température moteur, odomètre
- Détection événements : freinage brusque, accélération, virages serrés, dos d'âne, nids de poule, excès de vitesse
- **Send flags** : SENDP (périodique), CAPDEV (changement cap), IOCHANGE (ignition), OVERSPEED, JERCK, etc.

### 4.4 Flux de Données

```
GPS Tracker → TCP (Rust) → Parse Frame → PostgreSQL (gps_positions)
                                       → Redis Cache (position temps réel)
                                       → Redis Pub/Sub → .NET RedisPubSubConsumer → SignalR → Frontend
                                       → RabbitMQ → .NET GpsTelemetryConsumer → SignalR → Frontend
```

**Double canal temps réel** :
1. **Redis Pub/Sub** — Latence minimale (~ms) pour positions temps réel
2. **RabbitMQ** — Pour traitement asynchrone (alertes, geofencing, etc.)

### 4.5 CLI

```bash
gps-ingest-rust                           # Service d'ingestion (défaut)
gps-ingest-rust --calculate-daily         # Stats journalières (hier)
gps-ingest-rust -d 2026-01-20             # Stats pour date spécifique
```

---

## 5. Infrastructure Docker

### 5.1 Services (7 conteneurs)

| Service | Image | Port | Rôle |
|---------|-------|------|------|
| **postgres** | postgis/postgis:16 | 5433 | BDD principale + spatial |
| **redis** | redis:7-alpine | 6379 | Cache temps réel + pub/sub |
| **rabbitmq** | rabbitmq:3.13-management | 5673/15673 | Messaging async |
| **gps-ingest** | Custom (Rust) | 3000/6100/6200/6210 | Ingestion GPS TCP |
| **gis-api** | Custom (.NET 8) | 5000 | API REST + SignalR |
| **frontend** | Custom (Angular+Nginx) | 4200 | SPA + proxy API |
| **valhalla** | docker-valhalla | 8002 | Road matching |
| **nominatim** | mediagis/nominatim | 8088 | Reverse geocoding |

### 5.2 Healthchecks

Tous les services ont des healthchecks Docker :
- PostgreSQL : `pg_isready`
- Redis : `redis-cli ping`
- RabbitMQ : `rabbitmq-diagnostics ping`
- GPS Ingest : `curl http://localhost:3000/health`
- GIS API : `curl http://localhost:5000/health`
- Frontend : `curl http://localhost:80`

### 5.3 Dépendances

```
postgres ──┬── gps-ingest (depends: postgres, rabbitmq, redis)
redis   ───┤
rabbitmq ──┘
           ├── gis-api (depends: postgres, rabbitmq, redis)
           └── frontend (depends: gps-ingest, gis-api)
```

### 5.4 Volumes Persistants

- `pgdata` — Données PostgreSQL
- `redis-data` — Données Redis (AOF)
- `valhalla-data` — Tuiles routières Valhalla
- `nominatim-data` — Données géographiques Nominatim

---

## 6. Base de Données

### 6.1 Tables Principales (~50+)

**Core** : `societes`, `users`, `drivers`, `roles`, `subscription_types`, `refresh_tokens`

**Véhicules** : `vehicles`, `vehicle_documents`, `vehicle_assignments`, `driver_assignments`, `driver_scores`, `user_vehicles`, `departments`

**GPS** : `gps_devices`, `gps_positions` (BIGINT id, indexé device_id + recorded_at), `gps_alerts`

**Trajets** : `trips`, `trip_waypoints`, `driving_events`, `daily_statistics`, `vehicle_stops`

**Géofences** : `geofences`, `geofence_vehicles`, `geofence_events`, `geofence_groups`

**Maintenance** : `maintenance_templates`, `vehicle_maintenance_schedules`, `maintenance_logs`, `maintenance_notifications`, `maintenance_alert_settings`, `maintenance_records`, `maintenance_parts`

**Financier** : `vehicle_costs`, `fuel_entries`, `fuel_types`, `fuel_pricings`, `fuel_records`

**Autres** : `accident_claims` + third_parties + documents, `suppliers`, `contracts`, `reservations`, `notifications`, `audit_logs`, `reports`, `report_schedules`, `brands`, `vehicle_models`, `repairs`

### 6.2 Index Critiques

- `gps_positions(device_id, recorded_at)` — Performance requêtes historique
- `drivers(user_id)` UNIQUE — Un driver par user
- Query filters automatiques par `company_id` sur toutes les tables tenant

---

## 7. Flux Métier Clés

### 7.1 Suivi GPS Temps Réel
```
Tracker → TCP Frame → Rust Parse → DB Insert + Redis Cache + RabbitMQ/Redis Pub
                                                              ↓
Frontend ← SignalR WebSocket ← .NET Consumer ← Redis Pub/Sub
```

### 7.2 Playback Historique
```
Frontend → GET /api/gps/vehicles/{id}/history?from=&to= → DB Query (gps_positions) → PositionDto[]
         → Option Valhalla road matching → Points snappés sur routes
```

### 7.3 Géofencing
```
Position entrante → Vérification intersection avec Geofences → GeofenceEvent créé → SignalR Alert
```

### 7.4 Maintenance Préventive
```
Template (huile: 10000km/6mois) → Assigné à véhicule → Schedule créé
Kilométrage avance → Seuils Warning/Critical → Notification → Réalisation → Log + Reset
```

### 7.5 Création Chauffeur
```
Page Chauffeurs → Popup (prénom, nom, email, mdp, permis, CIN, véhicule)
→ POST /api/drivers → Crée User (rôle "Chauffeur" auto) + Driver (atomique)
```

---

## 8. Système d'Abonnements

Plans contrôlent l'accès aux fonctionnalités :

| Feature | Description |
|---------|-------------|
| `maxVehicles/Users/GpsDevices/Geofences` | Limites quantitatives |
| `moduleDashboard/Monitoring/Vehicles/etc.` | Modules activés/désactivés |
| `gpsTracking/historyPlayback/fuelAnalysis` | Features granulaires |
| `reportTrips/reportFuel/reportSpeed/etc.` | Rapports par type |
| `historyRetentionDays` | Durée conservation historique |

---

## 9. Sécurité

- **JWT Authentication** avec refresh tokens
- **BCrypt** pour hashing mots de passe
- **Multi-tenant isolation** automatique via EF Core query filters
- **Permission middleware** pour routes admin
- **CORS** configuré (actuellement AllowAnyOrigin en dev)
- **SignalR** auth via query string JWT pour WebSocket

---

## 10. Points Forts ✅

1. **Architecture Clean solide** — Séparation Domain/Application/Infrastructure respectée
2. **CQRS bien structuré** — 27 modules Features avec MediatR
3. **Multi-tenancy robuste** — Query filters automatiques, impossible de voir les données d'un autre tenant
4. **GPS ingestion performant** — Rust async pour TCP haute performance
5. **Double canal temps réel** — Redis (latence) + RabbitMQ (fiabilité)
6. **Maintenance préventive complète** — Templates, schedules, notifications, logs
7. **Système de rapports riche** — 10+ types avec export
8. **Infrastructure Docker complète** — Healthchecks, dépendances ordonnées
9. **Géoservices intégrés** — Valhalla (road matching) + Nominatim (geocoding)

---

## 11. Points à Améliorer 🔴

### Architecture
1. **Domain Events définis mais JAMAIS dispatchés** — Les events dans `DomainEvent.cs` ne sont jamais émis
2. **Pas de transactions explicites** — Certaines opérations multi-table risquent des incohérences
3. **ApiService monolithique** — 99KB, ~300 méthodes dans un seul fichier

### Frontend
4. **Composants très volumineux** — `reports.component.ts` (170KB), `monitoring.component.ts` (90KB) → difficiles à maintenir
5. **Pas de state management** — Pas de NgRx/Signal Store, tout en BehaviorSubject
6. **CSS inline** — Pas de design system partagé, styles dupliqués entre composants
7. **Mock user hardcodé** — `admin@test.com` dans le code de production

### Backend
8. **Seed data dans Program.cs** — 400+ lignes de seed dans le point d'entrée
9. **EF Migrations manuelles** — Certaines migrations sont des fichiers SQL bruts plutôt que des vraies migrations EF
10. **Pas de tests unitaires significatifs** — Le dossier `tests/` existe mais est minimal

### Sécurité
11. **CORS AllowAnyOrigin** — Doit être restreint en production
12. **JWT key en config** — La clé doit être dans un secret manager
13. **Pas de rate limiting** — Aucune protection contre les abus

### Performance
14. **Pas de pagination sur certaines requêtes** — `GetDriversQuery` retourne TOUT
15. **N+1 potentiels** — Certaines queries avec Include pourraient être optimisées

---

## 12. Statistiques

| Métrique | Valeur |
|----------|--------|
| **Entités Domain** | 38 |
| **Controllers** | 44 |
| **Features CQRS** | 27 modules |
| **Composants Frontend** | 40+ |
| **Routes Frontend** | 30+ |
| **API Methods** | ~300 |
| **Tables PostgreSQL** | ~50+ |
| **Docker Services** | 7-8 |
| **Protocoles GPS** | 2 (HH, AAP/ACI) |
| **Types de Rapports** | 10+ |
