# Système d'Entretien Programmable - Spécifications Techniques

## 1. Vue d'ensemble

Le système d'entretien programmable permet aux utilisateurs de :
- Créer des types d'entretien configurables
- Programmer ces entretiens selon des critères temporels ou kilométriques
- Affecter ces programmes à des véhicules spécifiques
- Recevoir des notifications automatiques
- Enregistrer les réalisations avec coûts associés

---

## 2. Architecture des Données

### 2.1 Tables Principales

```
┌─────────────────────────┐
│  maintenance_templates  │  Types d'entretien (Vidange, Freins, etc.)
├─────────────────────────┤
│ - interval_km           │  Intervalle en kilomètres
│ - interval_months       │  Intervalle en mois
│ - warning_km/days       │  Seuils d'alerte
│ - critical_km/days      │  Seuils critiques
│ - estimated_cost        │  Coût estimé
└───────────┬─────────────┘
            │ 1:N
            ▼
┌─────────────────────────────────┐
│  vehicle_maintenance_schedules  │  Assignation véhicule ↔ template
├─────────────────────────────────┤
│ - next_due_km                   │  Prochain kilométrage
│ - next_due_date                 │  Prochaine date
│ - status                        │  ok/upcoming/due/overdue/critical
│ - custom_interval_*             │  Override des intervalles
│ - is_paused                     │  Programme en pause
└───────────┬─────────────────────┘
            │ 1:N
            ▼
┌─────────────────────────┐
│    maintenance_logs     │  Historique des réalisations
├─────────────────────────┤
│ - done_date / done_km   │  Date et km de réalisation
│ - actual_cost           │  Coût réel
│ - parts_replaced        │  Pièces remplacées (JSONB)
│ - quality_rating        │  Note qualité (1-5)
└─────────────────────────┘
```

### 2.2 Source du Kilométrage

Le kilométrage est obtenu automatiquement depuis **`gps_positions.odometer_km`** (FMS) :

```sql
-- Vue matérialisée pour le kilométrage actuel
SELECT COALESCE(
    (SELECT gp.odometer_km FROM gps_positions gp 
     WHERE gp.device_id = v.gps_device_id 
       AND gp.odometer_km > 0
     ORDER BY gp.recorded_at DESC LIMIT 1),
    v.mileage  -- Fallback sur kilométrage manuel
) AS current_km
FROM vehicles v;
```

---

## 3. Logique Métier

### 3.1 Calcul du Statut

| Statut | Condition KM | Condition Date |
|--------|--------------|----------------|
| **ok** | > 5000 km avant | > 90 jours avant |
| **upcoming** | ≤ 5000 km avant | ≤ 90 jours avant |
| **due** | ≤ warning_km (1000) | ≤ warning_days (30) |
| **critical** | ≤ critical_km | ≤ critical_days |
| **overdue** | Dépassé | Dépassé |

### 3.2 Calcul de la Prochaine Échéance

Après réalisation d'un entretien :

```csharp
// Utiliser l'intervalle personnalisé ou celui du template
var intervalKm = schedule.CustomIntervalKm ?? template.IntervalKm;
var intervalMonths = schedule.CustomIntervalMonths ?? template.IntervalMonths;

schedule.NextDueKm = doneKm + intervalKm;
schedule.NextDueDate = doneDate.AddMonths(intervalMonths);
schedule.Status = "ok";
schedule.NotificationCount = 0;
```

### 3.3 Génération des Notifications

Les notifications sont générées automatiquement quand :
1. Le statut passe à `due`, `overdue`, ou `critical`
2. La fréquence de rappel est respectée (défaut: 7 jours)
3. Le nombre max de rappels n'est pas atteint (défaut: 3)

```csharp
// Éviter les doublons
var existingToday = await _context.MaintenanceNotifications
    .AnyAsync(n => n.ScheduleId == scheduleId 
                && n.NotificationType == status
                && n.CreatedAt.Date == today);
```

---

## 4. API Endpoints

### 4.1 Templates (Types d'entretien)

| Méthode | Endpoint | Description |
|---------|----------|-------------|
| GET | `/api/maintenance/templates` | Liste des templates |
| POST | `/api/maintenance/templates` | Créer un template |
| PUT | `/api/maintenance/templates/{id}` | Modifier un template |
| DELETE | `/api/maintenance/templates/{id}` | Supprimer un template |
| GET | `/api/maintenance/templates/{id}/parts` | Pièces du template |
| POST | `/api/maintenance/templates/{id}/parts` | Ajouter une pièce |

### 4.2 Schedules (Programmes véhicule)

