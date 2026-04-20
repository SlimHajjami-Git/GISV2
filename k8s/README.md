# GIS V2 — Kubernetes (K3s) Production Deployment

## Architecture

```
Internet
   │
   ├── TCP 6100/6200/6210 ──→ [gps-ingest] (Rust) ──→ PostgreSQL + Redis + RabbitMQ
   │
   ├── HTTPS belive.calypso.com
   │       ├── /api/*     ──→ [gis-api] x2 (.NET)
   │       ├── /gps-hub   ──→ [gis-api] (SignalR WebSocket)
   │       └── /*         ──→ [frontend] x2 (Angular + Nginx)
   │
   ├── HTTPS mobile.calypso.com
   │       ├── /api/*     ──→ [gis-api]
   │       └── /*         ──→ [mobile] (Ionic + Nginx)
   │
   └── HTTPS api.calypso.com ──→ [gis-api]

Internal:
   [gis-api] ──→ [valhalla] (routing)
   [gis-api] ──→ [nominatim] (geocoding)
```

## Prerequisites

| Resource | Minimum | Recommended |
|----------|---------|-------------|
| **RAM**  | 8 GB    | 16 GB       |
| **CPU**  | 4 vCPU  | 8 vCPU      |
| **Disk** | 80 GB SSD | 160 GB SSD |
| **OS**   | Ubuntu 22.04+ | Ubuntu 24.04 |

## Quick Start (First Deployment)

### 1. SSH into your VPS
```bash
ssh root@your-vps-ip
```

### 2. Clone the repository
```bash
cd /opt
git clone https://github.com/SlimHajjami-Git/GISV2.git
cd GISV2
```

### 3. Configure secrets and domain
```bash
# Edit secrets (CHANGE ALL PASSWORDS!)
nano k8s/00-namespace-secrets.yaml

# Domains are pre-configured:
#   belive.calypso.com  → frontend
#   mobile.calypso.com  → mobile app
#   api.calypso.com     → API direct access
# To change domains, edit k8s/04-ingress.yaml and k8s/02-applications.yaml (CORS)
```

### 3b. Provision the Firebase service account Secret (one-time)

The mobile push notification feature needs a Firebase Admin SDK service
account JSON (different from `google-services.json`). It is **never**
committed to the repo.

1. On your workstation: Firebase Console → Project Settings → Service
   accounts → "Generate new private key" → save as `firebase-sa.json`.
2. Copy the file onto the server (`/opt/GISV2/firebase-sa.json` is fine).
3. Create the K8s Secret:
   ```bash
   kubectl create secret generic firebase-sa -n gisv2 \
     --from-file=sa.json=/opt/GISV2/firebase-sa.json
   ```
4. Restart the API so it picks the file up:
   ```bash
   kubectl rollout restart deployment/gis-api -n gisv2
   ```
5. Verify: `kubectl logs -n gisv2 -l app=gis-api | grep -i firebase`
   should log "Firebase initialized from /etc/firebase/sa.json".

If the Secret is missing the pod still starts (the mount is `optional`),
but FCM push notifications are silently disabled — in-app SignalR
notifications continue to work.

### 4. Configure DNS
Create these A records pointing to your VPS IP:
```
belive.calypso.com      → <VPS_IP>
mobile.calypso.com   → <VPS_IP>
api.calypso.com      → <VPS_IP>
```

### 5. Run the deployment
```bash
sudo bash k8s/deploy.sh all
```

This will:
1. ✅ Check system prerequisites
2. ✅ Install K3s (lightweight Kubernetes)
3. ✅ Install cert-manager (auto TLS via Let's Encrypt)
4. ✅ Setup local container registry
5. ✅ Build all Docker images
6. ✅ Deploy all services
7. ✅ Verify deployment

## Updating Services

After pushing code changes to git:
```bash
cd /opt/GISV2
git pull

# Update all services
sudo bash k8s/update.sh all

# Or update specific service only
sudo bash k8s/update.sh api       # .NET API only
sudo bash k8s/update.sh frontend  # Angular frontend only
sudo bash k8s/update.sh ingest    # Rust GPS ingest only
sudo bash k8s/update.sh mobile    # Mobile app only
```

## Operations

### Check status
```bash
sudo bash k8s/update.sh status
```

### View logs
```bash
sudo bash k8s/update.sh logs gis-api
sudo bash k8s/update.sh logs gps-ingest
sudo bash k8s/update.sh logs frontend
```

### Restart a service (zero-downtime)
```bash
kubectl rollout restart deployment/gis-api -n gisv2
```

### Scale a service
```bash
# Scale API to 3 replicas
kubectl scale deployment/gis-api -n gisv2 --replicas=3

# Scale frontend to 1 replica
kubectl scale deployment/frontend -n gisv2 --replicas=1
```

### Access RabbitMQ management UI
```bash
# Port-forward to local machine
kubectl port-forward svc/rabbitmq -n gisv2 15672:15672
# Then open http://localhost:15672
```

### Database backup
```bash
# Create backup
kubectl exec -n gisv2 postgres-0 -- pg_dump -U postgres gis_v2 | gzip > backup_$(date +%Y%m%d).sql.gz

# Restore backup
gunzip -c backup_20260302.sql.gz | kubectl exec -i -n gisv2 postgres-0 -- psql -U postgres gis_v2
```

## Kubernetes vs Docker Compose

| Feature | Docker Compose | K3s (this setup) |
|---------|---------------|-------------------|
| Auto-restart crashed containers | ✅ `restart: unless-stopped` | ✅ Immediate with liveness probes |
| Health checks | Basic | **Advanced** (liveness + readiness) |
| Rolling updates | ❌ Downtime during restart | ✅ Zero-downtime |
| Horizontal scaling | ❌ Manual | ✅ `kubectl scale` |
| Resource limits | Basic | **Granular** per container |
| TLS certificates | Manual (certbot) | **Auto** (cert-manager) |
| Service discovery | Docker DNS | **K8s DNS** |
| Secrets management | `.env` files | **K8s Secrets** (base64) |
| Monitoring integration | Manual | **Built-in** (metrics-server) |

## Troubleshooting

### Pod stuck in CrashLoopBackOff
```bash
kubectl describe pod <pod-name> -n gisv2
kubectl logs <pod-name> -n gisv2 --previous
```

### Valhalla/Nominatim taking too long to start
These services download and process OSM data on first start. This can take 5-30 minutes depending on VPS speed. Check progress:
```bash
kubectl logs -n gisv2 -l app=valhalla -f
kubectl logs -n gisv2 -l app=nominatim -f
```

### GPS trackers can't connect
Verify TCP ports are exposed:
```bash
ss -tlnp | grep -E "6100|6200|6210"
```

### TLS certificate not issued
```bash
kubectl get certificate -n gisv2
kubectl describe certificate gisv2-frontend-tls -n gisv2
kubectl logs -n cert-manager -l app=cert-manager -f
```

## File Structure
```
k8s/
├── 00-namespace-secrets.yaml   # Namespace, Secrets, ConfigMap
├── 01-infrastructure.yaml      # PostgreSQL, Redis, RabbitMQ
├── 02-applications.yaml        # gis-api, gps-ingest, frontend, mobile
├── 03-geo-services.yaml        # Valhalla, Nominatim
├── 04-ingress.yaml             # Traefik Ingress + TLS
├── deploy.sh                   # Full first-time deployment script
├── update.sh                   # Quick update/redeploy script
└── README.md                   # This file
```
