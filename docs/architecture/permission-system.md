# Unified Permission System

## Overview

The permission system merges **role-based permissions** with **subscription-based permissions** into a unified hierarchical structure. This allows:

1. **Subscription-level control**: Features are enabled/disabled based on the company's subscription plan
2. **Role-level granularity**: Within available features, roles can be configured with specific permissions
3. **Automatic constraint enforcement**: Roles cannot grant permissions beyond their subscription limits

---

## Permission Hierarchy

```
CATEGORIES (Main Modules):
├── dashboard          - Tableau de bord (BASE)
├── monitoring         - Monitoring (requires: gps_tracking)
│   ├── view           - Accès au monitoring
│   ├── real_time      - Suivi GPS temps réel (requires: gps_tracking)
│   ├── history        - Historique des trajets (requires: history_playback)
│   └── alerts         - Alertes temps réel (requires: real_time_alerts)
├── vehicles           - Véhicules (BASE)
│   ├── view/create/edit/delete
│   └── gps_devices    - Gestion appareils GPS
├── geofences          - Géofences (BASE)
│   └── view/create/edit/delete
├── employees          - Employés (BASE)
│   ├── view/create/edit/delete
│   ├── drivers        - Gestion chauffeurs
│   └── staff          - Gestion personnel
├── maintenance        - Maintenance (BASE)
│   ├── view/create/edit/delete
│   ├── schedule       - Planification
│   ├── history        - Historique
│   └── alerts         - Alertes maintenance
├── costs              - Coûts (BASE)
│   ├── view/create/edit/delete
│   ├── fuel           - Coûts carburant
│   ├── maintenance    - Coûts maintenance
│   └── other          - Autres coûts
├── reports            - Rapports (BASE)
│   ├── view
│   └── export
├── advanced_reports   - Rapports avancés (requires: advanced_reports)
│   ├── view           - Accès rapports avancés
│   ├── trip           - Rapport trajet
│   ├── fuel           - Rapport carburant (requires: fuel_analysis)
│   ├── speed          - Rapport vitesse
│   ├── stops          - Rapport arrêt
│   ├── distance       - Rapport distance
│   ├── cost           - Rapport coût
│   ├── maintenance    - Rapport maintenance
│   └── behavior       - Comportement de conduite (requires: driving_behavior)
├── settings           - Paramètres (BASE)
│   ├── view
│   └── edit
├── users              - Utilisateurs (BASE)
│   └── view/create/edit/delete
└── api_access         - Accès API (requires: api_access)
    ├── enabled
    ├── read
    └── write
```

---

## Permission Types

### Base Permissions (Always Available)
These are included in all subscription plans:
- `dashboard` - Tableau de bord
- `vehicles` - Véhicules
- `geofences` - Géofences
- `employees` - Employés
- `maintenance` - Maintenance
- `costs` - Coûts
- `reports` - Rapports de base
- `settings` - Paramètres
- `users` - Utilisateurs

### Feature Permissions (Subscription-Dependent)
These require specific subscription features to be enabled:

| Permission | Subscription Feature |
|------------|---------------------|
| `monitoring` | `gps_tracking` |
| `monitoring.real_time` | `gps_tracking` |
| `monitoring.history` | `history_playback` |
| `monitoring.alerts` | `real_time_alerts` |
| `advanced_reports` | `advanced_reports` |
| `advanced_reports.fuel` | `fuel_analysis` |
| `advanced_reports.behavior` | `driving_behavior` |
| `api_access` | `api_access` |

---

## Mapping from Original Systems

### Role Permissions → New Structure

| Original | New Category | New Sub-permissions |
|----------|--------------|---------------------|
| Tableau de bord | `dashboard` | `view` |
| Surveillance | `monitoring` | `view`, `real_time`, `history`, `alerts` |
| Véhicules | `vehicles` | `view`, `create`, `edit`, `delete`, `gps_devices` |
| Géofences | `geofences` | `view`, `create`, `edit`, `delete` |
| Chauffeurs | `employees` | `drivers` |
| Employés | `employees` | `staff` |
| GPS | `vehicles` | `gps_devices` |
| Maintenance | `maintenance` | `view`, `create`, `edit`, `delete`, `schedule`, `history`, `alerts` |
| Coûts | `costs` | `view`, `create`, `edit`, `delete`, `fuel`, `maintenance`, `other` |
| Rapports | `reports` | `view`, `export` |
| Paramètres | `settings` | `view`, `edit` |
| Utilisateurs | `users` | `view`, `create`, `edit`, `delete` |

### Subscription Permissions → New Structure

| Original | New Location | Constraint |
|----------|--------------|------------|
| Suivi GPS temps réel | `monitoring.real_time` | Requires `gps_tracking` |
| Installation GPS incluse | Feature flag only | `gps_installation` |
| Alertes temps réel | `monitoring.alerts` | Requires `real_time_alerts` |
| Historique des trajets | `monitoring.history` | Requires `history_playback` |
| Rapports avancés | `advanced_reports.*` | Requires `advanced_reports` |
| Analyse de carburant | `advanced_reports.fuel` | Requires `fuel_analysis` |
| Comportement de conduite | `advanced_reports.behavior` | Requires `driving_behavior` |
| Accès API | `api_access.*` | Requires `api_access` |

---

## API Endpoints

### Get Permission Template
```
GET /api/admin/permissions/template
```
Returns the full permission structure with metadata for UI display.

### Get Subscription Permissions
```
GET /api/admin/permissions/subscription/{subscriptionId}
```
Returns available permissions for a specific subscription type.

---

## JSON Structure Example

### Role Permissions Object
```json
{
  "dashboard": { "view": true },
  "vehicles": { 
    "view": true, 
    "create": true, 
    "edit": true, 
    "delete": false,
    "gps_devices": true 
  },
  "monitoring": { 
    "view": true, 
    "real_time": true, 
    "history": true, 
    "alerts": false 
  },
  "advanced_reports": { 
    "view": true, 
    "trip": true, 
    "fuel": true, 
    "speed": true, 
    "stops": true, 
    "distance": true, 
    "cost": false, 
    "maintenance": true, 
    "behavior": false 
  }
}
```

---

## Validation Rules

1. **Category-level check**: If a category requires a subscription feature (e.g., `monitoring` requires `gps_tracking`), all sub-permissions are blocked if the feature is disabled.

2. **Sub-permission check**: Some sub-permissions have additional requirements (e.g., `advanced_reports.fuel` requires `fuel_analysis`).

3. **Create/Update enforcement**: Both `CreateRoleCommandHandler` and `UpdateRoleCommandHandler` validate permissions against the company's subscription before saving.

4. **Error response**: If permissions exceed subscription limits, an error is returned listing the specific permissions that are not allowed.

---

## Subscription Limits

In addition to feature permissions, subscriptions define resource limits:

| Limit | Description |
|-------|-------------|
| `max_vehicles` | Maximum number of vehicles |
| `max_users` | Maximum number of users |
| `max_gps_devices` | Maximum GPS devices |
| `max_geofences` | Maximum geofences |
| `history_retention_days` | Days to retain GPS history |
