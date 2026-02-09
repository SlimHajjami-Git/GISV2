# 📊 Rapport d'Analyse Complet - GISV2
## Système de Gestion de Flotte (Fleet Management System)

**Date d'analyse**: 9 Février 2026  
**Version**: GISV2

---

# 1. ARCHITECTURE GLOBALE

## 1.1 Vue d'ensemble

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           GISV2 Architecture                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────┐     ┌─────────────┐     ┌─────────────────────────────┐   │
│  │   Angular   │────▶│  .NET 8 API │────▶│      PostgreSQL 16          │   │
│  │  Frontend   │     │  (GisAPI)   │     │      + PostGIS              │   │
│  │  Port 4200  │     │  Port 5000  │     │      Port 5433              │   │
│  └─────────────┘     └──────┬──────┘     └─────────────────────────────┘   │
│         │                   │                          ▲                    │
│         │            ┌──────┴──────┐                   │                    │
│         │            │  SignalR    │                   │                    │
│         └───────────▶│  WebSocket  │                   │                    │
│                      │  /hubs/gps  │                   │                    │
│                      └─────────────┘                   │                    │
│                                                        │                    │
│  ┌─────────────┐     ┌─────────────┐                   │                    │
│  │   GPS       │────▶│  Rust GPS   │───────────────────┘                    │
│  │  Devices    │TCP  │  Ingest     │                                        │
│  │  (IMEI)     │     │  Port 6100  │──────┐                                 │
│  └─────────────┘     └─────────────┘      │                                 │
│                                           ▼                                 │
│                      ┌─────────────┐  ┌─────────────┐  ┌─────────────┐      │
│                      │   Redis     │  │  RabbitMQ   │  │   OSRM      │      │
│                      │ Real-time   │  │  Messaging  │  │  Routing    │      │
│                      │ Port 6379   │  │ Port 5673   │  │  Port 5001  │      │
│                      └─────────────┘  └─────────────┘  └─────────────┘      │
│                                                                             │
│                      ┌─────────────┐                                        │
│                      │  Nominatim  │                                        │
│                      │  Geocoding  │                                        │
│                      │  Port 8088  │                                        │
│                      └─────────────┘                                        │
└─────────────────────────────────────────────────────────────────────────────┘
```

## 1.2 Stack Technologique

| Couche | Technologie | Version | Rôle |
|--------|-------------|---------|------|
| **Frontend** | Angular | 17+ | UI Standalone Components |
| **Backend API** | .NET | 8.0 | API REST + SignalR |
| **GPS Ingestion** | Rust | Latest | TCP haute performance |
| **Base de données** | PostgreSQL + PostGIS | 16 | Données spatiales |
| **Cache** | Redis | 7 | Positions temps réel |
| **Messaging** | RabbitMQ | 3.13 | Communication async |
| **Routing** | OSRM | Latest | Road snapping |
| **Geocoding** | Nominatim | 4.4 | Reverse geocoding |

---

# 2. BACKEND .NET (Clean Architecture CQRS)

## 2.1 Structure des couches

```
services/
├── GisAPI/                 # Couche Présentation (Controllers, Hubs)
└── src/
    ├── GisAPI.Domain/      # Couche Domaine (Entités, Interfaces)
    ├── GisAPI.Application/ # Couche Application (CQRS, Services)
    └── GisAPI.Infrastructure/ # Couche Infrastructure (EF Core, RabbitMQ)
