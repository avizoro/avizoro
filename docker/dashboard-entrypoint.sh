#!/bin/sh
# Regenerate the dashboard's runtime config from $API_BASE_URL at container
# start. The Blazor WASM app fetches wwwroot/appsettings.json in the browser, so
# this is the seam for pointing a prebuilt image at any server without rebuilding.
set -eu

: "${API_BASE_URL:=http://localhost:7099}"
ROOT="/usr/share/nginx/html"

# Platforms that wire services together by hostname (e.g. Render's fromService
# "host") give a bare host; the browser needs a full URL.
case "$API_BASE_URL" in
  *://*) ;;
  *) API_BASE_URL="https://${API_BASE_URL}" ;;
esac

cat > "${ROOT}/appsettings.json" <<EOF
{
  "ApiBaseUrl": "${API_BASE_URL}"
}
EOF

# Drop the precompressed siblings from the published image so nginx serves the
# freshly written plaintext, not a stale build-time copy.
rm -f "${ROOT}/appsettings.json.br" "${ROOT}/appsettings.json.gz"

echo "[avizoro-dashboard] ApiBaseUrl set to ${API_BASE_URL}"
