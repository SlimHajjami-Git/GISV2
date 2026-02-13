# Rapport Détaillé — Notifications Instantanées GISV2

## Résumé

Implémentation complète des notifications temps réel via **SignalR** + **toasts frontend** pour 3 catégories :
1. **Géofencing** — Entrée/Sortie de véhicules dans les zones géofencées
2. **Comportement de conduite** — Freinage brusque, accélération rapide, virage brusque, excès de vitesse
3. **Actions administratives** — Notification aux admins lors d'ajouts par les employés

---

## Architecture

```
GPS Device → Rust Ingest → RabbitMQ/Redis → BroadcastPositionCommandHandler
                                                    ↓
                                        ┌───────────┼───────────────┐
                                        ↓           ↓               ↓
                                  Speed Alert   Geofence Check   Driving Behavior
                                        ↓           ↓               ↓
                                   MediatR INotification (Events)
                                        ↓           ↓               ↓
                                   Notification Handlers (find admins, persist + push)
                                        ↓
                                   NotificationService.CreateAndSendAsync()
                                        ↓
                                   SignalR GpsHub → "NewNotification"
                                        ↓
                                   Frontend SignalRService → NotificationService
                                        ↓
                                   NotificationToastService → ToastService → Toast UI
```

---

## 1. Notifications Géofencing (Entrée / Sortie)

### Backend
| Fichier | Description |
|---------|-------------|
| `BroadcastPositionCommandHandler.cs` | Ajout de la méthode `CheckGeofences()` — vérifie chaque position GPS contre toutes les géofences actives de la société |
| `BroadcastPositionCommandHandler.cs` | Algorithme **Ray-Casting** pour polygones + **Haversine** pour cercles |
| `BroadcastPositionCommandHandler.cs` | Tracking d'état par véhicule (`_vehicleGeofenceState`) pour détecter les transitions entrée/sortie |
| `BroadcastPositionCommandHandler.cs` | Cache géofences par société (TTL 2 min) pour éviter les requêtes DB à chaque frame GPS |
| `GeofenceNotificationEvent.cs` | Event MediatR existant — publié lors d'une entrée ou sortie |
| `GeofenceNotificationHandler.cs` | Handler existant — persiste la notification et push via SignalR aux admins |

### Détails techniques
- **Détection entrée** : véhicule n'était PAS dans la géofence → il y EST maintenant → `AlertOnEntry = true`
- **Détection sortie** : véhicule ÉTAIT dans la géofence → il n'y EST plus → `AlertOnExit = true`
- **Double broadcast** : Event MediatR (notification persistée) + SignalR direct `GeofenceEvent` (temps réel UI)

### Frontend
- `SignalRService.geofenceEvent$` écoute les événements `GeofenceEvent` du hub
- `NotificationToastService` affiche un toast **info** (entrée) ou **warning** (sortie)

---

## 2. Notifications Comportement de Conduite

### Backend
| Fichier | Description |
|---------|-------------|
| `DrivingBehaviorNotificationEvent.cs` | **NOUVEAU** — Event MediatR pour comportements dangereux |
| `DrivingBehaviorNotificationHandler.cs` | **NOUVEAU** — Handler qui notifie les admins société |
| `BroadcastPositionCommandHandler.cs` | Publication de l'event quand `AlertType` ∈ `{harsh_braking, rapid_acceleration, sharp_turn, pothole, jerk}` |

### Types de comportement détectés
| Type | Titre notification | Priorité |
|------|-------------------|----------|
| `harsh_braking` | Freinage brusque | high |
| `rapid_acceleration` | Accélération rapide | normal |
| `sharp_turn` | Virage brusque | normal |
| `pothole` | Choc détecté (nid-de-poule) | low |
| `overspeed` | Excès de vitesse | high si > 140 km/h |

### Cooldown
- **3 minutes** par combinaison véhicule + type de comportement
- Évite le spam de notifications pour le même véhicule

### Frontend
- `SignalRService.alert$` écoute les alertes GPS du hub
- `NotificationToastService` affiche un toast **warning** (freinage, virage) ou **error** (excès de vitesse, SOS)

---

## 3. Notifications Actions Administratives (Employés)

### Backend
| Fichier | Description |
|---------|-------------|
| `AdminActionNotificationEvent.cs` | **NOUVEAU** — Event MediatR pour actions employés |
| `AdminActionNotificationHandler.cs` | **NOUVEAU** — Handler qui notifie tous les admins (sauf l'auteur de l'action) |
| `CreateVehicleCommandHandler.cs` | **MODIFIÉ** — Publie `AdminActionNotificationEvent` après création véhicule |
| `CreateDriverCommandHandler.cs` | **MODIFIÉ** — Publie `AdminActionNotificationEvent` après création chauffeur |
| `CreateUserCommandHandler.cs` | **MODIFIÉ** — Publie `AdminActionNotificationEvent` après création utilisateur |

