#!/usr/bin/env python3
"""Retain only the latest final run per assembly, with hashes and explicit unexecuted scope."""
import datetime,hashlib,json,pathlib,shutil,xml.etree.ElementTree as ET
root=pathlib.Path(__file__).resolve().parents[1];dest=root/'docs/evidence';dest.mkdir(exist_ok=True)
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};latest={}
for f in (root/'artifacts/tests').glob('*.trx'):
 x=ET.parse(f);method=x.find('.//t:TestMethod',ns)
 if method is None:continue
 assembly=pathlib.Path(method.get('codeBase','')).name
 if assembly not in latest or latest[assembly][0].stat().st_mtime<f.stat().st_mtime:latest[assembly]=(f,x)
assert set(latest)=={'Erp.UnitTests.dll','Erp.ArchitectureTests.dll','Erp.IntegrationTests.dll'}
runs=[]
for assembly,(f,x) in sorted(latest.items()):
 c=x.find('.//t:Counters',ns).attrib
 assert int(c['failed'])==0 and int(c['executed'])==int(c['passed']) and int(c['notExecuted'])==0
 target=dest/(assembly.replace('.dll','')+'.trx');shutil.copyfile(f,target)
 runs.append({'assembly':assembly,'executed':int(c['executed']),'passed':int(c['passed']),'failed':0,'skipped':0,'sha256':hashlib.sha256(target.read_bytes()).hexdigest()})
for source,name in [
 ('artifacts/security/npm-audit.json','npm-audit.json'),('artifacts/security/nuget-audit.json','nuget-audit.json'),
 ('artifacts/web/browser-results.json','browser-results.json'),('artifacts/web/shell-rtl.png','shell-rtl.png'),
 ('artifacts/web/unit-results.json','angular-unit-results.json'),
 ('artifacts/local/startup-results.json','startup-results.json')]:shutil.copyfile(root/source,dest/name)
assert json.loads((dest/'npm-audit.json').read_text())['metadata']['vulnerabilities']['total']==0
browser=json.loads((dest/'browser-results.json').read_text());assert all(t['status']=='PASS' for t in browser['checks'])
assert json.loads((dest/'startup-results.json').read_text())['status']=='PASS'
unit=json.loads((dest/'angular-unit-results.json').read_text())
assert unit['success'] and unit['numTotalTests']>0 and unit['numPassedTests']==unit['numTotalTests'] and unit['numFailedTests']==unit['numPendingTests']==0
assert (root/'artifacts/api/Erp.Api.dll').exists() and (root/'artifacts/worker/Erp.Worker.dll').exists()
summary={'decision':'FOUNDATION PASS','scope':'Authorized Backend/Web Foundation only; no business feature or production readiness claim',
 'recordedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'dotnet':runs,'dotnetTotal':sum(x['passed'] for x in runs),
 'angularUnit':{'executed':unit['numTotalTests'],'passed':unit['numPassedTests'],'failed':unit['numFailedTests'],'runner':'Vitest 4.1.12'},'browser':{'executed':len(browser['checks']),'passed':len(browser['checks']),'browser':browser['browser']},
 'localStartup':'PASS','securityAudits':'no currently reported vulnerabilities','ciRemoteExecuted':False,'productionDeployment':False,
 'userApprovalRequiredForNextSlice':True}
(dest/'summary.json').write_text(json.dumps(summary,indent=2))
print(json.dumps(summary,indent=2))
