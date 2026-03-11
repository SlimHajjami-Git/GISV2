# Rapport d'Intégration — Noron NR024 GPS Tracker

## 1. Résumé

Intégration complète du protocole GPS **Noron NR024** dans le service Rust d'ingestion GPS (GISV2).
Le décodeur a été porté depuis le projet .NET GISV1 (`NR024.cs`) vers Rust, avec gestion de l'accusé de réception (ACK/handshake) et intégration dans le pipeline existant (DB, Redis, RabbitMQ, services).

**Port TCP assigné : 6300**

---

## 2. Analyse du Protocole Noron NR024

### 2.1 Caractéristiques

| Aspect | Détail |
|--------|--------|
| **Type** | Protocole binaire, Little-Endian |
| **Transport GISV1** | UDP (port 6903) |
| **Transport GISV2** | TCP (port 6300) |
| **Coordonnées** | IEEE 754 float32 (pas NMEA) |
| **Horodatage** | Entier 32 bits à champs de bits |
| **ID Appareil** | 11 octets UTF-8 dans chaque paquet |
| **Handshake** | ACK fixe de 13 octets |

### 2.2 Structure des paquets

```
[nPackLen: u16 LE] [nFlag: u16 LE] [corps: variable]
```

### 2.3 Types de messages (nFlag)

| Flag | Nom | Description |
|------|-----|-------------|
| `0x0000` | Handshake | Login - l'appareil envoie son ID (11 octets) |
| `0x0003` | Alarm | Position + alarme |
| `0x0008` | Position Standard | Upload de position standard |
| `0x0032` | Position Étendue | Position + température + kilométrage |
| `0x0200` | Position + IP | Position avec préfixe IP/port (6 octets) |
| `0x0201` | Position + IP | Variante avec préfixe IP/port |
| `0x8009` | Control Response | Réponse à une commande serveur |
| `0x8201` | Position + IP | Variante avec préfixe IP/port |

### 2.4 Structure des données de position (30 octets)

| Champ | Taille | Type | Description |
|-------|--------|------|-------------|
| bEnable | 1 | u8 | GPS valide (0/1) |
| bAlarm | 1 | u8 | Type d'alarme |
| nSpeed | 1 | u8 | Vitesse (km/h) |
| nDirection | 2 | i16 LE | Cap (degrés) |
| fLongitude | 4 | f32 LE | Longitude IEEE 754 |
| fLatitude | 4 | f32 LE | Latitude IEEE 754 |
| lDateTime | 4 | i32 LE | Horodatage bit-packed |
| sUserID | 11 | UTF-8 | ID appareil |
| IoValue | 1 | u8 | État I/O (bit 0 = contact) |
| VolValue | 1 | u8 | Tension |

### 2.5 Champs étendus (0x0032 uniquement)

| Champ | Taille | Type |
|-------|--------|------|
| Temperature | 2 | i16 LE |
| Mileage | 4 | f32 LE (→ i32) |

### 2.6 Encodage de l'horodatage (bit-packed i32)

```
year   = (val >> 26) + 2000    (bits 26-31)
month  = (val >> 22) & 0x0F    (bits 22-25)
day    = (val >> 17) & 0x1F    (bits 17-21)
hour   = (val >> 12) & 0x1F    (bits 12-16)
minute = (val >> 6)  & 0x3F    (bits 6-11)
second = val & 0x3F            (bits 0-5)
```

### 2.7 Mécanisme d'Accusé de Réception (ACK)

Quand le tracker envoie un paquet `nFlag=0x0000` (handshake), le serveur **doit** répondre avec 13 octets fixes :

```
0D 0A 2A 4B 57 00 13 00 00 80 01 0D 0A
```

Décomposition :
- `0D 0A` → `\r\n` (marqueur début)
- `2A 4B 57 00` → `*KW\0` (signature)
- `13 00` → longueur du paquet (LE)
- `00 80` → flag 0x8000 (réponse de contrôle, LE)
- `01` → code d'erreur (succès)
- `0D 0A` → `\r\n` (marqueur fin)

**Source GISV1 :** `NR024TrackerShakeHandResp.cs`

---

## 3. Différences clés vs NEMS/AAP

| Aspect | NEMS (HH/AA) | Noron (NR024) |
|--------|-------------|---------------|
| Format | ASCII hexadécimal | Binaire pur |
| Coordonnées | Format NMEA (degrés/minutes) | Float IEEE 754 |
| Horodatage | Hex-encoded dans payload | Bit-packed i32 |
| ID appareil | Info frame séparée (IMEI:xxx) | Dans chaque paquet (11 octets) |
| Handshake | AAAA → AA06 (ASCII) | 0x0000 → 13 bytes binaires |
| FMS/Fuel | Oui (V3 frames) | Non (sauf temp/km en 0x0032) |
| MEMS | Oui (accéléromètre) | Non |

---

## 4. Fichiers Modifiés / Créés

### 4.1 Nouveau fichier

| Fichier | Description |
|---------|-------------|
| `src/telemetry/noron.rs` | Décodeur complet Noron NR024 (≈470 lignes) |

### 4.2 Fichiers modifiés

