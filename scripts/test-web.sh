#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../src/Clients/Web"
npm run lint
npm run format:check
npm run build
npm test
if curl -fsS http://127.0.0.1:4208 >/dev/null 2>&1; then echo 'Port 4208 already in use' >&2; exit 1; fi
python3 ../../../scripts/serve-web.py > /tmp/erp-foundation-web.log 2>&1 &
server_pid=$!
trap 'kill "$server_pid" 2>/dev/null || true' EXIT
for attempt in {1..30}; do kill -0 "$server_pid"; if curl -fsS http://127.0.0.1:4208 >/dev/null 2>&1; then break; fi; sleep 1; done
npm run test:browser
