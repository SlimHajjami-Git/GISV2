# Migration Production Database Schema

## Objectif
Synchroniser le schéma de la base de données de production avec le schéma local (code).

## Fichiers

| Fichier | Description |
|---------|-------------|
| `PRODUCTION_migrate_schema.sql` | Script principal pour la production |
| `migrate_prod_to_local_schema.sql` | Version initiale (pour référence) |
| `migrate_fix_remaining.sql` | Script de correction (pour référence) |

## Changements effectués

### Nouvelles tables créées
- `societes` - Remplace `companies`
- `subscription_types` - Remplace `subscriptions`
- `roles` - Gestion des rôles utilisateurs
- `geofence_groups` - Groupes de geofences
- `poi_visits` - Visites des points d'intérêt
- `vehicle_user_assignments` - Assignations véhicule-utilisateur

### Colonnes ajoutées

**Table `users`:**
- `user_type` - Type d'utilisateur
- `role_id` - Référence au rôle
- `is_company_admin` - Flag admin
- `date_of_birth`, `cin`, `hire_date`, `license_number`, `license_expiry`

**Table `geofences`:**
- `group_id`, `icon_name`, `active_start_time`, `active_end_time`
- `active_days`, `max_stay_duration_minutes`, `notification_cooldown_minutes`

**Table `geofence_events`:**
- `device_id`, `address`, `duration_inside_seconds`, `is_notified`, `notified_at`

**Table `gps_positions`:**
- `event_key`

### Tables supprimées (avec backup)
- `companies` → Données migrées vers `societes`
- `employees` → Données migrées vers `users`
- `subscriptions` → Données migrées vers `subscription_types`
- `campaigns` → Supprimée

### Colonnes supprimées
- `gps_positions.is_bird_flight`
- `gps_positions.bird_flight_reason`
- `gps_positions.implicit_speed_kph`

## Procédure de migration

### 1. Backup complet
```bash
docker exec gisv2-postgres-1 pg_dump -U postgres gis_v2 > backup_before_migration.sql
```

### 2. Copier le script vers le conteneur
```bash
docker cp PRODUCTION_migrate_schema.sql gisv2-postgres-1:/tmp/migrate.sql
```

### 3. Exécuter la migration
```bash
docker exec gisv2-postgres-1 psql -U postgres -d gis_v2 -f /tmp/migrate.sql
```

### 4. Vérifier le résultat
```sql
-- Vérifier les nouvelles tables
SELECT tablename FROM pg_tables WHERE schemaname='public' 
AND tablename IN ('societes', 'subscription_types', 'roles', 'geofence_groups', 'poi_visits', 'vehicle_user_assignments');

-- Vérifier les migrations EF
SELECT * FROM "__EFMigrationsHistory" ORDER BY "MigrationId";

-- Vérifier les données migrées
SELECT COUNT(*) FROM societes;
SELECT COUNT(*) FROM subscription_types;
```

## Rollback

Les tables originales sont sauvegardées avec le préfixe `_backup_`:
- `_backup_companies_migration`
- `_backup_employees_migration`
- `_backup_subscriptions_migration`

Pour restaurer en cas de problème:
```sql
-- Recréer companies depuis backup
CREATE TABLE companies AS SELECT * FROM _backup_companies_migration;
-- etc.
```

Ou utiliser le backup complet:
```bash
docker exec -i gisv2-postgres-1 psql -U postgres -d gis_v2 < backup_before_migration.sql
```

## Notes importantes

1. **Le script est idempotent** - peut être exécuté plusieurs fois sans erreur
2. **Les erreurs "column does not exist"** pour tables déjà supprimées sont normales
3. **Testez toujours sur une copie** avant d'exécuter sur production