### Actions notifiées
| Action | Message exemple | Module cible |
|--------|----------------|-------------|
| `vehicle_created` | "Ahmed a ajouté le véhicule Camion-01" | /vehicles |
| `driver_created` | "Ahmed a ajouté le chauffeur Mohamed Ali" | /drivers |
| `user_created` | "Ahmed a créé l'utilisateur Slim Ben Salah" | /users |
| `maintenance_created` | (prêt à brancher) | /maintenance |
| `cost_created` | (prêt à brancher) | /costs |
| `document_created` | (prêt à brancher) | /documents |
| `accident_created` | (prêt à brancher) | /accidents |
| `geofence_created` | (prêt à brancher) | /geofences |

### Comportement
- L'auteur de l'action n'est **PAS** notifié (exclusion dans le handler)
- Seuls les admins de la société reçoivent la notification
- La notification contient un `actionUrl` pour naviguer directement vers le module concerné

---

## 4. Frontend — Toast System

### Fichiers créés/modifiés
| Fichier | Description |
|---------|-------------|
| `toast-container.component.ts` | **NOUVEAU** — Composant standalone Angular qui affiche les toasts en haut à droite |
| `notification-toast.service.ts` | **NOUVEAU** — Service bridge qui connecte les événements SignalR aux toasts |
| `main.ts` | **MODIFIÉ** — Intègre le toast container et initialise le bridge au démarrage de l'app |

### Design du Toast
- Position fixe en haut à droite (z-index: 99999)
- 4 types visuels : **success** (vert), **error** (rouge), **warning** (orange), **info** (bleu)
- Animation slide-in depuis la droite
- Auto-dismiss configurable (5–10 secondes selon la priorité)
- Click pour fermer, hover effet visuel
- Dark theme cohérent avec l'UI existante

### Flux de données
```
SignalR Hub
  ├── "NewNotification" → NotificationService.newNotification$ → NotificationToastService → Toast
  ├── "GeofenceEvent"   → SignalRService.geofenceEvent$        → NotificationToastService → Toast
  └── "Alert"           → SignalRService.alert$                → NotificationToastService → Toast
```

---

## 5. Notifications existantes (déjà implémentées avant cette session)

| Type | Event | Handler | Déclencheur |
|------|-------|---------|-------------|
| `speed_alert` | `SpeedAlertNotificationEvent` | `SpeedAlertNotificationHandler` | Excès de vitesse (cooldown 5 min) |
| `geofence` | `GeofenceNotificationEvent` | `GeofenceNotificationHandler` | Entrée/sortie géofence |
| `maintenance_due` | `MaintenanceDueNotificationEvent` | `MaintenanceDueNotificationHandler` | Maintenance à échéance |

---

## 6. Récapitulatif des fichiers

### Fichiers CRÉÉS (4)
1. `src/GisAPI.Application/Features/Notifications/Events/DrivingBehaviorNotificationEvent.cs`
2. `src/GisAPI.Application/Features/Notifications/Events/DrivingBehaviorNotificationHandler.cs`
3. `src/GisAPI.Application/Features/Notifications/Events/AdminActionNotificationEvent.cs`
4. `src/GisAPI.Application/Features/Notifications/Events/AdminActionNotificationHandler.cs`
5. `gis-frontend/src/components/shared/toast-container.component.ts`
6. `gis-frontend/src/services/notification-toast.service.ts`

### Fichiers MODIFIÉS (4)
1. `src/GisAPI.Application/Features/Gps/Commands/BroadcastPosition/BroadcastPositionCommandHandler.cs` — Géofence check + driving behavior events
2. `src/GisAPI.Application/Features/Vehicles/Commands/CreateVehicle/CreateVehicleCommandHandler.cs` — Admin notification
3. `src/GisAPI.Application/Features/Drivers/Commands/CreateDriverCommand.cs` — Admin notification
4. `src/GisAPI.Application/Features/Users/Commands/CreateUser/CreateUserCommandHandler.cs` — Admin notification
5. `gis-frontend/src/main.ts` — Toast container + bridge initialization

---

## 7. Pour brancher de nouvelles notifications

Pour ajouter une notification à un nouveau module (ex: maintenance, coûts, sinistres) :

```csharp
// Dans le CommandHandler du module, injecter IPublisher et publier :
_ = _publisher.Publish(new AdminActionNotificationEvent(
    companyId, actorId, actor.FullName,
    "maintenance_created",  // ActionType
    "Vidange Camion-01",    // EntityName
    entity.Id,              // EntityId
    "maintenance"           // EntityType
), ct);
```

Le handler `AdminActionNotificationHandler` gère déjà tous les types listés — il suffit de publier l'event.

---

## 8. Build & Test

```bash
# Depuis c:\Users\Mega-PC\Desktop\GISV2
docker compose up -d --build gis-api frontend
```

Backend compile avec **0 erreurs, 0 warnings**.