| Fichier | Modification |
|---------|-------------|
| `src/telemetry.rs` | Ajout `pub mod noron;` |
| `src/telemetry/model.rs` | Ajout `ProtocolMetadata` pour "noron" → NORON/NR024 |
| `src/transport.rs` | Gestion binaire Noron dans `handle_tcp_connection` + `route_noron_frame()` |
| `config/listeners.yaml` | Nouveau listener port 6300, protocol: noron |
| `Dockerfile` | EXPOSE 6300 |
| `docker-compose.yaml` | Port mapping 6300:6300 |
| `docker-compose.prod.yml` | Port mapping 6300:6300 |
| `k8s/02-applications.yaml` | containerPort + hostPort 6300 |

---

## 5. Architecture de l'Implémentation

### 5.1 Flux de données

```
Tracker Noron → TCP:6300
    ↓
handle_tcp_connection()  [transport.rs]
    ↓ (détection protocol == "noron")
    ↓
telemetry::noron::decode_buffer()  [noron.rs]
    ↓
    ├── Handshake → connection_map + HANDSHAKE_ACK (13 bytes)
    ├── Position  → NoronPosition → HhFrame → route_noron_frame()
    └── Unknown   → log + skip
    
route_noron_frame()  [transport.rs]
    ↓
    ├── Validation GPS (null island, out-of-range, future date)
    ├── GPS Validator (anti-teleportation)
    ├── GPS Stabilizer (anti-drift)
    ├── Geocoding (reverse geocode)
    ├── Speed Filter
    ↓
    ├── DB: ingest_hh_frame() → gps_positions table
    ├── Redis: cache_position() → real-time dashboard
    ├── RabbitMQ: publish_hh_frame() → .NET API consumer
    ↓
    ├── Stop Detector → vehicle_stops
    ├── Trip Detector → trips
    ├── Driving Events → driving_events
    └── Geofence Detector → geofence_events
```

### 5.2 Conversion Noron → HhFrame

Les données Noron sont converties en `HhFrame` (structure commune) pour réutiliser tout le pipeline existant :

| Champ HhFrame | Source Noron |
|---------------|-------------|
| `latitude` | `fLatitude` (f32 → f64) |
| `longitude` | `fLongitude` (f32 → f64) |
| `speed_kph` | `nSpeed` (u8 → f64, forcé à 0 si contact OFF) |
| `heading_deg` | `nDirection` (i16 → f64, clampé 0-360) |
| `ignition_on` | `IoValue & 0x01` |
| `is_valid` | `bEnable != 0` |
| `recorded_at` | `lDateTime` (bit-packed → NaiveDateTime) |
| `send_flag` | 11 (ALERT) si `bAlarm > 0`, sinon 1 (PERIODIC) |
| `power_voltage` | `VolValue` |
| `temperature_raw` | `Temperature` (0x0032 only) |
| `odometer_km` | `Mileage` (0x0032 only) |
| `fms_temperature_c` | `Temperature` (0x0032 only) |
| `fuel_raw` | 0 (Noron ne remonte pas le carburant) |
| `mems_x/y/z` | 0 (Noron n'a pas d'accéléromètre) |

---

## 6. Tests Unitaires

8 tests écrits et passés :

| Test | Vérifie |
|------|---------|
| `test_decode_handshake` | Décodage handshake, extraction device ID |
| `test_decode_position_standard` | Position 0x0008 : lat, lon, speed, heading, datetime |
| `test_decode_position_extended` | Position 0x0032 : température, kilométrage |
| `test_decode_datetime` | Conversion bit-packed → NaiveDateTime |
| `test_decode_datetime_y2k_base` | Edge case : date minimum (2000-01-01) |
| `test_multiple_packets_in_buffer` | Plusieurs paquets dans un seul buffer TCP |
| `test_speed_zero_when_ignition_off` | Vitesse forcée à 0 quand contact OFF |
| `test_handshake_ack_bytes` | Vérification des 13 octets ACK vs GISV1 |

---

## 7. Déploiement

### Docker (dev)
```bash
docker compose up -d --build gps-ingest
```

### Docker (prod)
```bash
docker compose -f docker-compose.prod.yml up -d --build gps-ingest
```

### Kubernetes
Le port 6300 est déjà configuré dans `k8s/02-applications.yaml` avec hostPort.

### Test manuel (netcat)
```bash
# Envoyer un handshake Noron (hex)
echo -ne '\x0f\x00\x00\x00NR024TEST01' | nc localhost 6300
```

---

## 8. Considérations Futures

1. **UDP Support** : Le protocole original GISV1 utilisait UDP. Si nécessaire, on pourra ajouter un listener UDP en implémentant le branch `TransportKind::Udp` dans `transport.rs`.

2. **Commandes distantes** : Le TCP writer est déjà enregistré dans `device_writers` pour envoyer des commandes au tracker Noron si besoin.

3. **Capteur de carburant** : Noron ne remonte pas de données carburant. Si un capteur externe est ajouté, il faudra mapper `fuel_raw` depuis un champ approprié.

4. **MEMS / Accéléromètre** : Non supporté par Noron. Les événements de conduite sont détectés par variation de vitesse uniquement.
