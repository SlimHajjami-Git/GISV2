# Analyse Architecturale GIS_V2 & Plan APIs Backend

## 📋 Vue d'Ensemble des Projets

Le workspace **GIS_V2** contient **4 projets principaux** formant une plateforme de gestion de flotte véhicules avec suivi GPS:

| Projet | Technologie | Rôle |
|--------|-------------|------|
| `gis-frontend` | Angular 17+ | Interface utilisateur |
| `GisAPI` | .NET 8 | API REST principale |
| `gps-ingest-rust` | Rust/Tokio | Ingestion GPS temps réel |
| `shared-kernel` | Protobuf | Contrats partagés |

---

## 🏗️ Architecture Actuelle du Frontend

### Structure des Pages (Routes)

#### **Application Client** (`/`)
| Route | Composant | Description |
|-------|-----------|-------------|
| `/` | `LandingComponent` | Page d'accueil publique |
| `/login` | `LoginComponent` | Authentification |
| `/register` | `RegisterComponent` | Inscription |
| `/dashboard` | `DashboardComponent` | Tableau de bord principal |
| `/vehicles` | `VehiclesComponent` | Gestion des véhicules |
| `/employees` | `EmployeesComponent` | Gestion des employés |
| `/gps-devices` | `GPSDevicesComponent` | Gestion des appareils GPS |
| `/monitoring` | `MonitoringComponent` | Suivi temps réel |
| `/geofences` | `GeofencesComponent` | Zones géographiques |
| `/maintenance` | `MaintenanceComponent` | Entretien véhicules |
| `/costs` | `VehicleCostsComponent` | Suivi des coûts |
| `/reports` | `ReportsComponent` | Rapports et analytics |
| `/settings` | `SettingsComponent` | Paramètres utilisateur |
| `/profile` | `ProfileComponent` | Profil utilisateur |
| `/users` | `UserManagementComponent` | Gestion des utilisateurs |
| `/notifications` | `NotificationsComponent` | Centre de notifications |
| `/subscription` | `SubscriptionComponent` | Gestion abonnement |

#### **Panel Admin** (`/admin/`)
| Route | Composant | Description |
|-------|-----------|-------------|
| `/admin/login` | `AdminLoginComponent` | Connexion admin |
| `/admin/dashboard` | `AdminDashboardComponent` | Dashboard admin |
| `/admin/clients` | `AdminClientsComponent` | Gestion des clients/entreprises |
| `/admin/users` | `AdminUsersComponent` | Utilisateurs système |
| `/admin/health` | `AdminHealthComponent` | État des services |
| `/admin/features` | `AdminFeatureControlComponent` | Contrôle des fonctionnalités |
| `/admin/estimates` | `AdminEstimatesComponent` | Devis/Facturation |
| `/admin/activity` | `AdminActivityComponent` | Logs d'activité |
| `/admin/settings` | `AdminSettingsComponent` | Paramètres système |

---

## 📊 Analyse des Entités (Modèles de Données)

### Entités Principales

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   Subscription  │────<│     Company     │>────│      User       │
└─────────────────┘     └─────────────────┘     └─────────────────┘
                               │                        │
                               │                        │
           ┌───────────────────┼───────────────────┐    │
           │                   │                   │    │
           ▼                   ▼                   ▼    │
   ┌─────────────┐     ┌─────────────┐     ┌───────────┴───┐
   │   Vehicle   │     │  Employee   │     │  UserSettings │
   └─────────────┘     └─────────────┘     └───────────────┘
           │
           ├──────────────────┬────────────────────┐
           │                  │                    │
           ▼                  ▼                    ▼
   ┌─────────────┐    ┌──────────────┐    ┌─────────────┐
   │  GpsDevice  │    │ Maintenance  │    │ VehicleCost │
   └─────────────┘    └──────────────┘    └─────────────┘
           │
           ├─────────────────┐
           │                 │
           ▼                 ▼
   ┌─────────────┐    ┌─────────────┐
   │ GpsPosition │    │  GpsAlert   │
   └─────────────┘    └─────────────┘

