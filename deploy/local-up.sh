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
IMAGE_ALIAS=noxtend-api:local
METADATA="$REPO_ROOT/deploy/local-deployment.py"
RECORD="$REPO_ROOT/deploy/.local/deployments/latest.json"
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

for tool in kubectl docker curl lsof ps id sleep seq mktemp cat nohup python3; do
  command -v "$tool" >/dev/null || die "$tool not found"
done

listeners() {
  local result status=0
  result=$(lsof -nP -iTCP:18080 -sTCP:LISTEN -t 2>&1) || status=$?
  if [ "$status" -ne 0 ] && { [ "$status" -ne 1 ] || [ -n "$result" ]; }; then
    die "cannot inspect port 18080: $result"
  fi
  printf '%s' "$result"
}

CONTEXT=$(kubectl config current-context) || die "cannot determine Kubernetes context"
[ -n "$CONTEXT" ] || die "cannot determine Kubernetes context"
LISTENERS=$(listeners)
if [ -n "$LISTENERS" ]; then
  OWNER=$(id -u)
  KUBECTL=$(command -v kubectl)
  # All-listener validation before TERM
  for pid in $LISTENERS; do
    [[ "$pid" =~ ^[0-9]+$ ]] || die "cannot identify listener on port 18080"
    PROCESS=$(ps -ww -p "$pid" -o uid= -o command=) || die "cannot identify listener PID $pid on port 18080"
    read -r uid executable arguments <<< "$PROCESS"
    [ "$uid" = "$OWNER" ] && { [ "$executable" = "$KUBECTL" ] || [ "$executable" = kubectl ]; } || \
      die "port 18080 is occupied by an unverified process (PID $pid); stop it manually"
    case "$arguments" in
      "-n $NAMESPACE port-forward svc/api 18080:8080"|\
      "-n $NAMESPACE port-forward svc/api 18080:8080 --address 127.0.0.1"|\
      "--context $CONTEXT -n $NAMESPACE port-forward svc/api 18080:8080"|\
      "--context $CONTEXT -n $NAMESPACE port-forward svc/api 18080:8080 --address 127.0.0.1") ;;
      *) die "port 18080 is occupied by an unverified process (PID $pid); stop it manually" ;;
    esac
  done
fi

image_metadata() {
  local metadata extra
  metadata=$(docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }} {{ index .Config.Labels "io.noxtend.source.dirty" }} {{ .Id }}' "$IMAGE_ALIAS") || \
    die "cannot inspect $IMAGE_ALIAS; rebuild with deploy/local-up.sh without --skip-build"
  read -r REVISION DIRTY IMAGE_ID extra <<< "$metadata"
  [[ "$REVISION" =~ ^([a-f0-9]{40}|[a-f0-9]{64})$ ]] && \
    [[ "$DIRTY" = true || "$DIRTY" = false ]] && \
    [[ "$IMAGE_ID" =~ ^sha256:[a-f0-9]{64}$ ]] && [ -z "$extra" ] || \
    die "image provenance is missing or invalid; rebuild with deploy/local-up.sh without --skip-build"
}

# Existing image provenance before terminating a working forward
if [ "$SKIP_BUILD" = true ]; then
  image_metadata
else
  command -v git >/dev/null || die "git not found"
  REVISION=$(git -C "$REPO_ROOT" rev-parse HEAD) || die "cannot determine source revision"
  STATUS=$(git -C "$REPO_ROOT" status --porcelain --untracked-files=normal) || die "cannot determine source dirty state"
  DIRTY=false
  [ -z "$STATUS" ] || DIRTY=true
fi

if [ -n "$LISTENERS" ]; then
  log "Stopping existing Noxtend port-forward on port 18080"
  for pid in $LISTENERS; do
    kill -TERM "$pid" || die "cannot stop port-forward PID $pid"
  done
  for ((attempt=0; attempt<25; attempt++)); do
    REMAINING=$(listeners) || die "cannot verify port 18080 release"
    [ -n "$REMAINING" ] || break
    sleep 0.2
  done
  REMAINING=$(listeners) || die "cannot verify port 18080 release"
  [ -z "$REMAINING" ] || die "port 18080 was not released after TERM; stop it manually"
