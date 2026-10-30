#!/usr/bin/env python3
"""Retain the Manual Journal Reversal gate and its full regression evidence."""
import datetime
import hashlib
import json
import pathlib
import shutil
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[1]
dest = root / "docs/evidence/reversal-v1"
dest.mkdir(parents=True, exist_ok=True)
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

latest = {}
for file in (root / "artifacts/tests").glob("*.trx"):
    tree = ET.parse(file)
    method = tree.find(".//t:TestMethod", ns)
    if method is None:
        continue
    assembly = pathlib.Path(method.get("codeBase", "")).name
    if assembly not in latest or latest[assembly][0].stat().st_mtime < file.stat().st_mtime:
        latest[assembly] = (file, tree)
assert set(latest) == {"Erp.UnitTests.dll", "Erp.ArchitectureTests.dll", "Erp.IntegrationTests.dll"}

runs = []
for assembly, (file, tree) in sorted(latest.items()):
    counters = tree.find(".//t:Counters", ns).attrib
    assert int(counters["failed"]) == int(counters["notExecuted"]) == 0
    assert int(counters["executed"]) == int(counters["passed"])
    target = dest / (assembly.removesuffix(".dll") + ".trx")
    shutil.copyfile(file, target)
    runs.append({
        "assembly": assembly,
        "executed": int(counters["executed"]),
        "passed": int(counters["passed"]),
        "failed": 0,
        "skipped": 0,
        "sha256": hashlib.sha256(target.read_bytes()).hexdigest(),
    })

sources = {
    "artifacts/security/npm-audit.json": "npm-audit.json",
    "artifacts/security/nuget-audit.json": "nuget-audit.json",
    "artifacts/web/unit-results.json": "angular-unit-results.json",
    "artifacts/web/browser-results.json": "foundation-browser-results.json",
    "artifacts/journal/golden-results.json": "journal-regression-results.json",
    "artifacts/trial-balance/golden-results.json": "trial-balance-regression-results.json",
    "artifacts/general-ledger/golden-results.json": "general-ledger-regression-results.json",
    "artifacts/reversal/golden-results.json": "golden-results.json",
    "artifacts/reversal/browser-results.json": "browser-results.json",
    "artifacts/reversal/reversal-ar.png": "reversal-ar.png",
    "artifacts/reversal/reversal-en.png": "reversal-en.png",
}
for source, name in sources.items():
    shutil.copyfile(root / source, dest / name)
shutil.copyfile("/tmp/erp-reversal-verify-final.log", dest / "verification.txt")

def read(name):
    return json.loads((dest / name).read_text())

angular = read("angular-unit-results.json")
assert angular["success"] and angular["numTotalTests"] == angular["numPassedTests"]
assert angular["numFailedTests"] == angular["numPendingTests"] == 0
browser_names = [
    "foundation-browser-results.json",
    "journal-regression-results.json",
    "trial-balance-regression-results.json",
    "general-ledger-regression-results.json",
    "golden-results.json",
]
browsers = [read(name) for name in browser_names]
assert all(run.get("status", "PASS") == "PASS" and not run["errors"] for run in browsers)
assert all(check["status"] == "PASS" for run in browsers for check in run["checks"])
golden = browsers[-1]
db = golden["databaseChecks"]
assert db["originalUnchanged"] is True and db["originalPeriod"] == "Closed" and db["reversalPeriod"] == "Open"
assert db["postedJournals"] == "2" and db["links"] == "1" and db["reversedOutbox"] == "1"
assert db["reversalReceipts"] == "1" and db["unbalanced"] == "0"

assert read("npm-audit.json")["metadata"]["vulnerabilities"]["total"] == 0
nuget = read("nuget-audit.json")
assert not nuget.get("problems")
for project in nuget.get("projects", []):
    for framework in project.get("frameworks") or []:
        for group in ["topLevelPackages", "transitivePackages"]:
            assert all(not package.get("vulnerabilities") for package in framework.get(group, []))
assert (root / "artifacts/api/Erp.Api.dll").exists()
assert (root / "artifacts/worker/Erp.Worker.dll").exists()

baseline = read("../general-ledger-v1/summary.json")
automated = sum(run["passed"] for run in runs) + angular["numPassedTests"]
browser_checks = sum(len(run["checks"]) for run in browsers)
assert automated == 185 and automated - baseline["automatedTestsTotal"] == 34
assert browser_checks == 96 and len(golden["checks"]) == 22
recorded = datetime.datetime.now(datetime.timezone.utc).isoformat()
summary = {
    "decision": "SLICE PASS",
    "recordedAtUtc": recorded,
    "scope": "Finance Manual Journal Reversal; all previous regression gates required",
    "dotnet": runs,
    "dotnetTotal": sum(run["passed"] for run in runs),
    "angularUnit": angular["numPassedTests"],
    "automatedTestsTotal": automated,
    "newAutomatedTests": automated - baseline["automatedTestsTotal"],
    "browserAcceptanceChecksTotal": browser_checks,
    "newBrowserAcceptanceChecks": len(golden["checks"]),
    "reversalBrowserChecks": len(golden["checks"]),
    "assertionCounting": "xUnit theory rows plus Vitest tests; browser acceptance checks are counted separately",
    "goldenDatabaseChecks": db,
    "migrationsAdded": 1,
    "tablesAdded": 1,
    "explicitIndexesAdded": 1,
    "architecture": "PASS",
    "securityAudits": "No reported vulnerabilities at execution time",
    "ciRemoteExecuted": False,
    "productionDeployment": False,
    "nextSliceStarted": False,
    "remainingExternalGates": [
        "production identity/MFA",
        "production deployment/operations",
        "Jordan/JoFotara/legal/retention/residency verification",
    ],
}
(dest / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")

excluded = {"bin", "obj", "node_modules", ".angular", "dist", ".local", "artifacts", ".git", "poc-v1", "__pycache__"}
files = []
for file in sorted(root.rglob("*")):
    rel = file.relative_to(root)
    if any(part in excluded for part in rel.parts) or not file.is_file() or str(rel).startswith("docs/evidence/"):
        continue
    if file.suffix in [".cs", ".csproj", ".ts", ".html", ".css", ".json", ".py", ".sh", ".md", ".yml", ".props", ".slnx", ".mjs"]:
        files.append({"path": str(rel), "sha256": hashlib.sha256(file.read_bytes()).hexdigest()})
(dest / "source-manifest.json").write_text(json.dumps({"recordedAtUtc": recorded, "files": files}, indent=2) + "\n")
print(json.dumps(summary, indent=2))
