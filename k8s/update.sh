#!/bin/bash
# =============================================================================
# GIS V2 — Quick Update Script
# Rebuilds and redeploys specific services without full cluster setup
# Usage: sudo bash update.sh [all|api|ingest|frontend|mobile]
# =============================================================================
set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log()  { echo -e "${GREEN}[✓]${NC} $1"; }
warn() { echo -e "${YELLOW}[!]${NC} $1"; }
err()  { echo -e "${RED}[✗]${NC} $1"; exit 1; }
info() { echo -e "${BLUE}[i]${NC} $1"; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
REGISTRY="localhost:5000"
TIMESTAMP=$(date +%Y%m%d-%H%M%S)

export KUBECONFIG=/etc/rancher/k3s/k3s.yaml

build_and_deploy() {
    local service="$1"
    local dockerfile="$2"
    local context="$3"
    local k8s_name="$4"

    info "Building ${service}..."
    docker build -t ${REGISTRY}/gisv2/${service}:latest \
                 -t ${REGISTRY}/gisv2/${service}:${TIMESTAMP} \
                 -f "${dockerfile}" "${context}"
    docker push ${REGISTRY}/gisv2/${service}:latest
    docker push ${REGISTRY}/gisv2/${service}:${TIMESTAMP}
    log "${service} image built and pushed"

    info "Rolling restart ${k8s_name}..."
    kubectl rollout restart deployment/${k8s_name} -n gisv2
    kubectl rollout status deployment/${k8s_name} -n gisv2 --timeout=300s
    log "${k8s_name} deployed (zero-downtime rolling update)"

    cleanup_build_residue
}

cleanup_build_residue() {
    # Chaque build laisse derrière lui l'image précédente (dé-taguée ou avec un
    # tag horodaté jamais réutilisé) + du cache BuildKit. Sans nettoyage, le
    # disque de la VM se remplit (constaté : ~63 Go de résidus, disque à 83% →
    # GC kubelet + lenteurs générales). On garde 7 jours d'images (rollback
    # possible via les tags horodatés récents) et ~8 Go de cache de build
    # (les couches de base restent chaudes → rebuilds rapides).
    info "Cleaning build residue (old images + build cache)..."
    docker image prune -af --filter "until=168h" >/dev/null 2>&1 || true
    docker builder prune -f --keep-storage 8GB >/dev/null 2>&1 || true
    # Côté containerd (K3s) : retire les anciennes versions importées qui ne
    # sont plus utilisées par aucun pod.
    k3s crictl rmi --prune >/dev/null 2>&1 || true
    log "Build residue cleaned (images >7j + cache >8GB + containerd orphelins)"
}

CMD="${1:-all}"

case "$CMD" in
    api)
        build_and_deploy "gis-api" \
            "${PROJECT_ROOT}/services/GisAPI/Dockerfile" \
            "${PROJECT_ROOT}/services" \
            "gis-api"
        ;;
    ingest)
        build_and_deploy "gps-ingest" \
            "${PROJECT_ROOT}/services/gps-ingest-rust/Dockerfile" \
            "${PROJECT_ROOT}/services/gps-ingest-rust" \
            "gps-ingest"
        ;;
    frontend)
        build_and_deploy "frontend" \
            "${PROJECT_ROOT}/services/gis-frontend/Dockerfile.prod" \
            "${PROJECT_ROOT}/services/gis-frontend" \
            "frontend"
        ;;
    mobile)
        build_and_deploy "mobile" \
            "${PROJECT_ROOT}/services/gis-mobile/Dockerfile.prod" \
            "${PROJECT_ROOT}/services/gis-mobile" \
            "mobile"
        ;;
    all)
        info "Pulling latest code..."
        cd "${PROJECT_ROOT}" && git pull

        build_and_deploy "gis-api" \
            "${PROJECT_ROOT}/services/GisAPI/Dockerfile" \
            "${PROJECT_ROOT}/services" \
            "gis-api"
        build_and_deploy "gps-ingest" \
            "${PROJECT_ROOT}/services/gps-ingest-rust/Dockerfile" \
            "${PROJECT_ROOT}/services/gps-ingest-rust" \
            "gps-ingest"
        build_and_deploy "frontend" \
            "${PROJECT_ROOT}/services/gis-frontend/Dockerfile.prod" \
            "${PROJECT_ROOT}/services/gis-frontend" \
            "frontend"
        build_and_deploy "mobile" \
            "${PROJECT_ROOT}/services/gis-mobile/Dockerfile.prod" \
            "${PROJECT_ROOT}/services/gis-mobile" \
            "mobile"

        log "All services updated"
        ;;
    config)
        info "Re-applying K8s configuration (no rebuild)..."
        kubectl apply -f "${SCRIPT_DIR}/00-namespace-secrets.yaml"
        kubectl apply -f "${SCRIPT_DIR}/01-infrastructure.yaml"
        kubectl apply -f "${SCRIPT_DIR}/02-applications.yaml"
        kubectl apply -f "${SCRIPT_DIR}/03-geo-services.yaml"
        kubectl apply -f "${SCRIPT_DIR}/04-ingress.yaml"
        log "Configuration re-applied"
        ;;
    status)
        echo ""
        echo "===== Pods ====="
        kubectl get pods -n gisv2 -o wide
        echo ""
        echo "===== Resource Usage ====="
        kubectl top pods -n gisv2 2>/dev/null || warn "Metrics server not available"
        echo ""
        echo "===== Recent Events ====="
        kubectl get events -n gisv2 --sort-by=.lastTimestamp | tail -15
        ;;
    logs)
        SERVICE="${2:-gis-api}"
        kubectl logs -n gisv2 -l app=${SERVICE} --tail=100 -f
        ;;
    *)
        echo "Usage: sudo bash update.sh [all|api|ingest|frontend|mobile|config|status|logs]"
        echo ""
        echo "Commands:"
        echo "  all       - Rebuild and deploy all custom services"
        echo "  api       - Rebuild and deploy gis-api only"
        echo "  ingest    - Rebuild and deploy gps-ingest only"
        echo "  frontend  - Rebuild and deploy frontend only"
        echo "  mobile    - Rebuild and deploy mobile only"
        echo "  config    - Re-apply K8s manifests (no rebuild)"
        echo "  status    - Show cluster status"
        echo "  logs      - Tail logs (usage: update.sh logs gis-api)"
        exit 1
        ;;
esac