┌─────────────────┐
│    Geofence     │──────>│ GeofenceVehicle │
└─────────────────┘       └─────────────────┘
           │
           ▼
   ┌─────────────────┐
   │ GeofenceEvent   │
   └─────────────────┘
```

### Détail des Entités

| Entité | Champs Clés | Relations |
|--------|-------------|-----------|
| **Company** | id, name, type, settings, subscriptionId | → Users, Vehicles, Employees, Geofences |
| **User** | id, name, email, roles[], permissions[], companyId | → Company, UserSettings |
| **Vehicle** | id, name, plate, status, hasGps, companyId | → GpsDevice, Employee(driver), Maintenance, Costs |
| **Employee** | id, name, role, status, companyId | → Vehicles (driver/supervisor) |
| **GpsDevice** | id, deviceUid(IMEI), simNumber, status, companyId | → Vehicle, Positions, Alerts |
| **GpsPosition** | id, deviceId, lat, lng, speed, timestamp | → GpsDevice |
| **GpsAlert** | id, type, severity, message, resolved | → GpsDevice, Vehicle |
| **Geofence** | id, name, type, coordinates, alertOnEntry/Exit | → Vehicles (M:N), Events |
| **MaintenanceRecord** | id, vehicleId, type, status, costs | → Vehicle, Parts |
| **VehicleCost** | id, vehicleId, type, amount, date | → Vehicle |
| **Subscription** | id, name, price, features[], maxVehicles | → Companies |

---

## 🔌 Analyse des Appels API Frontend

### APIs Consommées par le Frontend (api.service.ts)

#### **Authentification** (`/api/auth`)
| Endpoint | Méthode | DTO Request | DTO Response | Implémenté |
|----------|---------|-------------|--------------|------------|
| `/auth/login` | POST | `{email, password}` | `{token, refreshToken, user}` | ✅ |
| `/auth/register` | POST | `{name, email, password, companyName, phone?}` | `{token, refreshToken, user}` | ✅ |

#### **Utilisateurs** (`/api/users`)
| Endpoint | Méthode | Description | Implémenté |
|----------|---------|-------------|------------|
| `/users` | GET | Liste tous les utilisateurs | ✅ |
| `/users/{id}` | GET | Détail utilisateur | ✅ |
| `/users` | POST | Créer utilisateur | ✅ |
| `/users/{id}` | PUT | Modifier utilisateur | ✅ |
| `/users/{id}` | DELETE | Supprimer utilisateur | ✅ |

#### **Véhicules** (`/api/vehicles`)
| Endpoint | Méthode | Description | Implémenté |
|----------|---------|-------------|------------|
| `/vehicles` | GET | Liste des véhicules | ✅ |
| `/vehicles/{id}` | GET | Détail véhicule | ✅ |
| `/vehicles` | POST | Créer véhicule | ✅ |
| `/vehicles/{id}` | PUT | Modifier véhicule | ✅ |
| `/vehicles/{id}` | DELETE | Supprimer véhicule | ✅ |
| `/vehicles/locations` | GET | Positions actuelles | ⚠️ Partiel |

#### **Employés** (`/api/employees`)
| Endpoint | Méthode | Description | Implémenté |
|----------|---------|-------------|------------|
| `/employees` | GET | Liste des employés | ✅ |
| `/employees/drivers` | GET | Chauffeurs uniquement | ⚠️ À créer |
| `/employees/supervisors` | GET | Superviseurs uniquement | ⚠️ À créer |
| `/employees` | POST | Créer employé | ✅ |
| `/employees/{id}` | PUT | Modifier employé | ✅ |
| `/employees/{id}` | DELETE | Supprimer employé | ✅ |

#### **Géofences** (`/api/geofences`)
| Endpoint | Méthode | Description | Implémenté |
|----------|---------|-------------|------------|
| `/geofences` | GET | Liste des géofences | ✅ |
| `/geofences/{id}` | GET | Détail géofence | ✅ |
| `/geofences` | POST | Créer géofence | ✅ |
| `/geofences/{id}` | PUT | Modifier géofence | ✅ |
| `/geofences/{id}` | DELETE | Supprimer géofence | ✅ |
| `/geofences/{id}/vehicles` | POST | Assigner véhicules | ⚠️ À créer |

#### **Maintenance** (`/api/maintenance`)
| Endpoint | Méthode | Description | Implémenté |
|----------|---------|-------------|------------|
| `/maintenance` | GET | Liste maintenances (filter: vehicleId) | ✅ |
| `/maintenance/upcoming` | GET | Maintenances à venir | ⚠️ À créer |
| `/maintenance` | POST | Créer maintenance | ✅ |
| `/maintenance/{id}` | PUT | Modifier maintenance | ✅ |
| `/maintenance/{id}` | DELETE | Supprimer maintenance | ✅ |

#### **Coûts** (`/api/costs`)
| Endpoint | Méthode | Description | Implémenté |
|----------|---------|-------------|------------|
| `/costs` | GET | Liste des coûts (filters) | ✅ |
| `/costs/summary` | GET | Résumé des coûts | ⚠️ À créer |
| `/costs` | POST | Créer coût | ✅ |
| `/costs/{id}` | DELETE | Supprimer coût | ✅ |

#### **Appareils GPS** (`/api/gpsdevices`)
| Endpoint | Méthode | Description | Implémenté |
|----------|---------|-------------|------------|
| `/gpsdevices` | GET | Liste des appareils | ✅ |
| `/gpsdevices/unassigned` | GET | Appareils non assignés | ⚠️ À créer |
| `/gpsdevices` | POST | Créer appareil | ✅ |
| `/gpsdevices/{id}/assign/{vehicleId}` | POST | Assigner à véhicule | ⚠️ À créer |
| `/gpsdevices/{id}/unassign` | POST | Désassigner | ⚠️ À créer |
| `/gpsdevices/{id}/positions` | GET | Historique positions | ⚠️ À créer |

#### **Alertes** (`/api/alerts`)
| Endpoint | Méthode | Description | Implémenté |
|----------|---------|-------------|------------|
| `/alerts` | GET | Liste des alertes (filters) | ✅ |
| `/alerts/unread-count` | GET | Nombre non lues | ⚠️ À créer |
| `/alerts/{id}/resolve` | POST | Résoudre alerte | ⚠️ À créer |
| `/alerts/resolve-all` | POST | Résoudre toutes | ⚠️ À créer |

---

## 🔧 APIs Admin à Créer (admin.service.ts)

Le panel admin utilise actuellement des **données mock**. Voici les APIs à implémenter:

#### **Admin Auth** (`/api/admin/auth`)
| Endpoint | Méthode | Description |
|----------|---------|-------------|
| `/admin/auth/login` | POST | Connexion admin |
| `/admin/auth/logout` | POST | Déconnexion |

#### **Admin Dashboard** (`/api/admin/dashboard`)
| Endpoint | Méthode | Description |
|----------|---------|-------------|
| `/admin/dashboard/stats` | GET | Statistiques globales |
| `/admin/dashboard/feature-usage` | GET | Usage des fonctionnalités |

#### **Gestion Clients** (`/api/admin/clients`)
| Endpoint | Méthode | Description |
|----------|---------|-------------|
| `/admin/clients` | GET | Liste des entreprises clientes |
| `/admin/clients/{id}` | GET | Détail client |
| `/admin/clients` | POST | Créer client |
| `/admin/clients/{id}` | PUT | Modifier client |
| `/admin/clients/{id}/suspend` | POST | Suspendre client |
| `/admin/clients/{id}/activate` | POST | Activer client |
| `/admin/clients/{id}/settings` | PUT | Modifier paramètres client |

#### **Utilisateurs Système** (`/api/admin/users`)
| Endpoint | Méthode | Description |
|----------|---------|-------------|
| `/admin/users` | GET | Tous les utilisateurs système |
| `/admin/users?companyId={id}` | GET | Utilisateurs par entreprise |
| `/admin/users/{id}/permissions` | PUT | Modifier permissions |
| `/admin/users/{id}/suspend` | POST | Suspendre utilisateur |
| `/admin/users/{id}/activate` | POST | Activer utilisateur |

#### **Santé des Services** (`/api/admin/health`)
| Endpoint | Méthode | Description |
|----------|---------|-------------|
| `/admin/health/services` | GET | État de tous les services |
| `/admin/health/metrics` | GET | Métriques système |

#### **Logs d'Activité** (`/api/admin/activity`)
| Endpoint | Méthode | Description |
|----------|---------|-------------|
| `/admin/activity/logs` | GET | Logs d'activité (pagination) |
| `/admin/activity/logs?userId={id}` | GET | Logs par utilisateur |

#### **Mode Maintenance** (`/api/admin/maintenance-mode`)
| Endpoint | Méthode | Description |
|----------|---------|-------------|
| `/admin/maintenance-mode` | GET | État du mode maintenance |
| `/admin/maintenance-mode` | PUT | Activer/désactiver |

#### **Devis/Facturation** (`/api/admin/estimates`)
| Endpoint | Méthode | Description |
|----------|---------|-------------|
| `/admin/estimates` | GET | Liste des devis |
| `/admin/estimates` | POST | Créer devis |
| `/admin/estimates/{id}` | PUT | Modifier devis |
| `/admin/estimates/{id}/send` | POST | Envoyer devis |

#### **Abonnements** (`/api/admin/subscriptions`)
| Endpoint | Méthode | Description |
|----------|---------|-------------|
| `/admin/subscriptions` | GET | Liste des plans |
| `/admin/subscriptions` | POST | Créer plan |
| `/admin/subscriptions/{id}` | PUT | Modifier plan |

---

## 🚀 Plan d'Implémentation Backend

### Phase 1: Compléter les APIs Client Existantes (Priorité Haute)

#### 1.1 Endpoints Manquants dans les Contrôleurs Existants

| Contrôleur | Endpoints à Ajouter |
|------------|---------------------|
| `EmployeesController` | `GET /employees/drivers`, `GET /employees/supervisors` |
| `GeofencesController` | `POST /geofences/{id}/vehicles` |
| `MaintenanceController` | `GET /maintenance/upcoming` |
| `CostsController` | `GET /costs/summary` |
| `GpsDevicesController` | `GET /unassigned`, `POST /{id}/assign/{vehicleId}`, `POST /{id}/unassign`, `GET /{id}/positions` |
| `AlertsController` | `GET /unread-count`, `POST /{id}/resolve`, `POST /resolve-all` |

#### 1.2 Nouveaux Endpoints Temps Réel

| Endpoint | Description |
|----------|-------------|
| `GET /vehicles/locations` | Positions actuelles de tous les véhicules |
| `GET /vehicles/{id}/track` | Position temps réel d'un véhicule |
| `GET /vehicles/{id}/trips` | Historique des trajets |
| `GET /vehicles/{id}/trips/{tripId}/replay` | Points pour replay |

### Phase 2: APIs Admin (Priorité Moyenne)

#### 2.1 Nouveau Contrôleur: `AdminController`

```
services/GisAPI/Controllers/Admin/
├── AdminAuthController.cs
├── AdminDashboardController.cs
├── AdminClientsController.cs
├── AdminUsersController.cs
├── AdminHealthController.cs
├── AdminActivityController.cs
├── AdminEstimatesController.cs
└── AdminSettingsController.cs
```

#### 2.2 Nouveaux Modèles

| Modèle | Description |
|--------|-------------|
| `AdminUser` | Utilisateur admin (super_admin, admin, support) |
| `ActivityLog` | Journal d'activité |
| `Estimate` | Devis client |
| `EstimateItem` | Ligne de devis |
| `SystemSettings` | Paramètres système |

### Phase 3: APIs Avancées (Priorité Basse)

| Fonctionnalité | Endpoints |
|----------------|-----------|
| **Rapports** | `/reports/generate`, `/reports/schedule`, `/reports/templates` |
| **Driver Scores** | `/drivers/{id}/score`, `/drivers/rankings` |
| **Notifications Push** | `/notifications/send`, `/notifications/subscribe` |
| **Export Data** | `/export/vehicles`, `/export/trips`, `/export/costs` |

---

## 📁 Structure Recommandée pour les Nouveaux Fichiers

```
services/GisAPI/
├── Controllers/
│   ├── Admin/                          # Nouveau dossier
│   │   ├── AdminAuthController.cs
│   │   ├── AdminDashboardController.cs
│   │   ├── AdminClientsController.cs
│   │   ├── AdminUsersController.cs
│   │   ├── AdminHealthController.cs
│   │   ├── AdminActivityController.cs
│   │   ├── AdminEstimatesController.cs
│   │   └── AdminSettingsController.cs
│   └── ... (existants)
├── DTOs/
│   ├── Admin/                          # Nouveau dossier
│   │   ├── AdminAuthDTOs.cs
│   │   ├── DashboardDTOs.cs
│   │   ├── ClientDTOs.cs
│   │   ├── EstimateDTOs.cs
│   │   └── ActivityLogDTOs.cs
│   ├── CostDTOs.cs                     # Nouveau
│   ├── MaintenanceDTOs.cs              # Nouveau
│   ├── GeofenceDTOs.cs                 # Nouveau
│   ├── GpsDeviceDTOs.cs                # Nouveau
│   ├── AlertDTOs.cs                    # Nouveau
│   └── ... (existants)
├── Models/
│   ├── Admin/                          # Nouveau dossier
│   │   ├── AdminUser.cs
│   │   ├── ActivityLog.cs
│   │   ├── Estimate.cs
│   │   └── SystemSettings.cs
│   └── ... (existants)
├── Services/                           # Nouveau dossier
│   ├── Interfaces/
│   │   ├── IAuthService.cs
│   │   ├── IVehicleService.cs
│   │   ├── IGpsTrackingService.cs
│   │   ├── IReportService.cs
│   │   └── INotificationService.cs
│   └── Implementations/
│       ├── AuthService.cs
│       ├── VehicleService.cs
│       ├── GpsTrackingService.cs
│       ├── ReportService.cs
│       └── NotificationService.cs
└── Middleware/
    ├── AdminAuthMiddleware.cs          # Nouveau
    └── ActivityLogMiddleware.cs        # Nouveau
```

---

## 🔐 Sécurité & Autorisations

### Niveaux d'Accès

| Rôle | Scope | Accès |
|------|-------|-------|
| `super_admin` | Système | Tout accès |
| `admin` | Entreprise | Gestion complète de l'entreprise |
| `manager` | Entreprise | Gestion véhicules, employés, rapports |
| `employee` | Limité | Lecture seule, véhicules assignés |
| `driver` | Limité | Position actuelle, ses trajets |

### Headers d'Autorisation

```
Authorization: Bearer <JWT_TOKEN>
X-Company-Id: <company_id>  (pour multi-tenant)
```

---

## 📝 Prochaines Étapes

1. **Immédiat**: Compléter les endpoints manquants dans `EmployeesController`, `GpsDevicesController`, `AlertsController`
2. **Court terme**: Créer les DTOs pour toutes les entités
3. **Moyen terme**: Implémenter les contrôleurs Admin
4. **Long terme**: APIs temps réel (SignalR), Rapports, Notifications

---

*Document généré le 24/12/2024*
