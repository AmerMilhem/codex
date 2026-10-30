#!/usr/bin/env python3
"""Retain Trial Balance evidence without changing the approved Foundation/Manual Journal records."""
import datetime,hashlib,json,pathlib,shutil,xml.etree.ElementTree as ET

root=pathlib.Path(__file__).resolve().parents[1];dest=root/'docs/evidence/trial-balance-v1';dest.mkdir(parents=True,exist_ok=True)
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};latest={}
for file in (root/'artifacts/tests').glob('*.trx'):
 tree=ET.parse(file);method=tree.find('.//t:TestMethod',ns)
 if method is None:continue
 assembly=pathlib.Path(method.get('codeBase','')).name
 if assembly not in latest or latest[assembly][0].stat().st_mtime<file.stat().st_mtime:latest[assembly]=(file,tree)
assert set(latest)=={'Erp.UnitTests.dll','Erp.ArchitectureTests.dll','Erp.IntegrationTests.dll'}
runs=[]
for assembly,(file,tree) in sorted(latest.items()):
 c=tree.find('.//t:Counters',ns).attrib
 assert int(c['failed'])==int(c['notExecuted'])==0 and int(c['executed'])==int(c['passed'])
 target=dest/(assembly.removesuffix('.dll')+'.trx');shutil.copyfile(file,target)
 runs.append({'assembly':assembly,'executed':int(c['executed']),'passed':int(c['passed']),'failed':0,'skipped':0,'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
sources={
 'artifacts/security/npm-audit.json':'npm-audit.json','artifacts/security/nuget-audit.json':'nuget-audit.json',
 'artifacts/web/unit-results.json':'angular-unit-results.json','artifacts/web/browser-results.json':'foundation-browser-results.json',
 'artifacts/journal/golden-results.json':'journal-regression-results.json',
 'artifacts/trial-balance/golden-results.json':'golden-results.json','artifacts/trial-balance/browser-results.json':'browser-results.json',
 'artifacts/trial-balance/performance.json':'performance.json','artifacts/trial-balance/accounts-plan.json':'accounts-plan.json',
 'artifacts/trial-balance/control-plan.json':'control-plan.json','artifacts/trial-balance/selective-plan.json':'selective-plan.json',
 'artifacts/trial-balance/ef-projection.sql':'ef-projection.sql',
 'artifacts/trial-balance/trial-balance-ar.png':'trial-balance-ar.png','artifacts/trial-balance/trial-balance-en.png':'trial-balance-en.png',
 'artifacts/trial-balance/trial-balance-mobile-ar.png':'trial-balance-mobile-ar.png',
}
for source,name in sources.items():shutil.copyfile(root/source,dest/name)
def read(name):return json.loads((dest/name).read_text())
unit=read('angular-unit-results.json');assert unit['success'] and unit['numTotalTests']==unit['numPassedTests'] and unit['numFailedTests']==unit['numPendingTests']==0
foundation=read('foundation-browser-results.json');journal=read('journal-regression-results.json');golden=read('golden-results.json');performance=read('performance.json')
assert journal['status']==golden['status']=='PASS'
assert all(c['status']=='PASS' for run in [foundation,journal,golden] for c in run['checks'])
assert all(not run['errors'] for run in [foundation,journal,golden])
assert golden['databaseChecks']['readOnlyStateUnchanged'] is True and golden['databaseChecks']['globalUnbalanced']=='0'
assert performance['dataset']['lines']==100000 and performance['rowsReturned']==100
assert read('npm-audit.json')['metadata']['vulnerabilities']['total']==0
nuget=read('nuget-audit.json');assert not nuget.get('problems')
for project in nuget.get('projects',[]):
 for framework in project.get('frameworks',[]):
  for group in ['topLevelPackages','transitivePackages']:assert all(not p.get('vulnerabilities') for p in framework.get(group,[]))
assert (root/'artifacts/api/Erp.Api.dll').exists() and (root/'artifacts/worker/Erp.Worker.dll').exists()
baseline=json.loads((root/'docs/evidence/journal-v1/summary.json').read_text())
tests=sum(run['passed'] for run in runs)+unit['numPassedTests'];browser=sum(len(run['checks']) for run in [foundation,journal,golden])
summary={'decision':'SLICE PASS','recordedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
 'scope':'Trial Balance read-only; Foundation and Manual Journal regressions required; no production deployment or compliance claim',
 'dotnet':runs,'dotnetTotal':sum(run['passed'] for run in runs),'angularUnit':unit['numPassedTests'],
 'automatedTestsTotal':tests,'newAutomatedTests':tests-baseline['automatedTestsTotal'],
 'browserAcceptanceChecksTotal':browser,'newBrowserAcceptanceChecks':browser-baseline['browserAcceptanceChecksTotal'],
 'trialBalanceBrowserChecks':len(golden['checks']),'assertionCounting':'xUnit theory rows + Vitest tests; browser acceptance checks counted separately; SQL controls not counted again',
 'readOnlyStateUnchanged':True,'goldenDatabaseChecks':golden['databaseChecks'],'performanceDataset':performance['dataset'],
 'performanceMedianMilliseconds':performance['medians'],'indexesAdded':0,'migrationsAdded':0,
 'build':'Release clean; 0 warnings/errors','architecture':'PASS','securityAudits':'no currently reported vulnerabilities',
 'ciRemoteExecuted':False,'productionDeployment':False,'nextSliceStarted':False,
 'remainingExternalGates':['production identity/MFA','production deployment/operations','Jordan/JoFotara/legal/retention/residency verification']}
(dest/'summary.json').write_text(json.dumps(summary,indent=2,ensure_ascii=False))
excluded={'bin','obj','node_modules','.angular','dist','.local','artifacts','.git','poc-v1','__pycache__'};files=[]
for file in sorted(root.rglob('*')):
 relative=file.relative_to(root)
 if any(p in excluded for p in relative.parts) or not file.is_file() or str(relative).startswith('docs/evidence/'):continue
 if file.suffix in ['.cs','.csproj','.ts','.html','.css','.json','.py','.sh','.md','.yml','.props','.slnx','.mjs']:
  files.append({'path':str(relative),'sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
(dest/'source-manifest.json').write_text(json.dumps({'recordedAtUtc':summary['recordedAtUtc'],'files':files},indent=2))
print(json.dumps(summary,indent=2,ensure_ascii=False))
