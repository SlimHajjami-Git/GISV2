# Analyse Architecture GISV2 — CQRS, Microservices & Notifications

## 1. Architecture Actuelle — Ce qui fonctionne bien

### ✅ CQRS avec MediatR
- Séparation Commands/Queries via `ICommand<T>` / `IQuery<T>`
- Pipeline Behaviors: `ValidationBehaviour`, `LoggingBehaviour`, `AuthorizationBehaviour`
- Clean Architecture: Domain → Application → Infrastructure → API

### ✅ Multi-tenancy solide
- `ICurrentTenantService` injecté partout
- Query filters automatiques sur `TenantEntity` dans `GisDbContext`
- JWT claims portent companyId, userId, permissions

### ✅ RabbitMQ déjà câblé
- 3 exchanges déclarés: `gis.gps` (Topic), `gis.alerts` (Fanout), `gis.events` (Topic)
- Flux GPS: Rust → RabbitMQ → GpsTelemetryConsumer → MediatR → SignalR

### ✅ SignalR temps réel
- GpsHub avec groupes: `company_{id}`, `vehicle_{id}`, `geofence_{id}`
- Interface `IGpsHubService` dans Application layer (Clean Architecture respectée)

### ✅ Entités Notification déjà définies
- `Notification.cs` existe avec: Type, Title, Message, Priority, Channel, IsRead, Metadata (JSONB)
- `SpeedLimitAlert.cs` existe
- `DomainEvent.cs` définit déjà les events: GeofenceEntry/Exit, SpeedingAlert, MaintenanceDue, etc.
- Table `notifications` configurée dans DbContext avec query filter multi-tenant

---

## 2. Problèmes Identifiés 🔴

### 🔴 P1: Domain Events DÉFINIS mais JAMAIS DISPATCHÉS
```
DomainEvent.cs contient:
- GpsPositionReceivedEvent
- VehicleEnteredGeofenceEvent / VehicleExitedGeofenceEvent
- SpeedingAlertEvent
- AlertCreatedEvent
- MaintenanceDueEvent / MaintenanceCompletedEvent
- VehicleCreatedEvent / UpdatedEvent / DeletedEvent

MAIS: Aucun mécanisme ne les publie. Pas de INotificationHandler<>,
pas de dispatcher post-SaveChanges, pas de MediatR Publish().
```
**Impact**: Les événements métier n'existent que sur papier. Aucune réaction automatique.

### 🔴 P2: Controllers "Fat" — Logique métier hors du CQRS
```
❌ AdminController.cs    → CRUD sociétés, users, roles = logique directe dans controller
❌ AuthController.cs      → Login/Register = accès direct DbContext
❌ UsersController.cs     → CRUD users = accès direct DbContext
❌ AdminUserController.cs → CRUD users admin = accès direct DbContext
❌ SocietesController.cs  → CRUD sociétés = accès direct DbContext

✅ VehiclesController.cs  → Passe par MediatR (GetVehiclesQuery, etc.)
✅ RolesController.cs     → Passe par MediatR (CreateRoleCommand, etc.)
✅ GpsController.cs       → Passe par MediatR
```
**Impact**: Incohérence. La moitié du code respecte CQRS, l'autre non. Pas de validation pipeline, pas de logging uniforme, pas d'authorization automatique.

### 🔴 P3: Aucun Notification Controller/Service
```
- Notification entity ✅ (existe)
- DbSet<Notification> dans GisDbContext ✅ (existe)
- DbSet<Notification> dans IGisDbContext ❌ (MANQUANT)
- NotificationController ❌ (MANQUANT)
- CQRS Commands/Queries ❌ (MANQUANT)
- Notification hub/service ❌ (MANQUANT)
```
**Impact**: Les notifications ne sont ni créées, ni stockées, ni envoyées.

### 🟡 P4: Deux RabbitMQ Consumers redondants
```
1. GpsTelemetryConsumer (GisAPI/Services/) → queue "gps.telemetry.dotnet", exchange "telemetry.raw"
2. RabbitMqConsumerService (Infrastructure/) → queue "gis.gps.positions", exchange "gis.gps"
```
Ils écoutent des exchanges/queues différents mais font la même chose (BroadcastPositionCommand).
**Impact**: Confusion, potentiellement double-traitement des messages GPS.

