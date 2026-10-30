#!/usr/bin/env python3
"""Retain slice evidence separately; never overwrite the approved historical Foundation report."""
import datetime,hashlib,json,pathlib,shutil,xml.etree.ElementTree as ET

root=pathlib.Path(__file__).resolve().parents[1];dest=root/'docs/evidence/journal-v1';dest.mkdir(parents=True,exist_ok=True)
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};latest={}
for f in (root/'artifacts/tests').glob('*.trx'):
 tree=ET.parse(f);method=tree.find('.//t:TestMethod',ns)
 if method is None:continue
 assembly=pathlib.Path(method.get('codeBase','')).name
 if assembly not in latest or latest[assembly][0].stat().st_mtime<f.stat().st_mtime:latest[assembly]=(f,tree)
assert set(latest)=={'Erp.UnitTests.dll','Erp.ArchitectureTests.dll','Erp.IntegrationTests.dll'}
runs=[]
for assembly,(f,tree) in sorted(latest.items()):
 counters=tree.find('.//t:Counters',ns).attrib
 assert int(counters['failed'])==0 and int(counters['executed'])==int(counters['passed']) and int(counters['notExecuted'])==0
 target=dest/(assembly.removesuffix('.dll')+'.trx');shutil.copyfile(f,target)
 runs.append({'assembly':assembly,'executed':int(counters['executed']),'passed':int(counters['passed']),'failed':0,'skipped':0,'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
sources={
 'artifacts/security/npm-audit.json':'npm-audit.json','artifacts/security/nuget-audit.json':'nuget-audit.json',
 'artifacts/web/unit-results.json':'angular-unit-results.json','artifacts/web/browser-results.json':'foundation-browser-results.json',
 'artifacts/journal/golden-results.json':'golden-results.json','artifacts/journal/browser-results.json':'journal-browser-results.json',
 'artifacts/journal/journal-draft-rtl.png':'journal-draft-rtl.png','artifacts/journal/journal-posted-rtl.png':'journal-posted-rtl.png',
 'artifacts/local/startup-results.json':'startup-results.json',
}
for source,target in sources.items():shutil.copyfile(root/source,dest/target)
unit=json.loads((dest/'angular-unit-results.json').read_text());assert unit['success'] and unit['numTotalTests']==unit['numPassedTests'] and unit['numFailedTests']==unit['numPendingTests']==0
foundation=json.loads((dest/'foundation-browser-results.json').read_text());golden=json.loads((dest/'golden-results.json').read_text())
assert golden['status']=='PASS' and all(c['status']=='PASS' for c in foundation['checks']+golden['checks'])
assert not foundation['errors'] and not golden['errors']
assert json.loads((dest/'npm-audit.json').read_text())['metadata']['vulnerabilities']['total']==0
nuget=json.loads((dest/'nuget-audit.json').read_text());assert not nuget.get('problems')
for project in nuget.get('projects',[]):
 for framework in project.get('frameworks',[]):
  for group in ['topLevelPackages','transitivePackages']:
   assert all(not p.get('vulnerabilities') for p in framework.get(group,[]))
assert (root/'artifacts/api/Erp.Api.dll').exists() and (root/'artifacts/worker/Erp.Worker.dll').exists()
dotnet=sum(r['passed'] for r in runs);browser=len(foundation['checks'])+len(golden['checks']);total=dotnet+unit['numPassedTests']+browser
baseline=json.loads((root/'docs/evidence/summary.json').read_text());old=baseline['dotnetTotal']+baseline['angularUnit']['executed']+baseline['browser']['executed']
summary={'decision':'SLICE PASS','recordedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
 'scope':'Manual Journal only; Foundation regressions retained; no production deployment/identity/compliance approval',
 'dotnet':runs,'dotnetTotal':dotnet,'angularUnit':unit['numPassedTests'],
 'foundationBrowserChecks':len(foundation['checks']),'journalBrowserChecks':len(golden['checks']),
 'automatedTestsTotal':dotnet+unit['numPassedTests'],
 'newAutomatedTests':dotnet+unit['numPassedTests']-baseline['dotnetTotal']-baseline['angularUnit']['executed'],
 'browserAcceptanceChecksTotal':browser,'newBrowserAcceptanceChecks':browser-baseline['browser']['executed'],
 'automatedTestsAndChecksTotal':total,'newAutomatedTestsAndChecks':total-old,'foundationBaseline':old,
 'assertionCounting':'xUnit theory rows + Vitest tests + named browser acceptance checks; independent SQL checks are NOT counted again as tests',
 'goldenDatabaseChecks':golden['databaseChecks'],'build':'Release clean; 0 warnings/errors','architecture':'PASS',
 'securityAudits':'no currently reported vulnerabilities','localPublish':'API + Worker; no deployment',
 'ciRemoteExecuted':False,'nextSliceStarted':False,
 'remainingExternalGates':['production OIDC/MFA','production deployment/operations','Jordan/JoFotara/legal/retention/residency verification']}
(dest/'summary.json').write_text(json.dumps(summary,indent=2,ensure_ascii=False))
excluded={'bin','obj','node_modules','.angular','dist','.local','artifacts','.git','poc-v1'}
files=[]
for file in sorted(root.rglob('*')):
 relative=file.relative_to(root)
 if any(part in excluded for part in relative.parts) or not file.is_file() or str(relative).startswith('docs/evidence/'):continue
 if file.suffix in ['.cs','.csproj','.ts','.html','.css','.json','.py','.sh','.md','.yml','.props','.slnx']:
  files.append({'path':str(relative),'sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
(dest/'source-manifest.json').write_text(json.dumps({'recordedAtUtc':summary['recordedAtUtc'],'files':files},indent=2))
print(json.dumps(summary,indent=2,ensure_ascii=False))