fi

[ -f "$K8S_DIR/secrets.yaml" ] || die \
  "deploy/k8s/secrets.yaml is missing. Copy secrets.example.yaml and set the passwords (see deploy/k8s/README.md)."

# --- 1. image ------------------------------------------------------------------

if [ "$SKIP_BUILD" = false ]; then
  log "Building $IMAGE_ALIAS ($REVISION, dirty=$DIRTY)"
  docker build --label "org.opencontainers.image.revision=$REVISION" \
    --label "io.noxtend.source.dirty=$DIRTY" -t "$IMAGE_ALIAS" "$REPO_ROOT/apps/backend"
  image_metadata
fi

# Source SHA plus full config digest, including dirty builds
IMAGE="noxtend-api:${REVISION:0:40}-$(if [ "$DIRTY" = true ]; then echo dirty; else echo clean; fi)-${IMAGE_ID#sha256:}"
docker tag "$IMAGE_ID" "$IMAGE"
log "Using $IMAGE (dirty=$DIRTY)"

log "Importing image into the cluster's containerd namespace"
# Without this the kubelet cannot see the image, whatever `docker images` says
if docker inspect "$NODE_CONTAINER" >/dev/null 2>&1; then
  docker save "$IMAGE" | docker exec -i "$NODE_CONTAINER" ctr -n k8s.io images import -
else
  echo "  node container '$NODE_CONTAINER' not found — assuming the cluster shares the docker image store"
fi

# --- 2. manifests ---------------------------------------------------------------

log "Applying manifests"
# Namespace and secret first; the other four have no ordering constraint because the
# API reports unmet dependencies through /health rather than failing to start
kubectl --context "$CONTEXT" apply -f "$K8S_DIR/namespace.yaml"
kubectl --context "$CONTEXT" apply -f "$K8S_DIR/secrets.yaml"
kubectl --context "$CONTEXT" apply -f "$K8S_DIR/mssql.yaml" \
               -f "$K8S_DIR/redis.yaml" \
               -f "$K8S_DIR/azurite.yaml"
python3 "$METADATA" render "$K8S_DIR/api.yaml" "$IMAGE" | kubectl --context "$CONTEXT" apply -f -

# A rollout restart is needed when the image changed but the manifest did not
kubectl --context "$CONTEXT" -n "$NAMESPACE" rollout restart deployment/api >/dev/null

# --- 3. wait --------------------------------------------------------------------

log "Waiting for dependencies (mssql needs 30s-2m on first boot)"
kubectl --context "$CONTEXT" -n "$NAMESPACE" wait --for=condition=available --timeout=300s \
  deployment/mssql deployment/redis deployment/azurite deployment/api
# Available can still point at the old pod mid-rollout; forward only after the new one
kubectl --context "$CONTEXT" -n "$NAMESPACE" rollout status deployment/api --timeout=300s
POD_METADATA=$(python3 "$METADATA" verify "$CONTEXT" "$NAMESPACE" "$IMAGE") || die "cannot verify the applied api image"

# --- 4. verify -------------------------------------------------------------------

log "Port-forwarding and checking /health"
# kind-backed clusters do not expose NodePort on the host, so forward instead
FORWARD_LOG=$(mktemp "${TMPDIR:-/tmp}/noxtend-port-forward.XXXXXX")
if [ "$DETACH" = true ]; then
  nohup kubectl --context "$CONTEXT" -n "$NAMESPACE" port-forward svc/api 18080:8080 --address 127.0.0.1 >"$FORWARD_LOG" 2>&1 </dev/null &
else
  kubectl --context "$CONTEXT" -n "$NAMESPACE" port-forward svc/api 18080:8080 --address 127.0.0.1 >"$FORWARD_LOG" 2>&1 &
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

case "$HEALTH" in
  *'"db":"ok"'*'"redis":"ok"'*'"blob":"ok"'*)
    python3 "$METADATA" record "$RECORD" "$CONTEXT" "$NAMESPACE" "$REVISION" "$DIRTY" "$IMAGE" "$IMAGE_ID" "$POD_METADATA" || \
      die "cannot record the verified Backend deployment"
    printf '  latest successful Backend deployment: %s\n' "$RECORD"
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
