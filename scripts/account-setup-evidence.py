#!/usr/bin/env python3
"""Retain and validate the complete Chart of Accounts Setup acceptance gate."""
import datetime, hashlib, json, pathlib, shutil, xml.etree.ElementTree as ET

root=pathlib.Path(__file__).resolve().parents[1]
dest=root/'docs/evidence/account-setup-v1'; dest.mkdir(parents=True,exist_ok=True)
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
latest={}
for file in (root/'artifacts/tests').glob('*.trx'):
    tree=ET.parse(file)
    method=tree.find('.//t:TestMethod',ns)
    if method is None:continue
    assembly=pathlib.Path(method.get('codeBase','')).name
    if assembly not in latest or latest[assembly][0].stat().st_mtime<file.stat().st_mtime:latest[assembly]=(file,tree)
assert set(latest)=={'Erp.UnitTests.dll','Erp.ArchitectureTests.dll','Erp.IntegrationTests.dll'}
runs=[]
for assembly,(file,tree) in sorted(latest.items()):
    counters=tree.find('.//t:Counters',ns).attrib
    assert int(counters['failed'])==int(counters['notExecuted'])==0
    assert int(counters['executed'])==int(counters['passed'])
    target=dest/(assembly.removesuffix('.dll')+'.trx'); shutil.copyfile(file,target)
    runs.append({'assembly':assembly,'passed':int(counters['passed']),'failed':0,'skipped':0,'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
sources={
 'artifacts/security/npm-audit.json':'npm-audit.json',
 'artifacts/security/nuget-audit.json':'nuget-audit.json',
 'artifacts/web/unit-results.json':'angular-unit-results.json',
 'artifacts/web/browser-results.json':'foundation-browser-results.json',
 'artifacts/journal/golden-results.json':'journal-regression-results.json',
 'artifacts/trial-balance/golden-results.json':'trial-balance-regression-results.json',
 'artifacts/general-ledger/golden-results.json':'general-ledger-regression-results.json',
 'artifacts/reversal/golden-results.json':'reversal-regression-results.json',
 'artifacts/period-close/golden-results.json':'period-close-regression-results.json',
 'artifacts/period-setup/golden-results.json':'period-setup-regression-results.json',
 'artifacts/account-setup/golden-results.json':'golden-results.json',
 'artifacts/account-setup/browser-results.json':'browser-results.json',
 'artifacts/account-setup/account-setup-ar.png':'account-setup-ar.png',
 'artifacts/account-setup/account-setup-en.png':'account-setup-en.png',
}
for source,name in sources.items():shutil.copyfile(root/source,dest/name)
shutil.copyfile('/tmp/account-verify.log',dest/'verification.txt')
def read(name):return json.loads((dest/name).read_text())
angular=read('angular-unit-results.json')
assert angular['success'] and angular['numTotalTests']==angular['numPassedTests'] and angular['numFailedTests']==angular['numPendingTests']==0
browser_names=['foundation-browser-results.json','journal-regression-results.json','trial-balance-regression-results.json','general-ledger-regression-results.json','reversal-regression-results.json','period-close-regression-results.json','period-setup-regression-results.json','golden-results.json']
browsers=[read(name) for name in browser_names]
assert all(run.get('status','PASS')=='PASS' and all(check['status']=='PASS' for check in run['checks']) for run in browsers)
golden=browsers[-1]; db=golden['databaseChecks']
assert db['periodState']=='Open:1' and db['rlsForced']=='t' and db['unbalanced']=='0'
assert all(db[key]=='3' for key in ['accountRows','createRequestedAudit','createdAudit','createdOutbox','createReceipt'])
assert db['postedJournals']=='1' and db['newAccountJournal']=='2' and db['nonpostable']=='t'
assert read('npm-audit.json')['metadata']['vulnerabilities']['total']==0
nuget=read('nuget-audit.json'); assert not nuget.get('problems')
for project in nuget.get('projects',[]):
    for framework in project.get('frameworks') or []:
        for group in ['topLevelPackages','transitivePackages']:
            assert all(not package.get('vulnerabilities') for package in framework.get(group,[]))
assert (root/'artifacts/api/Erp.Api.dll').exists() and (root/'artifacts/worker/Erp.Worker.dll').exists()
prior_manifest=read('../period-setup-v1/source-manifest.json')['files']
prior={entry['path']:entry['sha256'] for entry in prior_manifest}
prior_designer='src/Modules/Finance/Erp.Finance.Infrastructure/Migrations/20261023212645_FiscalPeriodSetupV1.Designer.cs'
assert hashlib.sha256((root/prior_designer).read_bytes()).hexdigest()==prior[prior_designer]
migration_dir=root/'src/Modules/Finance/Erp.Finance.Infrastructure/Migrations'
migration_ids=sorted(file.stem for file in migration_dir.glob('20*_*.cs') if not file.name.endswith('.Designer.cs'))
assert migration_ids==['20261018141639_ManualJournalV1','20261020202816_ManualJournalReversalV1',
    '20261023152528_FiscalPeriodCloseV1','20261023212645_FiscalPeriodSetupV1',
    '20261024162900_ChartOfAccountsSetupV1']
chain={'migrationIds':migration_ids,'priorSetupDesignerMatchesApprovedManifest':True,
    'newMigration':'20261024162900_ChartOfAccountsSetupV1'}
(dest/'migration-chain-check.json').write_text(json.dumps(chain,indent=2)+'\n')
baseline=read('../period-setup-v1/summary.json')
automated=sum(run['passed'] for run in runs)+angular['numPassedTests']
browser_checks=sum(len(run['checks']) for run in browsers)
assert automated>baseline['automatedTestsTotal'] and browser_checks>baseline['browserAcceptanceChecksTotal']
summary={
 'decision':'SLICE PASS','recordedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
 'scope':'Finance Chart of Accounts Setup; all previous regression gates required',
 'dotnet':runs,'dotnetTotal':sum(run['passed'] for run in runs),'angularUnit':angular['numPassedTests'],
 'automatedTestsTotal':automated,'newAutomatedTests':automated-baseline['automatedTestsTotal'],
 'browserAcceptanceChecksTotal':browser_checks,'newBrowserAcceptanceChecks':len(golden['checks']),
 'accountSetupBrowserChecks':len(golden['checks']),'goldenDatabaseChecks':db,
 'migrationsAdded':1,'tablesAdded':0,'architecture':'PASS',
 'migrationChainCheck':chain,
 'securityAudits':'No reported vulnerabilities at execution time','ciRemoteExecuted':False,
 'productionDeployment':False,'nextSliceStarted':False,
}
(dest/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
excluded={'bin','obj','node_modules','.angular','dist','.local','artifacts','.git','poc-v1','__pycache__'}
files=[]
for file in sorted(root.rglob('*')):
    rel=file.relative_to(root)
    if any(part in excluded for part in rel.parts) or not file.is_file() or str(rel).startswith('docs/evidence/'):continue
    if file.suffix in ['.cs','.csproj','.ts','.html','.css','.json','.py','.sh','.md','.yml','.props','.slnx','.mjs']:
        files.append({'path':str(rel),'sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
(dest/'source-manifest.json').write_text(json.dumps({'recordedAtUtc':summary['recordedAtUtc'],'files':files},indent=2)+'\n')
print(json.dumps(summary,indent=2))
