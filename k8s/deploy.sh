#!/bin/bash
# =============================================================================
# GIS V2 — K3s Production Deployment Script
# Target: Ubuntu 22.04+ VPS (single node)
# Minimum: 8GB RAM, 4 vCPU, 80GB SSD
# =============================================================================
set -euo pipefail

# Colors for output
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
REGISTRY_PORT=5000
REGISTRY_NAME="registry.local"

# =============================================================================
# Phase 1: System Prerequisites
# =============================================================================
phase1_prerequisites() {
    info "Phase 1: Checking system prerequisites..."

    # Check OS
    if ! grep -qi "ubuntu\|debian" /etc/os-release 2>/dev/null; then
        warn "This script is tested on Ubuntu/Debian. Proceed with caution on other distros."
    fi

    # Check RAM (minimum 8GB)
    TOTAL_RAM_MB=$(free -m | awk '/^Mem:/{print $2}')
    if [ "$TOTAL_RAM_MB" -lt 7000 ]; then
        err "Minimum 8GB RAM required. Found: ${TOTAL_RAM_MB}MB"
    fi
    log "RAM: ${TOTAL_RAM_MB}MB (OK)"

    # Check disk space (minimum 40GB free)
    FREE_DISK_GB=$(df -BG / | awk 'NR==2{print $4}' | tr -d 'G')
    if [ "$FREE_DISK_GB" -lt 40 ]; then
        err "Minimum 40GB free disk required. Found: ${FREE_DISK_GB}GB"
    fi
    log "Disk: ${FREE_DISK_GB}GB free (OK)"

    # Install basic tools
    apt-get update -qq
    apt-get install -y -qq curl wget git jq > /dev/null 2>&1
    log "Base tools installed"
}

# =============================================================================
# Phase 2: Install K3s
# =============================================================================
phase2_install_k3s() {
    info "Phase 2: Installing K3s..."

    if command -v k3s &> /dev/null; then
        log "K3s already installed: $(k3s --version)"
    else
        # Install K3s with Traefik (default ingress controller)
        curl -sfL https://get.k3s.io | sh -s - \
            --write-kubeconfig-mode 644 \
            --disable servicelb \
            --kube-apiserver-arg="--default-not-ready-toleration-seconds=30" \
            --kube-apiserver-arg="--default-unreachable-toleration-seconds=30"

        # Wait for K3s to be ready
        sleep 10
        log "K3s installed: $(k3s --version)"
    fi

    # Setup kubectl alias
    if ! command -v kubectl &> /dev/null; then
        ln -sf /usr/local/bin/k3s /usr/local/bin/kubectl 2>/dev/null || true
    fi

    # Export kubeconfig for current session
    export KUBECONFIG=/etc/rancher/k3s/k3s.yaml

    # Wait for node to be ready
    info "Waiting for K3s node to be ready..."
    kubectl wait --for=condition=Ready node --all --timeout=120s
    log "K3s node ready"
}

# =============================================================================
# Phase 3: Install cert-manager (for Let's Encrypt TLS)
# =============================================================================
phase3_cert_manager() {
    info "Phase 3: Installing cert-manager..."

    if kubectl get namespace cert-manager &> /dev/null; then
        log "cert-manager already installed"
    else
        kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/v1.14.4/cert-manager.yaml
        
        # Wait for cert-manager to be ready
        info "Waiting for cert-manager pods..."
        kubectl wait --namespace cert-manager \
            --for=condition=Ready pod --all \
            --timeout=120s
        log "cert-manager installed"
    fi
}

