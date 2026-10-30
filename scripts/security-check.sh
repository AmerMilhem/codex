#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/env.sh
mkdir -p artifacts/security
dotnet list Erp.slnx package --vulnerable --include-transitive --format json > artifacts/security/nuget-audit.json
python3 - <<'PY'
import json
x=json.load(open('artifacts/security/nuget-audit.json'))
assert not x.get('problems'),x.get('problems')
for project in x.get('projects',[]):
 for target in project.get('frameworks',[]):
  for group in ['topLevelPackages','transitivePackages']:
   for package in target.get(group,[]):
    assert not package.get('vulnerabilities'),(package['id'],package.get('vulnerabilities'))
print('NuGet vulnerability gate passed.')
PY
npm --prefix src/Clients/Web audit --audit-level=moderate --json > artifacts/security/npm-audit.json
