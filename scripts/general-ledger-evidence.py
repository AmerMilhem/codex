#!/usr/bin/env python3
"""Retain the GL gate while preserving approved Foundation/Journal/Trial Balance evidence."""
import datetime,hashlib,json,pathlib,shutil,xml.etree.ElementTree as ET
root=pathlib.Path(__file__).resolve().parents[1];dest=root/'docs/evidence/general-ledger-v1';dest.mkdir(parents=True,exist_ok=True)
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
'artifacts/journal/golden-results.json':'journal-regression-results.json','artifacts/trial-balance/golden-results.json':'trial-balance-regression-results.json',
'artifacts/general-ledger/golden-results.json':'golden-results.json','artifacts/general-ledger/browser-results.json':'browser-results.json',
'artifacts/general-ledger/performance.json':'performance.json',
}
for pattern in ['*-plan.json','general-ledger-*.png']:
 for f in (root/'artifacts/general-ledger').glob(pattern):sources[str(f.relative_to(root))]=f.name
for source,name in sources.items():shutil.copyfile(root/source,dest/name)
def read(name):return json.loads((dest/name).read_text())
unit=read('angular-unit-results.json');assert unit['success'] and unit['numTotalTests']==unit['numPassedTests'] and unit['numFailedTests']==unit['numPendingTests']==0
browsers=[read(f) for f in ['foundation-browser-results.json','journal-regression-results.json','trial-balance-regression-results.json','golden-results.json']]
assert all(c['status']=='PASS' for run in browsers for c in run['checks']) and all(not run['errors'] for run in browsers)
golden=browsers[-1];assert golden['status']=='PASS' and golden['databaseChecks']['readOnlyStateUnchanged'] and golden['databaseChecks']['globalUnbalanced']=='0'
performance=read('performance.json');assert performance['dataset']['lines']==102000 and performance['dataset']['busyAccountLines']==51000
assert read('npm-audit.json')['metadata']['vulnerabilities']['total']==0
nuget=read('nuget-audit.json');assert not nuget.get('problems')
for p in nuget.get('projects',[]):
 for f in p.get('frameworks',[]):
  for group in ['topLevelPackages','transitivePackages']:assert all(not x.get('vulnerabilities') for x in f.get(group,[]))
assert (root/'artifacts/api/Erp.Api.dll').exists() and (root/'artifacts/worker/Erp.Worker.dll').exists()
baseline=json.loads((root/'docs/evidence/trial-balance-v1/summary.json').read_text())
tests=sum(r['passed'] for r in runs)+unit['numPassedTests'];browser=sum(len(r['checks']) for r in browsers)
plans={}
for file in sorted(dest.glob('*-plan.json')):
 d=json.loads(file.read_text())[0];nodes=[];visits=[0.0]
 def walk(n):
  if 'Scan' in n['Node Type']:
   loops=n['Actual Loops'];rows=n['Actual Rows'];removed=n.get('Rows Removed by Filter',0);visits[0]+=(rows+removed)*loops
   nodes.append({'node':n['Node Type'],'relation':n.get('Relation Name'),'index':n.get('Index Name'),'rowsPerLoop':rows,'loops':loops,'rowsRemovedPerLoop':removed})
  for child in n.get('Plans',[]):walk(child)
 walk(d['Plan']);plans[file.name]={'executionMilliseconds':d['Execution Time'],'returnedRows':d['Plan']['Actual Rows'],'scanTupleVisits':visits[0],'scanNodes':nodes}
(dest/'plan-summary.json').write_text(json.dumps({'definition':'Scan-node tuple visits across relations/loops, not distinct ledger lines','queries':plans},indent=2))
summary={'decision':'SLICE PASS','recordedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'Finance General Ledger Detail, read-only; all previous regression gates required',
'dotnet':runs,'dotnetTotal':sum(r['passed'] for r in runs),'angularUnit':unit['numPassedTests'],'automatedTestsTotal':tests,'newAutomatedTests':tests-baseline['automatedTestsTotal'],
'browserAcceptanceChecksTotal':browser,'newBrowserAcceptanceChecks':browser-baseline['browserAcceptanceChecksTotal'],'generalLedgerBrowserChecks':len(golden['checks']),
'assertionCounting':'xUnit theory rows plus Vitest tests; named browser acceptance checks counted separately; SQL controls not double-counted',
'goldenDatabaseChecks':golden['databaseChecks'],'performanceDataset':performance['dataset'],'performanceMedianMilliseconds':performance['medians'],
'migrationsAdded':0,'indexesAdded':0,'architecture':'PASS','securityAudits':'No reported vulnerabilities at execution time',
'ciRemoteExecuted':False,'productionDeployment':False,'nextSliceStarted':False,
'remainingExternalGates':['production identity/MFA','production deployment/operations','Jordan/JoFotara/legal/retention/residency verification']}
(dest/'summary.json').write_text(json.dumps(summary,indent=2))
excluded={'bin','obj','node_modules','.angular','dist','.local','artifacts','.git','poc-v1','__pycache__'};files=[]
for file in sorted(root.rglob('*')):
 rel=file.relative_to(root)
 if any(p in excluded for p in rel.parts) or not file.is_file() or str(rel).startswith('docs/evidence/'):continue
 if file.suffix in ['.cs','.csproj','.ts','.html','.css','.json','.py','.sh','.md','.yml','.props','.slnx','.mjs']:files.append({'path':str(rel),'sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
(dest/'source-manifest.json').write_text(json.dumps({'recordedAtUtc':summary['recordedAtUtc'],'files':files},indent=2))
print(json.dumps(summary,indent=2))
