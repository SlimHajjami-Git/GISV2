# Analyse du Flux GPS - Logique Idéale vs Code Existant

## 1. FLUX IDÉAL (Ce qu'on veut)

### Étape 1: Réception de la trame GPS
```
GPS Device → TCP Socket → Rust Ingestion Service
```
**Attendu:**
- Réception sur port TCP (ex: 5050)
- Parsing du protocole (HH, AAP, etc.)
- Extraction: lat, lon, speed, heading, ignition, timestamp

### Étape 2: Validation
**Attendu:**
- Vérifier coordonnées valides (-90 ≤ lat ≤ 90, -180 ≤ lon ≤ 180)
- Vérifier timestamp raisonnable (pas dans le futur, pas trop ancien)
- Vérifier device_uid existe en base

### Étape 3: Sauvegarde en Base de Données
**Attendu:**
- INSERT INTO gps_positions (device_id, lat, lon, speed, heading, ignition, recorded_at, ...)
- La position est TOUJOURS sauvée (sauf filtre véhicule arrêté)
- Retourner l'ID de la position insérée

### Étape 4: Publication RabbitMQ
**Attendu:**
- Publier IMMÉDIATEMENT après l'insert DB
- Payload JSON avec: device_uid, lat, lon, speed, heading, ignition, recorded_at
- Exchange: telemetry.raw, Routing key: hh

### Étape 5: Consommation C# Backend
**Attendu:**
- Consumer écoute la queue gps.telemetry.dotnet
- Désérialise le message JSON
- Envoie à MediatR (BroadcastPositionCommand)

### Étape 6: Broadcast SignalR
**Attendu:**
- Handler récupère device → vehicle → company
- Broadcast à tous les clients connectés de la company
- Payload: vehicleId, lat, lon, speed, isMoving, ignitionOn, timestamp

### Étape 7: Réception Frontend
**Attendu:**
- SignalR reçoit PositionUpdate
- Met à jour le véhicule dans la liste
- Met à jour le marqueur sur la carte
- Marqueur reflète la DERNIÈRE position reçue

---

## 2. CODE EXISTANT - ANALYSE

### Étape 1: Réception (transport.rs)
```rust
// ✅ CORRECT - Réception TCP et parsing
database.ingest_hh_frame(&resolved_uid, protocol, &frame, &event_key).await?;
```

### Étape 2: Validation (db.rs)
```rust
// ⚠️ FILTRE VÉHICULE ARRÊTÉ
if is_stopped {
    if elapsed_secs < STOPPED_VEHICLE_INTERVAL_SECS (20 min) {
        // SKIP - position non enregistrée!
        return Ok(());
    }
}
```
**Problème potentiel:** Si véhicule arrêté, position non enregistrée pendant 20 min.
Mais la publication RabbitMQ se fait APRÈS l'insert, donc pas de broadcast non plus!

### Étape 3: Insert DB (db.rs)
```rust
// ✅ CORRECT
let _position_id = self.insert_position(device_id, frame, event_key).await?;
```

### Étape 4: Publication RabbitMQ (transport.rs)
```rust
// ✅ CORRECT - Après l'insert
if let Some(publisher) = publisher {
    publisher.publish_hh_frame(&resolved_uid, protocol, &frame).await?;
}
```

### Étape 5-6: C# Consumer + Handler
```csharp
// ✅ CORRECT
await _gpsHubService.SendPositionUpdateAsync(device.CompanyId, positionUpdate);
```

### Étape 7: Frontend (monitoring.component.ts)
```typescript
// ✅ CORRECT
handlePositionUpdate(update: PositionUpdate) {
    vehicle.currentLocation = { lat: update.latitude, lng: update.longitude };
    this.updateSingleVehicleMarker(vehicle);
}
```

---

## 3. PROBLÈMES IDENTIFIÉS

### Problème 1: Filtre véhicule arrêté coupe TOUT le flux
**Localisation:** `db.rs` ligne 49-64
```rust
if is_stopped {
    if elapsed_secs < STOPPED_VEHICLE_INTERVAL_SECS {
        // Position PAS insérée
        // Donc RabbitMQ PAS publié (car on fait return avant)
        // Donc SignalR PAS broadcast
        // Donc Frontend PAS mis à jour
        return Ok(()); // <-- PROBLÈME ICI
    }
}
```

**Impact:** Pendant 20 minutes, aucune mise à jour live pour véhicules arrêtés.

### Problème 2: Publication RabbitMQ dans transport.rs, pas dans db.rs
**Localisation:** `transport.rs` ligne 640-645

Le flux actuel:
1. `transport.rs` appelle `database.ingest_hh_frame()`
2. Si véhicule arrêté, `ingest_hh_frame()` retourne sans insérer
3. `transport.rs` continue et appelle `publisher.publish_hh_frame()`

**WAIT - C'est correct!** La publication est dans transport.rs APRÈS l'appel à ingest.
Donc même si l'insert est skippé, la publication devrait se faire...

