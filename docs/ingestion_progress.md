# Historique – Service d'ingestion GPS (Rust)

_Mis à jour le 27 nov. 2025_

## Étapes réalisées

1. **Analyse initiale & architecture**
   - Lecture de la documentation `docs/architecture` (vision, legacy, roadmap) pour cadrer la réécriture de BeliveGis.
   - Définition du plan d’ingestion : service Rust unique, multi-protocoles, publication MQ, stockage PostgreSQL.

2. **Base de données**
   - Création des tables `devices` et `positions` (structure minimale).
   - Enrichissement : colonnes fuel/power/MEMS/flags, tables `device_events`, `stop_events`, `fuel_readings`, champ `protocol_type`.
   - Choix de PostgreSQL + `sqlx` comme couche d’accès.

3. **Bootstrap Rust**
   - Ajout des dépendances (`tokio`, `sqlx`, `serde_yaml`, `chrono`, `hex`, `serde_json`, etc.).
   - Chargement de `.env` via `dotenvy`, connexion à PostgreSQL.
   - Mise en place d’un fichier de configuration `config/listeners.yaml` listant les ports par protocole (6100, 6200, 6210).

4. **Infrastructure réseau**
   - Module `config` (lecture YAML) et `transport` (listeners TCP multi-ports, log en hex, Ctrl+C shutdown propre).
   - `transport::route_payload` dispatch sur les protocoles (actuellement `gps_type_1`).

5. **Parser HH**
   - Module `telemetry::model` (struct `HhFrame`, enums `FrameKind`, `FrameVersion`).
   - Module `telemetry::hh` : parse V1/V3, conversion timestamp, lat/lon, fuel, MEMS, flags, etc.
   - Test unitaire `parse_v3_sample_frame` validé avec la trame d’exemple du fabricant.

6. **Persistance**
   - Module `db` : `Database::ingest_hh_frame` avec `ensure_device`, `insert_position`, `insert_event`.
   - Intégration complète : chaque payload HH est parsé puis stocké (positions + events) via `Database`.

7. **Identification des boîtiers & trames INFO**
   - Ajout du parser `parse_info_frame` (HH01) pour extraire firmware / ICC / IMEI et alimenter la table `devices`.
   - `transport::route_payload` mémorise dynamiquement IMEI ↔ connexion (plus de `device_uid` statiques dans la config).
   - Les trames suivantes réutilisent automatiquement l’IMEI appris avant d’écrire en base.

8. **Préparation de la publication RabbitMQ**
   - Bootstrap du module `publisher` : initialisation conditionnelle via `TelemetryPublisher::from_env()` dans `main.rs`.
   - Le transport passe désormais un publisher optionnel aux listeners pour permettre l’émission d’événements après la persistance.

9. **Dockerisation du service**
   - Ajout d’un `Dockerfile` multi-stage (build Rust + runtime Debian) pour `gps-ingest-rust`.
   - `docker-compose.yaml` démarre Postgres, RabbitMQ (port 5672/15672) et le service d’ingestion (ports 6100/6200/6210).

## Prochaines étapes

- Extraire l’IMEI réel depuis les trames INFO/metadata pour remplacer `"UNKNOWN_DEVICE"` lors de l’insertion.
- Ajouter les autres protocoles (définir un trait `TelemetryDecoder`, modules dédiés, routing config).
- Publier les événements normalisés vers RabbitMQ et brancher les services .NET CQRS.
- Implémenter la détection d’arrêts et alimenter `stop_events`.