### 🟡 P5: Alertes éphémères (fire-and-forget)
```
BroadcastPositionCommandHandler:
  if (alertType != "normal" && alertType != "periodic")
    → SendAlertAsync() via SignalR
    → MAIS: pas de persistance, pas de notification créée
```
**Impact**: Si l'utilisateur est offline, il rate l'alerte. Aucune historique.

### 🟡 P6: `IMessageBus` dans Domain layer
```
IMessageBus est dans GisAPI.Domain.Interfaces
→ Violation: le Domain ne devrait pas connaître les détails messaging
→ Devrait être dans Application.Common.Interfaces
```
**Impact**: Couplage Domain → Infrastructure concept. Pas critique mais impure.

---

## 3. Proposition: Système de Notifications

### Architecture Recommandée

```
┌─────────────────────────────────────────────────────────────────┐
│                        Sources d'Événements                      │
│                                                                   │
│  GPS Ingest (Rust)     Geofence Detection    Maintenance Due     │
│  Speed Alerts          User Actions          System Events        │
│         │                    │                     │              │
│         ▼                    ▼                     ▼              │
│  ┌─────────────────────────────────────────────────┐             │
│  │           RabbitMQ (gis.events exchange)         │             │
│  │    routing keys: alert.*, geofence.*, maint.*   │             │
│  └─────────────────────┬───────────────────────────┘             │
│                        │                                          │
│                        ▼                                          │
│  ┌─────────────────────────────────────────────────┐             │
│  │     NotificationConsumer (BackgroundService)     │             │
│  │  - Consomme events de gis.events                │             │
│  │  - Crée Notification en DB                      │             │
│  │  - Envoie via SignalR (temps réel)              │             │
│  │  - Envoie push/email si configuré               │             │
│  └─────────────────────────────────────────────────┘             │
│                        │                                          │
│              ┌─────────┼─────────┐                               │
│              ▼         ▼         ▼                                │
│         SignalR     DB Store   Email/Push                         │
│      (real-time)  (historique) (async)                            │
│                                                                   │
│  ┌─────────────────────────────────────────────────┐             │
│  │        NotificationController (REST API)         │             │
│  │  GET  /api/notifications          (liste)        │             │
│  │  GET  /api/notifications/unread   (count)        │             │
│  │  PUT  /api/notifications/:id/read (marquer lu)   │             │
│  │  PUT  /api/notifications/read-all (tout lire)    │             │
│  │  DELETE /api/notifications/:id    (supprimer)    │             │
│  └─────────────────────────────────────────────────┘             │
│                                                                   │
│  ┌─────────────────────────────────────────────────┐             │
│  │           NotificationHub (SignalR)               │             │
│  │  Events:                                          │             │
│  │  - NewNotification (push to user_{id})            │             │
│  │  - UnreadCountChanged                             │             │
│  │  Groups: user_{id}, company_{id}                  │             │
│  └─────────────────────────────────────────────────┘             │
└─────────────────────────────────────────────────────────────────┘
```

### Types de Notifications à Implémenter

| Type | Source | Priorité | Exemple |
|------|--------|----------|---------|
| `speed_alert` | GPS frame send_flag=5 | high | "Véhicule X dépasse 120km/h" |
| `geofence_entry` | Geofence check | normal | "Véhicule X entré dans Zone Y" |
| `geofence_exit` | Geofence check | normal | "Véhicule X sorti de Zone Y" |
| `maintenance_due` | Scheduler | high | "Vidange due pour Véhicule X" |
| `maintenance_overdue` | Scheduler | critical | "Maintenance en retard: Véhicule X" |
| `document_expiry` | Scheduler | high | "Assurance expire dans 7 jours" |
| `ignition_change` | GPS frame send_flag=4 | low | "Véhicule X: contact coupé" |
| `harsh_driving` | GPS MEMS data | normal | "Freinage brusque détecté" |
| `fuel_anomaly` | Fuel analysis | high | "Perte carburant anormale" |
| `user_login` | Auth | low | "Nouvelle connexion depuis..." |

---

## 4. Plan d'Implémentation Recommandé

### Phase 1: Fondation (corriger l'architecture)
**Objectif**: Câbler les Domain Events + créer le CRUD notifications

1. **Ajouter `DbSet<Notification>` à `IGisDbContext`**
2. **Créer les CQRS pour Notifications**:
   - `CreateNotificationCommand` / Handler
   - `GetNotificationsQuery` / Handler (avec pagination)
   - `GetUnreadCountQuery` / Handler
   - `MarkNotificationReadCommand` / Handler
   - `MarkAllReadCommand` / Handler
