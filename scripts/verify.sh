#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/env.sh
mkdir -p artifacts/tests
dotnet restore Erp.slnx --locked-mode --disable-parallel -m:1 -nr:false
python3 scripts/check-dependencies.py
dotnet build Erp.slnx --no-restore --configuration Release --verbosity minimal -m:2 -nr:false
dotnet format Erp.slnx --verify-no-changes --no-restore
dotnet test Erp.slnx --no-build --no-restore --configuration Release --logger trx --results-directory artifacts/tests
bash scripts/test-web.sh
python3 scripts/test-journal-e2e.py
python3 scripts/test-trial-balance-e2e.py
python3 scripts/test-general-ledger-e2e.py
python3 scripts/test-reversal-e2e.py
python3 scripts/test-period-close-e2e.py
python3 scripts/test-period-setup-e2e.py
python3 scripts/test-account-setup-e2e.py
python3 scripts/test-book-setup-e2e.py
python3 scripts/test-correction-e2e.py
bash scripts/security-check.sh
dotnet publish src/Hosts/Erp.Api --no-restore --configuration Release --output artifacts/api
dotnet publish src/Hosts/Erp.Worker --no-restore --configuration Release --output artifacts/worker
