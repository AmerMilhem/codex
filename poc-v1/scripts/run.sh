#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export POC_TOOLS="${POC_TOOLS:-/workspace/erp-poc-v1/tools}"
export DOTNET_CLI_HOME="$POC_TOOLS/cli" NUGET_PACKAGES="$POC_TOOLS/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_EnableDiagnostics=0
dotnet_bin="$POC_TOOLS/dotnet/dotnet"
image='postgres:18.2@sha256:a688ae5c99f86c1424e046ace0de890608cc0ddb5ec8ef72a6ff8305fdc8f4d2'
if ! docker image inspect "$image" >/dev/null 2>&1; then docker pull "$image"; fi
if docker container inspect erp-poc-v1-pg >/dev/null 2>&1; then
 test "$(docker inspect --format '{{index .Config.Labels "purpose"}}' erp-poc-v1-pg)" = erp-throwaway-poc
 docker start erp-poc-v1-pg >/dev/null
else
 docker run -d --name erp-poc-v1-pg --label purpose=erp-throwaway-poc -e POSTGRES_PASSWORD=disposable-poc-only -e POSTGRES_DB=poc -p 127.0.0.1:55432:5432 "$image"
fi
for attempt in {1..30}; do if docker exec erp-poc-v1-pg pg_isready -U postgres -d poc >/dev/null; then break; fi; sleep 1; done
docker exec erp-poc-v1-pg pg_isready -U postgres -d poc
mkdir -p evidence/runs
run_dir="evidence/runs/$(date -u +%Y%m%dT%H%M%S)"
mkdir -p "$run_dir"
"$dotnet_bin" restore src/Poc.csproj --locked-mode > "$run_dir/restore.log" 2>&1
"$dotnet_bin" build src/Poc.csproj --no-restore --verbosity minimal > "$run_dir/build.log" 2>&1
set +e
"$dotnet_bin" src/bin/Debug/net10.0/Poc.dll 2>&1 | tee "$run_dir/backend.log"
backend_exit=${PIPESTATUS[0]}
set -e
cp evidence/*.json "$run_dir/"
python3 scripts/backup_restore.py
python3 scripts/versions_licenses.py
exit "$backend_exit"
