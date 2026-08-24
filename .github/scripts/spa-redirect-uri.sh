#!/usr/bin/env bash
#
# Add or remove a SWA preview environment's redirect URIs on the SPA app registration.
#
# MSAL redeems its authorization code cross-origin, and Entra only permits that for URIs
# registered under an application's "spa" platform — registering under "web" yields
# AADSTS9002326 at the token endpoint even though /authorize succeeds. There is no az CLI
# command for the spa platform, hence the raw Graph PATCH.
#
# Usage:
#   spa-redirect-uri.sh add    <pr-number|base-url>
#   spa-redirect-uri.sh remove <pr-number>
#
# "add" takes either a PR number (the preview hostname is derived, so this can run before
# the deploy exists) or the exact base URL the deploy reported. "remove" takes a PR number
# and strips every registered URI belonging to that PR's preview host, so it does not
# depend on the environment still existing or on the derivation matching.
#
# Required env: CIAM_TENANT_ID, CIAM_CLIENT_ID, CIAM_CLIENT_SECRET, SPA_OBJECT_ID
# Optional env: SWA_NAME (default swa-slypn-prod), SWA_RG (default rg-slypn-prod)

set -euo pipefail

MODE="${1:-}"
TARGET="${2:-}"
if [[ -z "$MODE" || -z "$TARGET" ]]; then
  echo "usage: $(basename "$0") <add|remove> <pr-number|base-url>" >&2
  exit 2
fi

: "${CIAM_TENANT_ID:?CIAM_TENANT_ID is required}"
: "${CIAM_CLIENT_ID:?CIAM_CLIENT_ID is required}"
: "${CIAM_CLIENT_SECRET:?CIAM_CLIENT_SECRET is required}"
: "${SPA_OBJECT_ID:?SPA_OBJECT_ID is required}"
SWA_NAME="${SWA_NAME:-swa-slypn-prod}"
SWA_RG="${SWA_RG:-rg-slypn-prod}"

# Derive a preview base URL from the production hostname. SWA names previews
# <prefix>-<env>.<region>.<suffix> where production is <prefix>.<suffix>, e.g.
# example-abc123.7.azurestaticapps.net -> example-abc123-42.westeurope.7.azurestaticapps.net
derive_preview_url() {
  local pr="$1" host region
  host=$(az staticwebapp show -n "$SWA_NAME" -g "$SWA_RG" --query defaultHostname -o tsv)
  region=$(az staticwebapp show -n "$SWA_NAME" -g "$SWA_RG" --query location -o tsv \
    | tr '[:upper:]' '[:lower:]' | tr -d ' ')
  echo "https://${host%%.*}-${pr}.${region}.${host#*.}"
}

graph_token() {
  curl -sf "https://login.microsoftonline.com/${CIAM_TENANT_ID}/oauth2/v2.0/token" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=${CIAM_CLIENT_ID}" \
    --data-urlencode "client_secret=${CIAM_CLIENT_SECRET}" \
    --data-urlencode "scope=https://graph.microsoft.com/.default" \
    | jq -r .access_token
}

TOKEN=$(graph_token)
CURRENT=$(curl -sf "https://graph.microsoft.com/v1.0/applications/${SPA_OBJECT_ID}" \
  -H "Authorization: Bearer $TOKEN" | jq -c '.spa.redirectUris // []')

case "$MODE" in
  add)
    if [[ "$TARGET" =~ ^[0-9]+$ ]]; then
      BASE_URL=$(derive_preview_url "$TARGET")
      echo "Derived preview URL for PR #${TARGET}: $BASE_URL"
    else
      BASE_URL="$TARGET"
    fi
    CALLBACK="${BASE_URL%/}/auth/callback"
    OAUTH_REDIRECT="${BASE_URL%/}/oauth2-redirect.html"
    UPDATED=$(jq -cn --argjson c "$CURRENT" --arg a "$CALLBACK" --arg b "$OAUTH_REDIRECT" \
      '$c + [$a, $b] | unique')
    DESCRIPTION="$CALLBACK  $OAUTH_REDIRECT"
    ;;
  remove)
    if [[ ! "$TARGET" =~ ^[0-9]+$ ]]; then
      echo "::error::remove expects a PR number, got '$TARGET'" >&2
      exit 2
    fi
    # Match on the PR's preview host rather than an exact URL, so cleanup does not
    # depend on the environment still existing or on the derivation above.
    #
    # String operations, not a regex: the obvious pattern ("-<pr>\\.") is one lost
    # backslash away from an unescaped dot, which would silently also match PR 1640
    # when closing PR 164 and break that PR's sign-in. endswith cannot do that —
    # "…-1640" does not end with "-164".
    #
    # Host is split("/")[2]; production is <prefix>.<suffix> with no "-<pr>" label
    # suffix, and localhost has no azurestaticapps.net host, so neither can match.
    UPDATED=$(jq -cn --argjson c "$CURRENT" --arg pr "$TARGET" '
      [ $c[]
        | select(
            ((split("/") | .[2]) // "") as $host
            | ((($host | split(".") | .[0]) // "") | endswith("-" + $pr))
              and ($host | endswith("azurestaticapps.net"))
            | not
          )
      ]')
    DESCRIPTION="every redirect URI for PR #${TARGET}"
    ;;
  *)
    echo "::error::unknown mode '$MODE' (expected add or remove)" >&2
    exit 2
    ;;
esac

if [[ "$UPDATED" == "$CURRENT" ]]; then
  echo "No change needed ($MODE): $DESCRIPTION"
  exit 0
fi

curl -sf -X PATCH "https://graph.microsoft.com/v1.0/applications/${SPA_OBJECT_ID}" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"spa\":{\"redirectUris\":${UPDATED}}}" > /dev/null

echo "${MODE}d: $DESCRIPTION"