3. **Créer `NotificationsController`** (REST API)
4. **Ajouter Domain Event Dispatcher**:
   - Intercepter `SaveChangesAsync` dans GisDbContext
   - Publier les DomainEvents via MediatR `IPublisher`

### Phase 2: Temps réel
**Objectif**: Notifications push instantanées via SignalR

5. **Étendre le GpsHub** (ou créer NotificationHub séparé):
   - Ajouter group `user_{id}` pour notifications ciblées
   - Event `NewNotification` vers le bon utilisateur
   - Event `UnreadCountChanged` pour badge UI
6. **Créer `INotificationService`**:
   - Méthode `NotifyAsync(userId, notification)`
   - Persiste en DB + push SignalR simultanément
7. **Frontend: Composant notification bell** dans le header

### Phase 3: Event-driven (réactif)
**Objectif**: Les événements GPS/métier déclenchent automatiquement des notifications

8. **Créer les `INotificationHandler<T>` MediatR**:
   - `SpeedingAlertHandler` → crée notification speed_alert
   - `GeofenceEventHandler` → crée notification geofence_entry/exit
   - `MaintenanceDueHandler` → crée notification maintenance_due
9. **Modifier `BroadcastPositionCommandHandler`**:
   - Après broadcast SignalR, publier `SpeedingAlertEvent` si speed > limit
   - Publier `GeofenceEntryEvent` si position dans zone
10. **Consumer RabbitMQ pour alertes async** (pour ne pas bloquer le flux GPS)

### Phase 4: Préférences & Canaux
**Objectif**: Configuration par utilisateur

11. **Entité `NotificationPreference`** (par user, par type)
12. **Email notifications** (SMTP intégration)
13. **Push browser** (Web Push API / Firebase)

---

## 5. Ce que je recommande de NE PAS faire

### ❌ Ne PAS créer un microservice notification séparé
**Raison**: Votre architecture est un **monolithe modulaire** (Clean Architecture), pas des vrais microservices indépendants. Le seul vrai service séparé est le Rust GPS ingest (justifié par la performance TCP). Ajouter un service Node.js ou Python pour les notifications serait de l'over-engineering:
- Complexité réseau/déploiement inutile
- Latence inter-service
- Vous n'avez pas besoin de scalabilité indépendante pour les notifications

### ❌ Ne PAS utiliser un event bus externe pour les events internes
**Raison**: MediatR `INotificationHandler<>` suffit pour les événements intra-process. RabbitMQ est utile pour la communication inter-process (Rust → .NET) mais pas nécessaire pour "un handler crée une notification dans la même DB".

### ❌ Ne PAS implémenter l'Outbox Pattern maintenant
**Raison**: Pattern utile pour la cohérence transactionnelle dans les systèmes distribués. Vous avez une seule DB PostgreSQL, pas besoin.

---

## 6. Ce que je recommande de corriger EN PRIORITÉ

### 1️⃣ Migrer les Fat Controllers vers CQRS (progressivement)
Commencer par `UsersController` → créer `CreateUserCommand`, `UpdateUserCommand`, etc.
**Bénéfice**: Validation automatique, logging, authorization uniforme.

### 2️⃣ Consolider les 2 RabbitMQ consumers en 1 seul
Garder `GpsTelemetryConsumer` (le principal). Supprimer ou réaffecter `RabbitMqConsumerService` pour les events/alerts.

### 3️⃣ Câbler les Domain Events (Phase 1 du plan ci-dessus)
C'est la fondation. Sans ça, aucune notification automatique n'est possible.

### 4️⃣ Persister les alertes au lieu de les fire-and-forget
Modifier `BroadcastPositionCommandHandler` pour écrire les alertes en DB.

---

## 7. Estimation Effort

| Phase | Effort | Priorité |
|-------|--------|----------|
| Phase 1: CRUD Notifications | 1-2 jours | 🔴 Critique |
| Phase 2: SignalR + Frontend bell | 1 jour | 🔴 Critique |
| Phase 3: Event handlers automatiques | 2-3 jours | 🟡 Important |
| Phase 4: Préférences & Email | 2 jours | 🟢 Nice-to-have |
| Migration Fat Controllers → CQRS | 2-3 jours | 🟡 Important |
| Consolidation RabbitMQ consumers | 0.5 jour | 🟢 Nice-to-have |
