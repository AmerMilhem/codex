#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/env.sh
if ! command -v dotnet >/dev/null; then python3 scripts/install-sdk.py; export PATH="$POC_TOOLS/dotnet:$PATH"; fi
test "$(dotnet --version)" = 10.0.402
test "$(node --version)" = v24.19.0
dotnet restore Erp.slnx --locked-mode
dotnet tool restore
npm --prefix src/Clients/Web ci --ignore-scripts
