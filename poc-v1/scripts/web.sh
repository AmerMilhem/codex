#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../web"
export npm_config_cache="${POC_TOOLS:-/workspace/erp-poc-v1/tools}/npm" NG_CLI_ANALYTICS=false
npm ci --ignore-scripts --no-fund > ../evidence/npm-ci.log 2>&1
npm run build > ../evidence/angular-build.log 2>&1
if curl -fsS http://127.0.0.1:4207 >/dev/null 2>&1; then echo 'PoC port 4207 already in use; stop its known owner first' >&2; exit 1; fi
python3 -m http.server 4207 --bind 127.0.0.1 --directory dist/grid/browser > ../evidence/angular-server.log 2>&1 &
server_pid=$!
trap 'kill "$server_pid" 2>/dev/null || true' EXIT
for attempt in {1..30}; do kill -0 "$server_pid"; if curl -fsS http://127.0.0.1:4207 >/dev/null 2>&1; then break; fi; sleep 1; done
npm run test:browser
