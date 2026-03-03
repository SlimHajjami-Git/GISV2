# GIS V2 — Migration K3s (02 Mars 2026)

## Ce qui a été fait aujourd'hui

### 1. Installation K3s sur le VPS (41.231.5.146)
- Installation de K3s (cluster Kubernetes léger, single-node)
- Configuration optimisée pour le VPS 8GB RAM
- ServiceLB désactivé (on utilise hostNetwork à la place)

### 2. Manifests Kubernetes créés
Les fichiers dans `k8s/` :
- `00-namespace-secrets.yaml` — Namespace `gisv2`, secrets, config
- `01-infrastructure.yaml` — PostgreSQL, Redis, RabbitMQ (StatefulSets)
- `02-applications.yaml` — gis-api, gps-ingest, frontend, mobile (Deployments)
- `03-geo-services.yaml` — Valhalla (routing), Nominatim (geocoding)
- `04-ingress.yaml` — Traefik ingress (routing HTTP/HTTPS)
- `deploy.sh` — Script de déploiement complet
- `update.sh` — Script de mise à jour des services

### 3. Problèmes résolus
- **Frontend CrashLoopBackOff** : Nginx essayait de résoudre `osrm` (service absent). Résolu avec un ConfigMap Nginx simplifié qui sert uniquement les fichiers statiques (Traefik gère le routing API)
- **Traefik ports 80/443** : ServiceLB désactivé → configuré `hostNetwork: true` + `runAsUser: 0` via HelmChartConfig pour que Traefik écoute directement sur les ports 80/443 de la machine
- **Espace disque** : 60GB récupérés via `docker system prune`

### 4. Migration base de données
- **Backup** de la DB production depuis l'ancien Docker Compose :
  ```bash
  docker run --rm -v gisv2_postgres_data:/var/lib/postgresql/data -v ~/:/backup \
    postgis/postgis:16-3.4-alpine bash -c \
    "docker-entrypoint.sh postgres &>/dev/null & sleep 5 && pg_dump -U postgres gis_v2 | gzip > /backup/gis_v2_backup.sql.gz && echo 'BACKUP OK'"
  ```
- **Restore** dans le pod K3s PostgreSQL :
  ```bash
  sudo kubectl cp ~/gis_v2_backup.sql.gz gisv2/postgres-0:/tmp/gis_v2_backup.sql.gz
  sudo kubectl exec -n gisv2 postgres-0 -- bash -c "
    psql -U postgres -c \"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='gis_v2' AND pid <> pg_backend_pid();\" &&
    dropdb -U postgres gis_v2 &&
    createdb -U postgres gis_v2 &&
    gunzip -c /tmp/gis_v2_backup.sql.gz | psql -U postgres gis_v2 &&
    echo 'RESTORE OK'"
  ```
- **Résultat** : 157,636 positions GPS + toutes les données restaurées

### 5. Ingress configuré
- Routing par domaine (belive.calypso.com, mobile.calypso.com, api.calypso.com)
- Routing par IP (http://41.231.5.146) → frontend + API
- Redirect HTTP → HTTPS automatique (pour les domaines)

### 6. État actuel des services
| Service      | Status       |
|-------------|-------------|
| Frontend     | ✅ Running   |
| Mobile       | ✅ Running   |
| GIS-API      | ✅ Running   |
| GPS-Ingest   | ✅ Running   |
| PostgreSQL   | ✅ Running   |
| Redis        | ✅ Running   |
| RabbitMQ     | ✅ Running   |
| Valhalla     | ✅ Running   |
| Nominatim    | ⏳ Import OSM |
| Traefik      | ✅ Ports 80/443 |

---

## Comment configurer le DNS (quand tu es prêt)

### Étape 1 : Aller sur le panel DNS Topnet
1. Connecte-toi à ton **espace client Topnet**
2. Va dans la gestion du domaine `calypso.com`
3. Cherche la section **Zone DNS** ou **DNS Records**

### Étape 2 : Créer les enregistrements A
Ajoute ces 3 enregistrements :

| Type | Nom      | Valeur         | TTL  |
|------|----------|---------------|------|
| A    | belive   | 41.231.5.146  | 3600 |
| A    | mobile   | 41.231.5.146  | 3600 |
| A    | api      | 41.231.5.146  | 3600 |

> **Note** : Le champ "Nom" peut aussi être demandé en format complet :
> `belive.calypso.com`, `mobile.calypso.com`, `api.calypso.com`

### Étape 3 : Attendre la propagation DNS (5-30 min)
```bash
# Tester depuis ton PC si le DNS est propagé :
nslookup belive.calypso.com
# Doit retourner : 41.231.5.146
```

### Étape 4 : Vérifier les certificats TLS
Les certificats Let's Encrypt seront automatiquement générés par cert-manager.
```bash
# Sur le VPS, vérifier l'état des certificats :
sudo kubectl get certificates -n gisv2
sudo kubectl get certificaterequests -n gisv2

# Vérifier les challenges ACME :
sudo kubectl get challenges -n gisv2
```

### Étape 5 : Tester
Ouvre dans ton navigateur :
- https://belive.calypso.com (frontend)
- https://mobile.calypso.com (mobile PWA)
- https://api.calypso.com/api/health (API health check)

---

## Commandes utiles K3s

```bash
# Voir tous les pods
sudo kubectl get pods -n gisv2

# Logs d'un service
sudo kubectl logs -n gisv2 -l app=gis-api --tail=50
sudo kubectl logs -n gisv2 -l app=frontend --tail=50

# Redémarrer un service
sudo kubectl rollout restart deployment/gis-api -n gisv2

# Mise à jour après git pull
cd ~/GISV2 && git pull
sudo kubectl apply -f k8s/

# Rebuild et redéployer un service (depuis le VPS)
bash k8s/update.sh frontend
bash k8s/update.sh api
bash k8s/update.sh all

# Accéder à la DB
sudo kubectl exec -n gisv2 -it postgres-0 -- psql -U postgres gis_v2

# Scaler un service
sudo kubectl scale deployment/gis-api -n gisv2 --replicas=2
```

---

## Backup régulier (recommandé)

```bash
# Backup de la DB K3s
sudo kubectl exec -n gisv2 postgres-0 -- bash -c \
  "pg_dump -U postgres gis_v2 | gzip" > ~/gis_v2_backup_$(date +%Y%m%d).sql.gz
```