# =============================================================================
# Phase 4: Setup local Docker registry (for custom images)
# =============================================================================
phase4_registry() {
    info "Phase 4: Setting up local container registry..."

    # Check if registry is already running in K3s
    if kubectl get deployment registry -n kube-system &> /dev/null; then
        log "Local registry already running"
        return
    fi

    # Deploy a local registry as a K3s pod
    cat <<EOF | kubectl apply -f -
apiVersion: apps/v1
kind: Deployment
metadata:
  name: registry
  namespace: kube-system
spec:
  replicas: 1
  selector:
    matchLabels:
      app: registry
  template:
    metadata:
      labels:
        app: registry
    spec:
      containers:
        - name: registry
          image: registry:2
          ports:
            - containerPort: 5000
              hostPort: ${REGISTRY_PORT}
          volumeMounts:
            - name: registry-data
              mountPath: /var/lib/registry
          env:
            - name: REGISTRY_STORAGE_DELETE_ENABLED
              value: "true"
      volumes:
        - name: registry-data
          hostPath:
            path: /opt/registry
            type: DirectoryOrCreate
---
apiVersion: v1
kind: Service
metadata:
  name: registry
  namespace: kube-system
spec:
  ports:
    - port: 5000
      targetPort: 5000
  selector:
    app: registry
EOF

    # Configure K3s to trust the local registry
    mkdir -p /etc/rancher/k3s
    cat > /etc/rancher/k3s/registries.yaml <<EOF
mirrors:
  "${REGISTRY_NAME}:${REGISTRY_PORT}":
    endpoint:
      - "http://localhost:${REGISTRY_PORT}"
  "registry.local:5000":
    endpoint:
      - "http://localhost:${REGISTRY_PORT}"
EOF

    # Add to /etc/hosts if not already there
    if ! grep -q "${REGISTRY_NAME}" /etc/hosts; then
        echo "127.0.0.1 ${REGISTRY_NAME}" >> /etc/hosts
    fi

    # Restart K3s to pick up registry config
    systemctl restart k3s
    sleep 15

    kubectl wait --for=condition=Ready node --all --timeout=120s
    log "Local registry configured at ${REGISTRY_NAME}:${REGISTRY_PORT}"
}

# =============================================================================
# Phase 5: Build and push Docker images
# =============================================================================
phase5_build_images() {
    info "Phase 5: Building and pushing Docker images..."

    # We need Docker or nerdctl to build images
    if command -v docker &> /dev/null; then
        BUILD_CMD="docker"
    elif command -v nerdctl &> /dev/null; then
        BUILD_CMD="nerdctl"
    else
        info "Installing Docker for building images..."
        curl -fsSL https://get.docker.com | sh
        BUILD_CMD="docker"
    fi

    REGISTRY="localhost:${REGISTRY_PORT}"

    # Build gis-api
    info "Building gis-api..."
    $BUILD_CMD build \
        -t ${REGISTRY}/gisv2/gis-api:latest \
        -f "${PROJECT_ROOT}/services/GisAPI/Dockerfile" \
        "${PROJECT_ROOT}/services"
    $BUILD_CMD push ${REGISTRY}/gisv2/gis-api:latest
    log "gis-api built and pushed"

    # Build gps-ingest
    info "Building gps-ingest..."
    $BUILD_CMD build \
        -t ${REGISTRY}/gisv2/gps-ingest:latest \
        -f "${PROJECT_ROOT}/services/gps-ingest-rust/Dockerfile" \
        "${PROJECT_ROOT}/services/gps-ingest-rust"
    $BUILD_CMD push ${REGISTRY}/gisv2/gps-ingest:latest
    log "gps-ingest built and pushed"

    # Build frontend
    info "Building frontend..."
    $BUILD_CMD build \
        -t ${REGISTRY}/gisv2/frontend:latest \
        -f "${PROJECT_ROOT}/services/gis-frontend/Dockerfile.prod" \
        "${PROJECT_ROOT}/services/gis-frontend"
    $BUILD_CMD push ${REGISTRY}/gisv2/frontend:latest
    log "frontend built and pushed"

    # Build mobile
    info "Building mobile..."
    $BUILD_CMD build \
        -t ${REGISTRY}/gisv2/mobile:latest \
        -f "${PROJECT_ROOT}/services/gis-mobile/Dockerfile.prod" \
        "${PROJECT_ROOT}/services/gis-mobile"
    $BUILD_CMD push ${REGISTRY}/gisv2/mobile:latest
    log "mobile built and pushed"

    log "All images built and pushed to local registry"
}