| Méthode | Endpoint | Description |
|---------|----------|-------------|
| GET | `/api/vehicles/{id}/maintenance` | Programmes du véhicule |
| POST | `/api/vehicles/{id}/maintenance` | Assigner un template |
| PUT | `/api/vehicles/{id}/maintenance/{scheduleId}` | Modifier le programme |
| DELETE | `/api/vehicles/{id}/maintenance/{scheduleId}` | Retirer le programme |
| POST | `/api/vehicles/{id}/maintenance/{scheduleId}/pause` | Mettre en pause |
| POST | `/api/vehicles/{id}/maintenance/{scheduleId}/done` | Marquer comme fait |

### 4.3 Alertes et Notifications

| Méthode | Endpoint | Description |
|---------|----------|-------------|
| GET | `/api/maintenance/alerts` | Entretiens dus/en retard |
| GET | `/api/maintenance/alerts/stats` | Statistiques globales |
| GET | `/api/maintenance/notifications` | Historique notifications |
| POST | `/api/maintenance/notifications/{id}/acknowledge` | Acquitter |

---

## 5. Jobs Planifiés

### 5.1 Rafraîchissement du Kilométrage

```sql
-- Toutes les heures
SELECT refresh_vehicle_mileage();
```

### 5.2 Mise à Jour des Statuts

```sql
-- Toutes les 15 minutes
SELECT update_maintenance_schedule_status();
```

### 5.3 Génération des Notifications

```sql
-- Chaque jour à 8h
SELECT generate_maintenance_notifications();
```

### 5.4 Implémentation .NET (Hosted Service)

```csharp
public class MaintenanceBackgroundService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _scheduler.UpdateAllScheduleStatusesAsync(ct: ct);
            await _scheduler.GenerateNotificationsAsync(ct: ct);
            await Task.Delay(TimeSpan.FromMinutes(15), ct);
        }
    }
}
```

---

## 6. Recommandations Interface Utilisateur

### 6.1 Dashboard Maintenance

```
┌────────────────────────────────────────────────────────────────┐
│  🔧 TABLEAU DE BORD MAINTENANCE                                │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐          │
│  │    3     │ │    5     │ │   12     │ │   45     │          │
│  │ EN RETARD│ │   DÛS    │ │ À VENIR  │ │   OK     │          │
│  │   🔴     │ │   🟠     │ │   🟡     │ │   🟢     │          │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘          │
│                                                                │
│  ┌─────────────────────────────────────────────────────────┐  │
│  │ ALERTES PRIORITAIRES                                    │  │
│  ├─────────────────────────────────────────────────────────┤  │
│  │ 🔴 Vidange - Camion 01 (ABC-123)     -1500 km / -15j   │  │
│  │ 🔴 Plaquettes frein - Fourgon 03     -800 km           │  │
│  │ 🟠 Filtre à air - Camion 05          500 km restants   │  │
│  │ 🟠 Climatisation - Camion 02         12 jours restants │  │
│  └─────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────┘
```

### 6.2 Vue Véhicule - Onglet Maintenance

```
┌────────────────────────────────────────────────────────────────┐
│  🚛 CAMION 01 - ABC-123                     KM: 125,450       │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  ENTRETIENS PROGRAMMÉS                    [+ Ajouter]         │
│  ───────────────────────────────────────────────────────────  │
│                                                                │
│  ┌─────────────────────────────────────────────────────────┐  │
│  │ 🔴 Vidange moteur                      OVERDUE         │  │
│  │    Dernier: 12/10/2025 à 115,000 km                    │  │
│  │    Prochain: 125,000 km (dépassé de 450 km)           │  │
│  │    Coût estimé: 80 TND                                 │  │
│  │    [Marquer comme fait] [Pause] [Modifier]             │  │
│  └─────────────────────────────────────────────────────────┘  │
│                                                                │
│  ┌─────────────────────────────────────────────────────────┐  │
│  │ 🟢 Courroie distribution                OK             │  │
│  │    Dernier: 01/03/2024 à 85,000 km                     │  │
│  │    Prochain: 185,000 km (59,550 km restants)          │  │
│  │    [Marquer comme fait] [Pause] [Modifier]             │  │
│  └─────────────────────────────────────────────────────────┘  │
│                                                                │
│  HISTORIQUE DES INTERVENTIONS              [Voir tout]        │
│  ───────────────────────────────────────────────────────────  │
│  │ 12/10/2025 │ Vidange moteur   │ 80 TND  │ Garage ABC │    │
│  │ 01/03/2024 │ Courroie distrib │ 600 TND │ Garage XYZ │    │
└────────────────────────────────────────────────────────────────┘
```

### 6.3 Modal "Marquer comme fait"

