# Guide de Migration Production - GIS V2

## 📋 Résumé des changements de base de données

### Nouvelles Tables (7)
| Table | Description | Source |
|-------|-------------|--------|
| `supplier_services` | Services des fournisseurs (N-N) | Section 3.1 |
| `AccidentClaims` | Déclarations de sinistres | Section 12.2 |
| `AccidentClaimThirdParties` | Tiers impliqués dans sinistres | Section 12.3 |
| `AccidentClaimDocuments` | Documents des sinistres | Section 12.4 |
| `MaintenanceTemplates` | Modèles d'entretien | Section 13.2 |
| `VehicleMaintenanceSchedules` | Planning entretiens véhicules | Section 13.3 |
| `MaintenanceLogs` | Historique des entretiens | Section 13.4 |

### Modifications Tables Existantes
| Table | Colonnes ajoutées |
|-------|-------------------|
| `vehicles` | InsuranceExpiry, TechnicalInspectionExpiry, TaxExpiry, RegistrationExpiry, TransportPermitExpiry |
| `vehicle_costs` | ExpiryDate, DocumentNumber, DocumentUrl |
| `suppliers` | PostalCode |
| `maintenance_records` | SupplierId |

---

## 🚀 Procédure de déploiement (docker-compose.prod.yml)

> **Note**: Les containers sont nommés `gisv2-postgres-1` et `gisv2-gis-api-1` (préfixe du dossier + service)

### Étape 1: Backup de la base de données
```bash
# Sur le serveur de production
ssh user@vm-belive-1
cd /root/GISV2

# Créer un backup
mkdir -p /root/backups
docker compose -f docker-compose.prod.yml exec -T postgres pg_dump -U postgres gis_v2 > /root/backups/gis_v2_backup_$(date +%Y%m%d_%H%M%S).sql
```

### Étape 2: Copier le script SQL
```bash
# Depuis votre machine locale
scp MIGRATION_PRODUCTION.sql user@vm-belive-1:/root/GISV2/
```

### Étape 3: Exécuter la migration
```bash
# Sur le serveur de production
cd /root/GISV2

# Copier le script dans le container postgres
docker cp MIGRATION_PRODUCTION.sql gisv2-postgres-1:/tmp/

# Exécuter le script
docker compose -f docker-compose.prod.yml exec -T postgres psql -U postgres -d gis_v2 -f /tmp/MIGRATION_PRODUCTION.sql
```

### Étape 4: Vérifier les tables créées
```bash
docker compose -f docker-compose.prod.yml exec -T postgres psql -U postgres -d gis_v2 -c "
SELECT table_name 
FROM information_schema.tables 
WHERE table_schema = 'public' 
AND table_name IN ('AccidentClaims', 'MaintenanceTemplates', 'VehicleMaintenanceSchedules', 'supplier_services')
ORDER BY table_name;
"
```

### Étape 5: Vérifier les colonnes vehicles
```bash
docker compose -f docker-compose.prod.yml exec -T postgres psql -U postgres -d gis_v2 -c "
SELECT column_name, data_type 
FROM information_schema.columns 
WHERE table_name = 'vehicles' 
AND column_name LIKE '%Expiry%';
"
```

### Étape 6: Redéployer l'API
```bash
cd /root/GISV2

# Rebuild et redémarrer l'API
docker compose -f docker-compose.prod.yml up -d --build gis-api

# Vérifier les logs
docker compose -f docker-compose.prod.yml logs -f gis-api --tail 50
```

### Étape 7: Vérifier l'API
```bash
# Test de santé
curl http://localhost:5000/health

# Test endpoint fournisseurs
curl -H "Authorization: Bearer <TOKEN>" http://localhost:5000/api/suppliers
```

---

## ⚠️ Rollback en cas de problème

### Restaurer le backup
```bash
cd /root/GISV2

# Arrêter l'API
docker compose -f docker-compose.prod.yml stop gis-api

# Restaurer la base
docker compose -f docker-compose.prod.yml exec -T postgres psql -U postgres -d gis_v2 < /root/backups/gis_v2_backup_XXXXXXXX.sql

# Redémarrer l'API (ancienne version)
docker compose -f docker-compose.prod.yml up -d gis-api
```

### Supprimer les nouvelles tables (si nécessaire)
```sql
DROP TABLE IF EXISTS "MaintenanceLogs";
DROP TABLE IF EXISTS "VehicleMaintenanceSchedules";
DROP TABLE IF EXISTS "MaintenanceTemplates";
DROP TABLE IF EXISTS "AccidentClaimDocuments";
DROP TABLE IF EXISTS "AccidentClaimThirdParties";
DROP TABLE IF EXISTS "AccidentClaims";
DROP TABLE IF EXISTS supplier_services;

ALTER TABLE vehicles DROP COLUMN IF EXISTS "InsuranceExpiry";
ALTER TABLE vehicles DROP COLUMN IF EXISTS "TechnicalInspectionExpiry";
ALTER TABLE vehicles DROP COLUMN IF EXISTS "TaxExpiry";
ALTER TABLE vehicles DROP COLUMN IF EXISTS "RegistrationExpiry";
ALTER TABLE vehicles DROP COLUMN IF EXISTS "TransportPermitExpiry";
```

---

## 📁 Fichiers de référence

| Fichier | Contenu |
|---------|---------|
| `MIGRATION_PRODUCTION.sql` | Script SQL complet à exécuter |
| `SPECIFICATIONS_BACKEND_JANVIER_2026.txt` | Spécifications détaillées |
| `RESUME_TRAVAUX_26_JANVIER_2026.txt` | Résumé des travaux effectués |
| `TRAVAUX_22_JANVIER_2026.txt` | Services Rust (pas de changement DB) |

---

## ✅ Checklist de déploiement

- [ ] Backup de la base effectué
- [ ] Script SQL copié sur le serveur
- [ ] Migration exécutée sans erreur
- [ ] Tables créées vérifiées
- [ ] Colonnes vehicles vérifiées
- [ ] API redéployée
- [ ] Logs sans erreur
- [ ] Endpoints testés