```

## 2.2 Entités Domain (36 classes)

### 2.2.1 Hiérarchie de base

| Classe | Description |
|--------|-------------|
| `Entity` | Classe de base avec `Id: int` |
| `AuditableEntity` | Hérite de Entity, ajoute `CreatedAt`, `UpdatedAt` |
| `TenantEntity` | Hérite de AuditableEntity, ajoute `CompanyId` (multi-tenant) |
| `LongIdTenantEntity` | Comme TenantEntity mais avec `Id: long` |

### 2.2.2 Entités Principales

#### **Societe** (Société/Entreprise)
```csharp
public class Societe : AuditableEntity
{
    public string Name { get; set; }           // Nom de la société
    public string Type { get; set; }           // transport, location, autre
    public SocieteSettings? Settings { get; set; } // JSONB config (Currency, Timezone, etc.)
    public int? SubscriptionTypeId { get; set; }   // Lien vers le plan d'abonnement
    public DateTime SubscriptionStartedAt { get; set; }
    public DateTime? SubscriptionExpiresAt { get; set; }
    public string SubscriptionStatus { get; set; } // active, expired, etc.
    
    // Navigation
    public ICollection<User> Users { get; set; }
    public ICollection<Vehicle> Vehicles { get; set; }
    public ICollection<GpsDevice> GpsDevices { get; set; }
    public ICollection<Geofence> Geofences { get; set; }
}
```
**Rôle**: Racine du multi-tenant. Chaque société a ses propres utilisateurs, véhicules, etc.

---

#### **User** (Utilisateur)
```csharp
public class User : TenantEntity
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }          // Unique par société
    public string PasswordHash { get; set; }   // BCrypt hash
    public int RoleId { get; set; }            // Lien vers Role
    public string Status { get; set; }         // active, inactive
    
    // Computed
    public bool IsCompanyAdmin => Role?.IsCompanyAdmin ?? false;
    public bool IsSystemAdmin => Role?.IsSystemAdmin ?? false;
    public string[] Permissions => Role?.Permissions...;
}
```
**Rôle**: Authentification et autorisation. Les permissions viennent du `Role` assigné.

---

#### **Role** (Rôle)
```csharp
public class Role : Entity
{
    public string Name { get; set; }           // Ex: "Administrateur", "Chauffeur"
    public int? SocieteId { get; set; }        // null = rôle système
    public bool IsCompanyAdmin { get; set; }   // Admin de la société
    public bool IsSystemRole { get; set; }     // Admin système global
    public Dictionary<string, object>? Permissions { get; set; } // JSONB permissions
}
```
**Rôle**: Définit les permissions. `IsSystemRole=true` donne accès admin global.

---

#### **Vehicle** (Véhicule)
```csharp
public class Vehicle : TenantEntity
{
    public string Name { get; set; }           // Nom/Label du véhicule
    public string Plate { get; set; }          // Immatriculation
    public string Type { get; set; }           // camion, voiture, moto
    public string Brand { get; set; }          // Marque
    public string Model { get; set; }          // Modèle
    public int Mileage { get; set; }           // Kilométrage actuel
    public string Status { get; set; }         // available, in_use, maintenance
    
    public int? GpsDeviceId { get; set; }      // Lien GPS (optionnel)
    public int? AssignedDriverId { get; set; } // Chauffeur assigné
    public int? SpeedLimit { get; set; }       // Limite de vitesse
    public int? FuelTankCapacity { get; set; } // Capacité réservoir (L)
    
    // Documents expiration
    public DateTime? InsuranceExpiry { get; set; }
    public DateTime? TechnicalInspectionExpiry { get; set; }
    
    // Navigation
    public ICollection<VehicleDocument> Documents { get; set; }
    public ICollection<MaintenanceRecord> MaintenanceRecords { get; set; }
    public ICollection<VehicleCost> Costs { get; set; }
}
```
**Rôle**: Entité centrale du système. Peut avoir un GPS, des documents, des maintenances.

---

#### **GpsDevice** (Boîtier GPS)
```csharp
public class GpsDevice : TenantEntity
{
    public string DeviceUid { get; set; }      // IMEI (identifiant unique)
    public string? Mat { get; set; }           // Matricule GPS logique
    public string? SimNumber { get; set; }     // Numéro SIM
    public string? SimOperator { get; set; }   // Opérateur (Ooredoo, Orange, etc.)
    public string? Model { get; set; }         // Modèle GPS
    public string? ProtocolType { get; set; }  // gps_type_1, aap, etc.
    public string FuelSensorMode { get; set; } // percent, raw_255, liters
    public string Status { get; set; }         // active, inactive, unassigned
    public DateTime? LastCommunication { get; set; }
    
