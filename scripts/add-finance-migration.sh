#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ $# -ne 1 || ! "$1" =~ ^[A-Za-z][A-Za-z0-9]*$ ]]; then
  echo 'Usage: scripts/add-finance-migration.sh MigrationName' >&2
  exit 2
fi
source scripts/env.sh
# EF --no-build uses compiled migration metadata. Compile every time immediately before add.
dotnet build src/Modules/Finance/Erp.Finance.Infrastructure --no-restore --verbosity quiet
dotnet ef migrations add "$1" \
  --project src/Modules/Finance/Erp.Finance.Infrastructure \
  --startup-project src/Modules/Finance/Erp.Finance.Infrastructure \
  --context FinanceDbContext --output-dir Migrations --no-build
