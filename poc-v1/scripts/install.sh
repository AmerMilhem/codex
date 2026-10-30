#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export POC_TOOLS="${POC_TOOLS:-/workspace/erp-poc-v1/tools}"
export DOTNET_CLI_HOME="$POC_TOOLS/cli" NUGET_PACKAGES="$POC_TOOLS/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export npm_config_cache="$POC_TOOLS/npm" NG_CLI_ANALYTICS=false
python3 scripts/bootstrap.py
"$POC_TOOLS/dotnet/dotnet" restore src/Poc.csproj --locked-mode
"$POC_TOOLS/dotnet/dotnet" build src/Poc.csproj --no-restore --verbosity minimal
cd web
npm ci --ignore-scripts --no-fund
npm run build
