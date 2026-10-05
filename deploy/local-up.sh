#!/usr/bin/env bash
#
# Local stack bring-up in one command.
#
# Plan §4.1 asks for the whole stack to come up from a single step. `kubectl apply`
# alone does not get there: recent Docker Desktop runs Kubernetes as a kind cluster, so
# the node is a separate container with its own containerd image store, and an image
# built by `docker build` is invisible to the kubelet (ErrImageNeverPull). This script
# is the missing step made repeatable rather than a line in a README nobody re-reads.
#
# Usage:  deploy/local-up.sh [--skip-build]
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
K8S_DIR="$REPO_ROOT/deploy/k8s"
NAMESPACE=noxtend
IMAGE=noxtend-api:local
NODE_CONTAINER=desktop-control-plane

log() { printf '\n\033[1m▸ %s\033[0m\n' "$1"; }
die() { printf '\033[31m✗ %s\033[0m\n' "$1" >&2; exit 1; }

# --- preflight -----------------------------------------------------------------

command -v kubectl >/dev/null || die "kubectl not found"
command -v docker  >/dev/null || die "docker not found"

[ -f "$K8S_DIR/secrets.yaml" ] || die \
  "deploy/k8s/secrets.yaml is missing. Copy secrets.example.yaml and set the passwords (see deploy/k8s/README.md)."

# --- 1. image ------------------------------------------------------------------

if [ "${1:-}" != "--skip-build" ]; then
  log "Building $IMAGE"
  docker build -t "$IMAGE" "$REPO_ROOT/apps/backend"

  log "Importing image into the cluster's containerd namespace"
  # Without this the kubelet cannot see the image, whatever `docker images` says
  if docker inspect "$NODE_CONTAINER" >/dev/null 2>&1; then
    docker save "$IMAGE" | docker exec -i "$NODE_CONTAINER" ctr -n k8s.io images import -
  else
    echo "  node container '$NODE_CONTAINER' not found — assuming the cluster shares the docker image store"
  fi
fi

# --- 2. manifests ---------------------------------------------------------------

log "Applying manifests"
# Namespace and secret first; the other four have no ordering constraint because the
# API reports unmet dependencies through /health rather than failing to start
kubectl apply -f "$K8S_DIR/namespace.yaml"
kubectl apply -f "$K8S_DIR/secrets.yaml"
kubectl apply -f "$K8S_DIR/mssql.yaml" \
               -f "$K8S_DIR/redis.yaml" \
               -f "$K8S_DIR/azurite.yaml" \
               -f "$K8S_DIR/api.yaml"

# A rollout restart is needed when the image changed but the manifest did not
kubectl -n "$NAMESPACE" rollout restart deployment/api >/dev/null

# --- 3. wait --------------------------------------------------------------------

log "Waiting for dependencies (mssql needs 30s-2m on first boot)"
kubectl -n "$NAMESPACE" wait --for=condition=available --timeout=300s \
  deployment/mssql deployment/redis deployment/azurite deployment/api

# --- 4. verify -------------------------------------------------------------------

log "Port-forwarding and checking /health"
# kind-backed clusters do not expose NodePort on the host, so forward instead
kubectl -n "$NAMESPACE" port-forward svc/api 18080:8080 >/dev/null 2>&1 &
FORWARD_PID=$!
trap 'kill $FORWARD_PID 2>/dev/null || true' EXIT

for _ in $(seq 1 30); do
  if curl -sf http://localhost:18080/health >/dev/null 2>&1; then break; fi
  sleep 2
done

HEALTH=$(curl -s http://localhost:18080/health || true)
echo "  $HEALTH"

case "$HEALTH" in
  *'"db":"ok"'*'"redis":"ok"'*'"blob":"ok"'*)
    printf '\n\033[32m✓ stack is up — http://localhost:18080\033[0m\n'
    printf '  port-forward stops when this script exits; re-run:\n'
    printf '    kubectl -n %s port-forward svc/api 18080:8080\n\n' "$NAMESPACE"
    ;;
  *)
    die "/health did not report every dependency as ok"
    ;;
esac