Vérifions le code:

```rust
// transport.rs ligne 501-504
database.ingest_hh_frame(&resolved_uid, protocol, &frame, &event_key).await?;
// ... autres traitements ...
// ligne 640-645
if let Some(publisher) = publisher {
    publisher.publish_hh_frame(&resolved_uid, protocol, &frame).await?;
}
```

**Conclusion:** La publication RabbitMQ se fait TOUJOURS, même si l'insert est skippé.
Donc le live devrait recevoir toutes les positions via SignalR.

### Problème 3: Données affichées viennent de sources différentes
**Live initial:** `getVehiclesWithPositions()` → lit depuis `gps_positions` (DB)
**Live updates:** SignalR → données du publisher (pas de DB)
**Playback:** `getVehicleHistory()` → lit depuis `gps_positions` (DB)

Si une position est publiée via SignalR mais PAS insérée en DB:
- Live initial: ancienne position (DB)
- Live update: nouvelle position (SignalR) ✓
- Playback: ancienne position (DB)

**C'est le bug!** Le playback lit la DB, mais certaines positions ne sont pas en DB.

---

## 4. SOLUTION PROPOSÉE

### Option A: Toujours insérer, toujours publier
Modifier `db.rs` pour insérer TOUTES les positions, pas de filtre 20 min.
- Avantage: Cohérence parfaite DB/Live/Playback
- Inconvénient: Plus de données en DB

### Option B: Séparer insertion et publication
Le code actuel fait déjà ça! Mais le problème est que:
- `ingest_hh_frame` dans `db.rs` fait `return Ok(())` avant l'insert
- `transport.rs` continue quand même et publie

Donc le live DEVRAIT fonctionner. Vérifions si c'est vraiment le cas...

### Option C: Publier AVANT le filtre d'insertion
Modifier le flux pour:
1. Publier à RabbitMQ (toujours)
2. Puis décider si on insert en DB

---

## 5. ANALYSE DÉTAILLÉE DU FLUX

### Flux dans transport.rs (ligne 501-645):
```rust
// 1. Appel à ingest_hh_frame
database.ingest_hh_frame(&resolved_uid, protocol, &frame, &event_key).await?;

// 2. ... autres services (stops, fuel, geofence, trips) ...

// 3. Publication RabbitMQ (TOUJOURS exécuté si étape 1 retourne Ok)
if let Some(publisher) = publisher {
    publisher.publish_hh_frame(&resolved_uid, protocol, &frame).await?;
}
```

### Flux dans db.rs (ingest_hh_frame_impl):
```rust
// Si véhicule arrêté ET elapsed < 20 min
if is_stopped && elapsed_secs < STOPPED_VEHICLE_INTERVAL_SECS {
    self.update_device_last_communication(device_id).await?;
    return Ok(()); // <-- Retourne OK, pas d'erreur
}

// Sinon: insert position
let _position_id = self.insert_position(device_id, frame, event_key).await?;
```

### CONCLUSION:
Le `return Ok(())` dans db.rs N'EST PAS une erreur.
L'opérateur `?` dans transport.rs ne propage que les erreurs (`Err`).
Donc `Ok(())` permet au code de continuer vers la publication RabbitMQ.

**✅ La publication RabbitMQ se fait TOUJOURS, même quand l'insert est skippé.**

---

## 6. VRAI PROBLÈME IDENTIFIÉ

Le problème n'est PAS dans le flux Rust → RabbitMQ → SignalR.

Le problème est dans la **synchronisation des sources de données**:

### Scénario problématique:
1. Page chargée à 12:50 → `getVehiclesWithPositions()` lit DB → position A
2. Véhicule bouge à 12:51 → position B publiée via SignalR → frontend mis à jour
3. Véhicule bouge à 12:52 → position C publiée via SignalR → frontend mis à jour
4. User lance playback 12:50-12:58 → lit DB → voit positions A, B, C
5. Playback montre position C à la fin
6. **Mais le marqueur live a été masqué!** 

Le fix appliqué (masquer tous les marqueurs live pendant playback) résout ce problème.

### Autre scénario (timezone):
L'endpoint history ajoute +1h pour compenser le timezone:
```csharp
var adjustedFrom = from.Value.AddHours(1);
var adjustedTo = to.Value.AddHours(1);
```

Mais l'endpoint live (`getVehiclesWithPositions`) ne fait PAS cet ajustement.
Cela peut causer un décalage entre les positions affichées.

---

## 7. CORRECTIONS RECOMMANDÉES

### Correction 1: Timezone (déjà en place dans history, vérifier coherence)
S'assurer que TOUTES les requêtes de positions utilisent le même ajustement timezone.

### Correction 2: Masquer marqueurs live (FAIT)
Tous les marqueurs live sont maintenant masqués pendant le playback.

### Correction 3 (optionnelle): Unifier les sources de données
Faire en sorte que le playback et le live utilisent exactement la même logique de requête.