# =============================================================================
# Phase 6: Deploy K8s manifests
# =============================================================================
phase6_deploy() {
    info "Phase 6: Deploying GIS V2 to K3s..."

    export KUBECONFIG=/etc/rancher/k3s/k3s.yaml

    # Apply in order (dependencies first)
    info "Creating namespace and secrets..."
    kubectl apply -f "${SCRIPT_DIR}/00-namespace-secrets.yaml"

    info "Deploying infrastructure (postgres, redis, rabbitmq)..."
    kubectl apply -f "${SCRIPT_DIR}/01-infrastructure.yaml"

    # Wait for infrastructure to be ready
    info "Waiting for infrastructure pods to be ready (this may take 1-2 minutes)..."
    kubectl wait --namespace gisv2 --for=condition=Ready pod -l app=postgres --timeout=180s
    kubectl wait --namespace gisv2 --for=condition=Ready pod -l app=redis --timeout=120s
    kubectl wait --namespace gisv2 --for=condition=Ready pod -l app=rabbitmq --timeout=180s
    log "Infrastructure ready"

    info "Deploying applications (api, ingest, frontend, mobile)..."
    kubectl apply -f "${SCRIPT_DIR}/02-applications.yaml"

    info "Deploying geo-services (valhalla, nominatim)..."
    kubectl apply -f "${SCRIPT_DIR}/03-geo-services.yaml"

    info "Configuring ingress (Traefik + TLS)..."
    kubectl apply -f "${SCRIPT_DIR}/04-ingress.yaml"

    # Wait for application pods
    info "Waiting for application pods (may take 2-5 minutes for first start)..."
    kubectl wait --namespace gisv2 --for=condition=Ready pod -l app=gis-api --timeout=300s
    kubectl wait --namespace gisv2 --for=condition=Ready pod -l app=gps-ingest --timeout=300s
    kubectl wait --namespace gisv2 --for=condition=Ready pod -l app=frontend --timeout=120s

    log "All core services deployed"
    warn "Valhalla and Nominatim may take 5-15 minutes for initial data import"
}

# =============================================================================
# Phase 7: Post-deploy verification
# =============================================================================
phase7_verify() {
    info "Phase 7: Verifying deployment..."

    export KUBECONFIG=/etc/rancher/k3s/k3s.yaml

    echo ""
    echo "========================================="
    echo "  Pod Status"
    echo "========================================="
    kubectl get pods -n gisv2 -o wide

    echo ""
    echo "========================================="
    echo "  Services"
    echo "========================================="
    kubectl get svc -n gisv2

    echo ""
    echo "========================================="
    echo "  Ingress"
    echo "========================================="
    kubectl get ingress -n gisv2

    echo ""
    echo "========================================="
    echo "  PersistentVolumeClaims"
    echo "========================================="
    kubectl get pvc -n gisv2

    echo ""
    log "Deployment complete!"
    echo ""
    info "=== NEXT STEPS ==="
    echo "  1. Update DNS records to point to this VPS IP"
    echo "  2. Edit k8s/00-namespace-secrets.yaml with real production passwords"
    echo "  3. Edit k8s/04-ingress.yaml with your actual domain names"
    echo "  4. Re-apply: kubectl apply -f ${SCRIPT_DIR}/"
    echo ""
    info "=== USEFUL COMMANDS ==="
    echo "  kubectl get pods -n gisv2                  # Pod status"
    echo "  kubectl logs -n gisv2 -l app=gis-api -f    # API logs"
    echo "  kubectl logs -n gisv2 -l app=gps-ingest -f # Ingest logs"
    echo "  kubectl top pods -n gisv2                   # Resource usage"
    echo "  kubectl rollout restart deploy/gis-api -n gisv2  # Restart API"
    echo ""
    info "=== GPS TRACKER PORTS ==="
    echo "  TCP 6100 → GPS Type 1"
    echo "  TCP 6200 → GPS Type 2"
    echo "  TCP 6210 → GPS Type 2.1"
    echo "  These are exposed via hostPort on the gps-ingest pod"
}

# =============================================================================
# Main
# =============================================================================
main() {
    echo "==========================================="
    echo "  GIS V2 — K3s Production Deployment"
    echo "==========================================="
    echo ""

    # Must be root
    if [ "$(id -u)" -ne 0 ]; then
        err "This script must be run as root. Use: sudo bash deploy.sh"
    fi

    # Parse command
    CMD="${1:-all}"

    case "$CMD" in
        prereq)     phase1_prerequisites ;;
        k3s)        phase2_install_k3s ;;
        cert)       phase3_cert_manager ;;
        registry)   phase4_registry ;;
        build)      phase5_build_images ;;
        deploy)     phase6_deploy ;;
        verify)     phase7_verify ;;
        all)
            phase1_prerequisites
            phase2_install_k3s
            phase3_cert_manager
            phase4_registry
            phase5_build_images
            phase6_deploy
            phase7_verify
            ;;
        *)
            echo "Usage: sudo bash deploy.sh [prereq|k3s|cert|registry|build|deploy|verify|all]"
            exit 1
            ;;
    esac
}

main "$@"
