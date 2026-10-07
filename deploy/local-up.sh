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
# Usage:  deploy/local-up.sh [--skip-build] [--forward | -d | --detach]
#   --skip-build  reuse the existing image
#   --forward     keep the API port-forward on localhost:18080 until Ctrl-C
#   -d, --detach  keep the API port-forward in the background after verification
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
K8S_DIR="$REPO_ROOT/deploy/k8s"
NAMESPACE=noxtend
IMAGE=noxtend-api:local
NODE_CONTAINER=desktop-control-plane

SKIP_BUILD=false
FORWARD=false
DETACH=false
for arg in "$@"; do
  case "$arg" in
    --skip-build) SKIP_BUILD=true ;;
    --forward)    FORWARD=true ;;
    -d|--detach)  DETACH=true ;;
    *) printf 'unknown option: %s\nusage: deploy/local-up.sh [--skip-build] [--forward | -d | --detach]\n' "$arg" >&2; exit 2 ;;
  esac
done

log() { printf '\n\033[1m▸ %s\033[0m\n' "$1"; }
die() { printf '\033[31m✗ %s\033[0m\n' "$1" >&2; exit 1; }

# --- preflight -----------------------------------------------------------------

command -v kubectl >/dev/null || die "kubectl not found"
command -v docker  >/dev/null || die "docker not found"

[ -f "$K8S_DIR/secrets.yaml" ] || die \
  "deploy/k8s/secrets.yaml is missing. Copy secrets.example.yaml and set the passwords (see deploy/k8s/README.md)."

# --- 1. image ------------------------------------------------------------------

if [ "$SKIP_BUILD" = false ]; then
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
# Available can still point at the old pod mid-rollout; forward only after the new one
kubectl -n "$NAMESPACE" rollout status deployment/api --timeout=300s

# --- 4. verify -------------------------------------------------------------------

log "Port-forwarding and checking /health"
# kind-backed clusters do not expose NodePort on the host, so forward instead
FORWARD_LOG=$(mktemp "${TMPDIR:-/tmp}/noxtend-port-forward.XXXXXX")
if [ "$DETACH" = true ]; then
  nohup kubectl -n "$NAMESPACE" port-forward svc/api 18080:8080 --address 127.0.0.1 >"$FORWARD_LOG" 2>&1 </dev/null &
else
  kubectl -n "$NAMESPACE" port-forward svc/api 18080:8080 --address 127.0.0.1 >"$FORWARD_LOG" 2>&1 &
fi
FORWARD_PID=$!
trap 'kill "$FORWARD_PID" 2>/dev/null || true' EXIT

HEALTH=""
for _ in $(seq 1 30); do
  if ! kill -0 "$FORWARD_PID" 2>/dev/null; then
    cat "$FORWARD_LOG" >&2
    die "port-forward ended; free port 18080 and retry"
  fi
  # Own listener readiness before probing a possibly occupied port
  case "$(<"$FORWARD_LOG")" in
    *'Forwarding from 127.0.0.1:18080 -> 8080'*)
      if HEALTH=$(curl -fsS --connect-timeout 1 --max-time 3 http://127.0.0.1:18080/health 2>/dev/null); then break; fi
      ;;
  esac
  sleep 2
done

if ! kill -0 "$FORWARD_PID" 2>/dev/null; then
  cat "$FORWARD_LOG" >&2
  die "port-forward ended; free port 18080 and retry"
fi
echo "  $HEALTH"

case "$HEALTH" in
  *'"db":"ok"'*'"redis":"ok"'*'"blob":"ok"'*)
    printf '\n\033[32m✓ Backend API is up — http://localhost:18080/health\033[0m\n'
    printf '  Frontend: run pnpm dev separately, then open http://localhost:5173\n'
    if [ "$DETACH" = true ]; then
      trap - EXIT
      printf '  port-forward running in background — PID %s\n' "$FORWARD_PID"
      printf '  log: %s\n  stop: kill %s\n\n' "$FORWARD_LOG" "$FORWARD_PID"
    elif [ "$FORWARD" = true ]; then
      # Forward is pinned to the current pod; an API restart ends it
      printf '  keeping port-forward on localhost:18080 — Ctrl-C to stop\n\n'
      wait "$FORWARD_PID" || die "port-forward ended (port 18080 busy or API pod restarted); re-run with --forward"
    else
      printf '  port-forward stops when this script exits; keep it with -d or --forward, or re-run:\n'
      printf '    kubectl -n %s port-forward svc/api 18080:8080\n\n' "$NAMESPACE"
    fi
    ;;
  *)
    die "/health did not report every dependency as ok"
    ;;
esac
