#!/usr/bin/env bash
# One-command internal-test deploy to Azure.
#
#   SQL_ADMIN_PASSWORD='...' ./deploy/azure/deploy.sh
#
# Overridable env: RG, LOCATION, PREFIX, IMAGE_TAG, SKIP_FRONTEND=1
# Teardown is one line:  az group delete -n "$RG" --yes --no-wait
#
# The API has no authentication, so the site is locked to this machine's public IP.
# Re-run after your IP changes, or the deployed API stops answering you.
set -euo pipefail

RG=${RG:-noxtend-test-rg}
LOCATION=${LOCATION:-koreacentral}
PREFIX=${PREFIX:-noxtend}
IMAGE_TAG=${IMAGE_TAG:-latest}
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)

: "${SQL_ADMIN_PASSWORD:?set SQL_ADMIN_PASSWORD (12+ chars: upper, lower, digit, symbol)}"

MY_IP=$(curl -fsS https://api.ipify.org)
echo "==> operator IP ${MY_IP} — the only address allowed to reach the API"

az group create -n "$RG" -l "$LOCATION" -o none

echo "==> provisioning (Redis takes ~10-15 min on a first run)"
az deployment group create \
  -g "$RG" -n noxtend -f "$ROOT/deploy/azure/main.bicep" \
  -p namePrefix="$PREFIX" imageTag="$IMAGE_TAG" allowedIpAddress="$MY_IP" \
     sqlAdminPassword="$SQL_ADMIN_PASSWORD" \
  -o none

out() { az deployment group show -g "$RG" -n noxtend --query "properties.outputs.$1.value" -o tsv; }
ACR=$(out acrName)
API=$(out apiName)
API_URL=$(out apiUrl)
SWA=$(out staticWebAppName)
SWA_URL=$(out staticWebAppUrl)

# Cloud build — the registry builds the image, so no local Docker daemon is needed.
echo "==> building noxtend-api:${IMAGE_TAG} in ${ACR}"
az acr build -r "$ACR" -t "noxtend-api:${IMAGE_TAG}" "$ROOT/apps/backend" -o none

# The first deploy references an image that does not exist yet; restart picks it up.
az webapp restart -g "$RG" -n "$API" -o none

if [[ "${SKIP_FRONTEND:-0}" != "1" ]]; then
  echo "==> building frontend against ${API_URL}"
  VITE_API_BASE_URL="$API_URL" pnpm --dir "$ROOT" build
  TOKEN=$(az staticwebapp secrets list -n "$SWA" -g "$RG" --query properties.apiKey -o tsv)
  npx -y @azure/static-web-apps-cli deploy "$ROOT/apps/frontend/dist" \
    --deployment-token "$TOKEN" --env production
fi

echo
echo "API : ${API_URL}/health"
echo "WEB : ${SWA_URL}"
echo "logs: az webapp log tail -g ${RG} -n ${API}"