    // Navigation
    public ICollection<GpsPosition> Positions { get; set; }
    public ICollection<GpsAlert> Alerts { get; set; }
}
```
**Rôle**: Représente le matériel GPS physique. Identifié par IMEI.

---

#### **GpsPosition** (Position GPS)
```csharp
public class GpsPosition : Entity
{
    public long Id { get; set; }               // BIGINT pour volume
    public int DeviceId { get; set; }          // Lien GpsDevice
    public DateTime RecordedAt { get; set; }   // Timestamp GPS
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? SpeedKph { get; set; }      // Vitesse km/h
    public double? CourseDeg { get; set; }     // Direction (0-360°)
    public bool? IgnitionOn { get; set; }      // Contact on/off
    public int? FuelRaw { get; set; }          // Niveau carburant brut
    public string? Address { get; set; }       // Adresse reverse-geocodée
    
    // MEMS Accelerometer (conduite)
    public short? MemsX { get; set; }
    public short? MemsY { get; set; }
    public short? MemsZ { get; set; }
    
    // FMS Data
    public short? TemperatureC { get; set; }   // Température moteur
    public long? OdometerKm { get; set; }      // Kilométrage GPS
    public short? Rpm { get; set; }            // Tours/minute
    public decimal? FuelRateLPer100Km { get; set; } // Consommation
}
```
**Rôle**: Données télématiques brutes. Volume élevé (index sur device_id, recorded_at).

---

#### **Geofence** (Zone Géographique)
```csharp
public class Geofence : TenantEntity
{
    public string Name { get; set; }
    public string Type { get; set; }           // polygon, circle
    public GeofencePoint[]? Coordinates { get; set; } // Points du polygone
    public double? CenterLat { get; set; }     // Centre cercle
    public double? CenterLng { get; set; }
    public double? Radius { get; set; }        // Rayon cercle (mètres)
    public bool AlertOnEntry { get; set; }
    public bool AlertOnExit { get; set; }
    public int NotificationCooldownMinutes { get; set; }
    public TimeSpan? ActiveStartTime { get; set; } // Plage horaire
    public TimeSpan? ActiveEndTime { get; set; }
    
    // Navigation
    public ICollection<GeofenceVehicle> AssignedVehicles { get; set; }
}
```
**Rôle**: Définit des zones pour déclencher des alertes entrée/sortie.

---

#### **Trip** (Trajet)
```csharp
public class Trip : TenantEntity
{
    public long Id { get; set; }               // BIGINT
    public int VehicleId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public string? StartAddress { get; set; }
    public decimal DistanceKm { get; set; }
    public int DurationMinutes { get; set; }
    public decimal? FuelConsumedLiters { get; set; }
    public decimal? MaxSpeedKph { get; set; }
    public int? HarshBrakingCount { get; set; }
    public int? HarshAccelerationCount { get; set; }
    public string Status { get; set; }         // in_progress, completed
    
    // Navigation
    public ICollection<TripWaypoint> Waypoints { get; set; }
}
```
**Rôle**: Agrège les positions en trajets avec statistiques.

---

#### **MaintenanceTemplate** (Modèle d'Entretien)
```csharp
public class MaintenanceTemplate : TenantEntity
{
    public string Name { get; set; }           // Ex: "Vidange huile"
    public string Category { get; set; }       // Moteur, Freinage, etc.
    public string Priority { get; set; }       // low, medium, high, critical
    public int? IntervalKm { get; set; }       // Intervalle kilométrique
    public int? IntervalMonths { get; set; }   // Intervalle temporel
    public decimal? EstimatedCost { get; set; }
    public int WarningKm { get; set; }         // Seuil alerte (km avant)
    public int WarningDays { get; set; }       // Seuil alerte (jours avant)
    