```
┌────────────────────────────────────────────────────────────────┐
│  ✅ ENREGISTRER L'ENTRETIEN                              [X]  │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  Entretien: Vidange moteur                                    │
│  Véhicule: Camion 01 (ABC-123)                                │
│                                                                │
│  ┌──────────────────────┐ ┌────────────────────────────────┐  │
│  │ Date *               │ │ Kilométrage *                  │  │
│  │ [31/01/2026      📅] │ │ [125,450        ] km           │  │
│  └──────────────────────┘ └────────────────────────────────┘  │
│                                                                │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │ Fournisseur/Garage                                       │ │
│  │ [Sélectionner...                               ▼]       │ │
│  └──────────────────────────────────────────────────────────┘ │
│                                                                │
│  COÛTS                                                        │
│  ┌────────────────────┐ ┌────────────────────┐               │
│  │ Main d'œuvre       │ │ Pièces             │               │
│  │ [45.00        ] TND│ │ [35.00        ] TND│               │
│  └────────────────────┘ └────────────────────┘               │
│                                                                │
│  PIÈCES REMPLACÉES                         [+ Ajouter]       │
│  ┌──────────────────────────────────────────────────────────┐│
│  │ ☑ Huile moteur 5W30 (5L)              25.00 TND         ││
│  │ ☑ Filtre à huile                      10.00 TND         ││
│  └──────────────────────────────────────────────────────────┘│
│                                                                │
│  ┌──────────────────────────────────────────────────────────┐│
│  │ Notes                                                    ││
│  │ [                                                    ]   ││
│  └──────────────────────────────────────────────────────────┘│
│                                                                │
│  ┌──────────────────────────────────────────────────────────┐│
│  │ Qualité du service: ⭐⭐⭐⭐⭐                           ││
│  └──────────────────────────────────────────────────────────┘│
│                                                                │
│                      [Annuler]  [💾 Enregistrer]              │
└────────────────────────────────────────────────────────────────┘
```

### 6.4 Configuration des Templates

```
┌────────────────────────────────────────────────────────────────┐
│  ⚙️ TYPES D'ENTRETIEN                        [+ Nouveau]      │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  Filtrer: [Tous      ▼] [Actifs uniquement ☑]                │
│                                                                │
│  ┌───────────────────────────────────────────────────────────┐│
│  │ Nom              │ Catégorie │ Intervalle │ Coût │ Actions││
│  ├───────────────────────────────────────────────────────────┤│
│  │ Vidange moteur   │ Moteur    │ 10,000 km  │ 80   │ ✏️ 🗑️ ││
│  │                  │           │ OU 6 mois  │      │       ││
│  ├───────────────────────────────────────────────────────────┤│
│  │ Plaquettes frein │ Freinage  │ 30,000 km  │ 150  │ ✏️ 🗑️ ││
│  ├───────────────────────────────────────────────────────────┤│
│  │ Climatisation    │ Climat.   │ 24 mois    │ 120  │ ✏️ 🗑️ ││
│  └───────────────────────────────────────────────────────────┘│
└────────────────────────────────────────────────────────────────┘
```

---

## 7. Codes Couleur et Icônes

| Statut | Couleur | Badge | Icône |
|--------|---------|-------|-------|
| ok | Vert `#22c55e` | `bg-green-100 text-green-800` | ✅ |
| upcoming | Jaune `#eab308` | `bg-yellow-100 text-yellow-800` | 🔔 |
| due | Orange `#f97316` | `bg-orange-100 text-orange-800` | ⚠️ |
| critical | Rouge foncé `#dc2626` | `bg-red-200 text-red-900` | 🚨 |
| overdue | Rouge `#ef4444` | `bg-red-100 text-red-800` | ❌ |
| paused | Gris `#6b7280` | `bg-gray-100 text-gray-800` | ⏸️ |

---

## 8. Notifications Push/Email

### Format Email

```
Sujet: ⚠️ Entretien dû - Vidange moteur - Camion 01 (ABC-123)

Bonjour,

L'entretien suivant est dû pour votre véhicule :

🚛 Véhicule: Camion 01 (ABC-123)
🔧 Entretien: Vidange moteur
📅 Échéance: Dans 15 jours OU 500 km
📍 Kilométrage actuel: 124,500 km

Détails:
- Dernier entretien: 12/10/2025 à 115,000 km
- Coût estimé: 80 TND
- Pièces nécessaires: Huile 5W30, Filtre à huile

[Planifier l'entretien] [Voir les détails]

---
Calypso Fleet Management
```

---

## 9. Fichiers Créés/Modifiés

| Fichier | Description |
|---------|-------------|
| `migrations/2026_01_31_enhanced_maintenance_system.sql` | Script SQL complet |
| `GisAPI.Domain/Entities/MaintenanceTemplate.cs` | Entités mises à jour |
| `GisAPI.Application/Services/MaintenanceSchedulerService.cs` | Service de calcul |

---

## 10. Prochaines Étapes

1. **Exécuter la migration SQL** sur la base de données
2. **Ajouter les DbSets** dans `GisDbContext`
3. **Créer les configurations EF Core** pour les nouvelles entités
4. **Implémenter le BackgroundService** pour les jobs planifiés
5. **Créer les endpoints API** manquants
6. **Développer le composant Angular** pour l'UI
7. **Tester avec des données réelles**