    // Navigation
    public ICollection<VehicleMaintenanceSchedule> Schedules { get; set; }
    public ICollection<MaintenanceTemplatePart> Parts { get; set; }
}
```
**Rôle**: Définit des entretiens récurrents (vidange, pneus, etc.)

---

#### **VehicleMaintenanceSchedule** (Planning Entretien)
```csharp
public class VehicleMaintenanceSchedule : AuditableEntity
{
    public int VehicleId { get; set; }
    public int TemplateId { get; set; }
    public DateTime? LastDoneDate { get; set; }
    public int? LastDoneKm { get; set; }
    public DateTime? NextDueDate { get; set; }
    public int? NextDueKm { get; set; }
    public string Status { get; set; }         // ok, upcoming, due, overdue, critical
    public bool IsPaused { get; set; }
}
```
**Rôle**: Lie un véhicule à un template de maintenance avec l'état actuel.

---

#### **SubscriptionType** (Type d'Abonnement)
```csharp
public class SubscriptionType : Entity
{
    public string Name { get; set; }           // Ex: "Premium GPS"
    public string Code { get; set; }           // Unique code
    public decimal MonthlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }
    
    // Limites
    public int MaxVehicles { get; set; }
    public int MaxUsers { get; set; }
    public int MaxGpsDevices { get; set; }
    public int MaxGeofences { get; set; }
    
    // Features
    public bool GpsTracking { get; set; }
    public bool FuelAnalysis { get; set; }
    public bool AdvancedReports { get; set; }
    
    // Modules activés
    public bool ModuleMonitoring { get; set; }
    public bool ModuleGeofences { get; set; }
    public bool ModuleFleetManagement { get; set; }
}
```
**Rôle**: Définit les plans/offres avec limites et fonctionnalités.

---

#### **VehicleStop** (Arrêt Véhicule)
```csharp
public class VehicleStop : LongIdTenantEntity
{
    public int VehicleId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationSeconds { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    public string StopType { get; set; }       // parking, traffic, delivery, etc.
    public bool IgnitionOff { get; set; }
    public int? GeofenceId { get; set; }       // Si dans une geofence
}
```
**Rôle**: Détecte et enregistre les arrêts avec durée et type.

---

#### **FuelRecord** (Enregistrement Carburant)
```csharp
public class FuelRecord : LongIdTenantEntity
{
    public int VehicleId { get; set; }
    public DateTime RecordedAt { get; set; }
    public short FuelPercent { get; set; }     // 0-100%
    public decimal? FuelLiters { get; set; }
    public decimal? ConsumptionRateLPer100Km { get; set; }
    public string EventType { get; set; }      // reading, refuel, theft_alert
    public bool IsAnomaly { get; set; }
    public string? AnomalyReason { get; set; }
}
```
**Rôle**: Suivi détaillé du carburant, détection anomalies/vol.

---

### 2.2.3 Autres Entités

| Entité | Rôle |
|--------|------|
| `AccidentClaim` | Déclarations de sinistres avec tiers et documents |
| `VehicleCost` | Coûts véhicule (carburant, entretien, assurance) |
| `Notification` | Notifications push/email |
| `PointOfInterest` | POI (stations, clients, etc.) |
| `Supplier` | Fournisseurs (garages, pièces) |
| `Repair` | Réparations avec pièces |
| `Contract` | Contrats de location |
| `DrivingEvent` | Événements conduite (freinage, accélération) |
| `DailyStatistics` | Statistiques quotidiennes agrégées |

---

## 2.3 Controllers API (41 controllers)

| Controller | Endpoints | Description |
|------------|-----------|-------------|
| `AuthController` | `/api/auth/*` | Login, Register, JWT tokens |
| `GpsController` | `/api/gps/*` | Positions temps réel, historique, playback |
| `VehiclesController` | `/api/vehicles/*` | CRUD véhicules |
| `GeofencesController` | `/api/geofences/*` | Zones géographiques |
| `TripsController` | `/api/trips/*` | Trajets |
| `MaintenanceController` | `/api/maintenance/*` | Entretiens |
| `MaintenanceTemplatesController` | `/api/maintenancetemplates/*` | Modèles entretien |
| `ReportsController` | `/api/reports/*` | Rapports (trips, fuel, speed) |
| `DashboardController` | `/api/dashboard/*` | KPIs et statistiques |
| `AdminController` | `/api/admin/*` | Administration système |
| `AlertsController` | `/api/alerts/*` | Alertes GPS |
| `UsersController` | `/api/users/*` | Gestion utilisateurs |
| `FuelRecordsController` | `/api/fuelrecords/*` | Historique carburant |
| `VehicleStopsController` | `/api/vehiclestops/*` | Arrêts véhicules |
| `POIController` | `/api/poi/*` | Points d'intérêt |
| `SuppliersController` | `/api/suppliers/*` | Fournisseurs |
| `AccidentClaimsController` | `/api/accidentclaims/*` | Sinistres |
| `SubscriptionTypesController` | `/api/subscriptiontypes/*` | Plans |

---

## 2.4 Services et Middleware

### SignalR Hub
- **Endpoint**: `/hubs/gps`
- **Events**: `PositionUpdate`, `Alert`, `GeofenceEvent`
- **Groups**: `company_{id}`, `vehicle_{id}`

### Middleware
- `ExceptionHandlingMiddleware` - Gestion erreurs globale
- `PermissionMiddleware` - Vérification admin pour /api/admin
- `TenantMiddleware` - Injection CompanyId depuis JWT

---

# 3. SERVICE RUST GPS INGEST

## 3.1 Architecture

```
gps-ingest-rust/src/
├── main.rs           # Bootstrap, CLI (--calculate-daily)
├── config.rs         # Chargement config YAML
├── db.rs             # Opérations PostgreSQL
├── transport.rs      # Listeners TCP
├── publisher.rs      # Publication RabbitMQ
├── redis_cache.rs    # Cache temps réel Redis
├── telemetry/
│   ├── hh.rs         # Protocole HH (GPS type 1)
│   └── aap.rs        # Protocole AAP/ACI (V1, V2, V3)
└── services/
    ├── stop_detector.rs      # Détection arrêts
    ├── fuel_tracker.rs       # Suivi carburant
    ├── geofence_detector.rs  # Détection entrée/sortie zones
    ├── trip_detector.rs      # Détection trajets
    ├── driving_events.rs     # Événements conduite
    ├── gps_validator.rs      # Validation positions
    ├── gps_stabilizer.rs     # Stabilisation GPS
    ├── geocoding.rs          # Reverse geocoding
    ├── gap_filler.rs         # Interpolation gaps
    └── daily_statistics.rs   # Calcul stats quotidiennes
```

## 3.2 Protocoles GPS Supportés

### Protocole HH (gps_type_1)
- **Port**: 6100
- **Format**: ASCII hexadécimal
- **Headers**: `HH00` (connect), `HH01` (info), `HH13` (position)
- **Données**: IMEI, ICC, coordonnées, vitesse, cap, ignition, fuel

### Protocole AAP/ACI
- **Versions**: V1, V2, V3
- **Données supplémentaires**: MEMS (accéléromètre), FMS (RPM, température, odomètre)
- **Événements**: Freinage brusque, accélération, virages

## 3.3 Services de Traitement

| Service | Rôle |
|---------|------|
| `StopDetector` | Détecte les arrêts (ignition off ou immobile) |
| `FuelTracker` | Suit consommation, détecte anomalies/vols |
| `GeofenceDetector` | Vérifie entrée/sortie zones, envoie alertes |
| `TripDetector` | Agrège positions en trajets |
| `DrivingEventsDetector` | Détecte comportement (freinage, accélération) |
| `GpsValidator` | Valide cohérence positions (vitesse, distance) |
| `GpsStabilizer` | Filtre bruit GPS |
| `GeocodingService` | Reverse geocoding via Nominatim |
| `DailyStatisticsCalculator` | Calcule stats quotidiennes |

---

# 4. FRONTEND ANGULAR

## 4.1 Structure

```
gis-frontend/src/
├── components/       # 40+ composants standalone
├── admin/            # Panel administration système
├── services/         # Services API, Auth, SignalR
├── guards/           # AuthGuard, FeatureGuard
├── models/types.ts   # Interfaces TypeScript
└── app.routes.ts     # Configuration routes
```

## 4.2 Composants Principaux

| Composant | Taille | Rôle |
|-----------|--------|------|
| `monitoring.component.ts` | 92KB | Carte Leaflet, tracking temps réel, playback GPS |
| `reports.component.ts` | 170KB | Rapports complets (trajets, carburant, vitesse) |
| `maintenance-templates.component.ts` | 82KB | Gestion entretiens programmés |
| `vehicles.component.ts` | 70KB | CRUD véhicules avec documents |
| `user-management.component.ts` | 58KB | Gestion utilisateurs et rôles |
| `geofences.component.ts` | 54KB | Dessin zones sur carte |
| `dashboard.component.ts` | 40KB | KPIs et statistiques |
| `fleet-management.component.ts` | 50KB | Vue flotte globale |

## 4.3 Services

| Service | Rôle |
|---------|------|
| `api.service.ts` (99KB) | Toutes les API calls (2952 lignes) |
| `auth.service.ts` | JWT authentication |
| `signalr.service.ts` | WebSocket GPS temps réel |
| `permission.service.ts` | Vérification permissions |
| `geocoding.service.ts` | Reverse geocoding côté client |
| `monitoring-api.service.ts` | API spécifique monitoring |

---

# 5. BASE DE DONNÉES

## 5.1 Tables Principales

```sql
-- Multi-tenant root
societes (id, name, type, subscription_type_id, settings JSONB)

-- Authentification
users (id, email, password_hash, role_id, company_id, status)
roles (id, name, societe_id, is_company_admin, is_system_role, permissions JSONB)

-- Véhicules
vehicles (id, name, plate, gps_device_id, assigned_driver_id, company_id, mileage)
vehicle_documents (id, vehicle_id, type, expiry_date, file_url)

-- GPS
gps_devices (id, device_uid IMEI, mat, sim_number, company_id, status)
gps_positions (id BIGINT, device_id, recorded_at, latitude, longitude, speed_kph, ignition_on)
  -- Index: (device_id, recorded_at)

-- Zones
geofences (id, name, type, coordinates JSONB, company_id)
geofence_vehicles (geofence_id, vehicle_id) -- M:N

-- Trajets
trips (id BIGINT, vehicle_id, start_time, end_time, distance_km, company_id)
trip_waypoints (id BIGINT, trip_id, latitude, longitude, timestamp)

-- Arrêts & Carburant
vehicle_stops (id BIGINT, vehicle_id, start_time, duration_seconds, company_id)
fuel_records (id BIGINT, vehicle_id, recorded_at, fuel_percent, event_type)

-- Maintenance
maintenance_templates (id, name, category, interval_km, interval_months, company_id)
vehicle_maintenance_schedules (id, vehicle_id, template_id, next_due_date, status)
maintenance_logs (id, vehicle_id, template_id, done_date, actual_cost)

-- Abonnements
subscription_types (id, code, name, max_vehicles, max_users, features...)
```

## 5.2 Index Critiques

```sql
-- Performance GPS (volume élevé)
CREATE INDEX ix_gps_positions_device_time ON gps_positions(device_id, recorded_at);
CREATE INDEX ix_gps_positions_time ON gps_positions(recorded_at);

-- Arrêts
CREATE INDEX ix_vehicle_stops_vehicle_time ON vehicle_stops(vehicle_id, start_time);

-- Carburant
CREATE INDEX ix_fuel_records_vehicle_time ON fuel_records(vehicle_id, recorded_at);
```

## 5.3 Multi-Tenancy

Le `GisDbContext` applique des **Query Filters** automatiques:

```csharp
if (_tenantService?.CompanyId != null)
{
    modelBuilder.Entity<Vehicle>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
    modelBuilder.Entity<User>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
    // ... toutes les TenantEntity
}
```

---

# 6. INFRASTRUCTURE DOCKER

## 6.1 Services

| Service | Image | Ports | Rôle |
|---------|-------|-------|------|
| `postgres` | postgis/postgis:16 | 5433 | Base de données spatiale |
| `rabbitmq` | rabbitmq:3.13-management | 5673, 15673 | Messaging |
| `redis` | redis:7-alpine | 6379 | Cache temps réel |
| `gps-ingest` | Custom Rust | 3000, 6100, 6200, 6210 | Ingestion GPS TCP |
| `gis-api` | Custom .NET 8 | 5000 | API REST |
| `frontend` | Custom Angular + Nginx | 4200 | Interface web |
| `osrm` | osrm/osrm-backend | 5001 | Road snapping |
| `nominatim` | mediagis/nominatim:4.4 | 8088 | Reverse geocoding |

## 6.2 Healthchecks

Tous les services ont des healthchecks configurés pour orchestration propre.

---

# 7. ALTERNATIVES À OSRM

## 7.1 Problème actuel avec OSRM

OSRM (Open Source Routing Machine) peut manquer de précision pour plusieurs raisons:
- **Données OSM obsolètes** pour la Tunisie
- **Configuration MLD** moins précise que CH
- **Absence de trafic temps réel**
- **Limites du road-matching**

## 7.2 Solutions Alternatives

### Option 1: **Valhalla** (Recommandé)
```yaml
# docker-compose.yaml
valhalla:
  image: ghcr.io/gis-ops/docker-valhalla/valhalla:latest
  volumes:
    - ./valhalla-data:/custom_files
  environment:
    - tile_urls=https://download.geofabrik.de/africa/tunisia-latest.osm.pbf
  ports:
    - "8002:8002"
```

**Avantages**:
- Meilleur map-matching que OSRM
- API `/trace_route` spécifique pour GPS tracking
- Support du trafic temps réel
- Configuration plus flexible
- Open source (MIT)

**Précision GPS**: ⭐⭐⭐⭐⭐

---

### Option 2: **GraphHopper** (Open Source)
```yaml
graphhopper:
  image: graphhopper/graphhopper:latest
  environment:
    - GRAPHHOPPER_OPTS=-Xmx2g -Xms1g
    - JAVA_OPTS=-server
  volumes:
    - ./graphhopper-data:/data
  command: --config /data/config.yml
  ports:
    - "8989:8989"
```

**Avantages**:
- API Map Matching dédiée
- Très bonne documentation
- Support isochrones
- Scalable

**Précision GPS**: ⭐⭐⭐⭐

---

### Option 3: **HERE Maps API** (Commercial)
```typescript
// Intégration TypeScript
const hereClient = new HereRoutingClient(apiKey);
const snappedRoute = await hereClient.matchRoute(gpsPoints);
```

**Avantages**:
- Excellente précision mondiale
- Trafic temps réel
- Support professionnel
- Données très à jour

**Inconvénients**:
- Coût par requête (~$0.50/1000 req)

**Précision GPS**: ⭐⭐⭐⭐⭐

---

### Option 4: **Mapbox Matching API** (Commercial)
```bash
curl "https://api.mapbox.com/matching/v5/mapbox/driving/{coordinates}?access_token={token}"
```

**Avantages**:
- API Map Matching optimisée GPS
- Très bonne couverture Afrique
- SDK Angular disponible

**Coût**: ~$0.50/1000 requêtes

**Précision GPS**: ⭐⭐⭐⭐⭐

---

### Option 5: **Améliorer OSRM** (Sans changement d'outil)

1. **Mettre à jour les données OSM**:
```bash
# Télécharger données récentes
wget https://download.geofabrik.de/africa/tunisia-latest.osm.pbf

# Reconstruire avec algorithme CH (plus précis que MLD)
docker run -t -v ./osrm-data:/data osrm/osrm-backend osrm-extract -p /opt/car.lua /data/tunisia-latest.osm.pbf
docker run -t -v ./osrm-data:/data osrm/osrm-backend osrm-contract /data/tunisia-latest.osrm
```

2. **Utiliser l'API Match** au lieu de Route:
```typescript
// Plus précis pour le GPS tracking
const matchUrl = `http://osrm:5000/match/v1/driving/${coordinates}?geometries=geojson&overview=full`;
```

3. **Ajuster les paramètres**:
```bash
# docker-compose.yaml
command: osrm-routed --algorithm ch --max-matching-size 1000 /data/tunisia-latest.osrm
```

---

## 7.3 Recommandation

Pour le **GPS Fleet Tracking en Tunisie**, je recommande:

### 🥇 **Valhalla** (Best choice)
- Gratuit et open source
- API `/trace_route` conçue pour le GPS tracking
- Meilleur map-matching que OSRM
- Facile à dockeriser

### Configuration suggérée:
```yaml
# docker-compose.yaml - Remplacer OSRM par Valhalla
valhalla:
  image: ghcr.io/gis-ops/docker-valhalla/valhalla:latest
  container_name: gisv2-valhalla
  environment:
    - tile_urls=https://download.geofabrik.de/africa/tunisia-latest.osm.pbf
    - serve_tiles=True
    - build_tar=False
  volumes:
    - valhalla-data:/custom_files
  ports:
    - "8002:8002"
  restart: unless-stopped

volumes:
  valhalla-data:
```

### Modification côté Rust:
```rust
// Dans gps-ingest-rust/src/services/road_snapper.rs
pub async fn snap_to_road(points: &[GpsPoint]) -> Result<Vec<SnappedPoint>> {
    let valhalla_url = std::env::var("VALHALLA_URL")
        .unwrap_or_else(|_| "http://valhalla:8002".to_string());
    
    let request = TraceRouteRequest {
        shape: points.iter().map(|p| ShapePoint {
            lat: p.latitude,
            lon: p.longitude,
            time: p.timestamp.timestamp(),
        }).collect(),
        costing: "auto",
        shape_match: "map_snap",
    };
    
    let response = reqwest::Client::new()
        .post(&format!("{}/trace_route", valhalla_url))
        .json(&request)
        .send()
        .await?;
    
    // Parse et retourner les points snappés
}
```

---

# 8. RÉSUMÉ EXÉCUTIF

## Points forts du projet
- ✅ Architecture Clean CQRS bien structurée
- ✅ Multi-tenancy robuste avec query filters
- ✅ Ingestion GPS haute performance en Rust
- ✅ Temps réel via Redis + SignalR
- ✅ Système de permissions granulaire
- ✅ Maintenance programmée complète

## Points d'amélioration suggérés
- 🔄 Remplacer OSRM par Valhalla pour meilleur road-matching
- 🔄 Ajouter tests unitaires (couverture actuelle faible)
- 🔄 Optimiser requêtes N+1 dans certains controllers
- 🔄 Ajouter monitoring (Prometheus/Grafana)

## Métriques du projet
- **36 entités Domain**
- **41 controllers API**
- **11 services Rust**
- **40+ composants Angular**
- **22 migrations SQL**
- **8 services Docker**

---

*Rapport généré automatiquement par l'analyse du codebase GISV2*
