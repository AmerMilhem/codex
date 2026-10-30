#!/usr/bin/env python3
"""Real Angular -> HTTP -> PostgreSQL acceptance on an owned disposable database.
No fixture database, API response, posting or permission is mocked. Secrets stay out of artifacts.
"""
import json,os,pathlib,secrets,signal,subprocess,time,urllib.request,uuid,tempfile
from period_close_client import close_period

ROOT=pathlib.Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/reversal';OUT.mkdir(parents=True,exist_ok=True)
env=os.environ.copy();processes=[];logs=[]
name='erp-reversal-browser-'+uuid.uuid4().hex[:12]
passwords={key:secrets.token_hex(24) for key in ['admin','migration','runtime','token']}
tenant='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';company='11111111-1111-4111-8111-111111111111'
expense='55555555-5555-4555-8555-555555555555';cash='66666666-6666-4666-8666-666666666666'
image='postgres:18.2@sha256:a688ae5c99f86c1424e046ace0de890608cc0ddb5ec8ef72a6ff8305fdc8f4d2'
def run(args,**kwargs):return subprocess.run(args,cwd=ROOT,env=env,check=True,capture_output=True,**kwargs)
def sql(text):return run(['docker','exec','-i',name,'psql','-XAt','-v','ON_ERROR_STOP=1','-U','postgres','-d','journal_browser'],input=text.encode()).stdout.decode().strip()
def spawn(label,args):
 file=(OUT/(label+'.log')).open('w');logs.append(file)
 process=subprocess.Popen(args,cwd=ROOT,env=env,stdout=file,stderr=subprocess.STDOUT,start_new_session=True);processes.append(process);return process
def ready(url,process):
 for _ in range(120):
  assert process.poll() is None,'Process failed; inspect artifacts/reversal logs.'
  try:
   with urllib.request.urlopen(url,timeout=1) as response:
    if response.status==200:return
  except OSError:time.sleep(.25)
 raise AssertionError('Readiness timed out: '+url)
try:
 for port in [5081,4209]:
  import socket
  with socket.socket() as probe:
   probe.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1);probe.bind(('127.0.0.1',port))
 with tempfile.NamedTemporaryFile(mode='w',dir='/tmp') as secret_file:
  secret_file.write('POSTGRES_PASSWORD='+passwords['admin']+'\nPOSTGRES_DB=journal_browser\n');secret_file.flush()
  run(['docker','run','--detach','--name',name,'--label','purpose=erp-reversal-browser-test','--env-file',secret_file.name,'--publish','127.0.0.1::5432',image])
 for _ in range(100):
  if subprocess.run(['docker','exec',name,'pg_isready','-h','127.0.0.1','-U','postgres','-d','journal_browser'],capture_output=True).returncode==0:break
  time.sleep(.2)
 else:raise AssertionError('PostgreSQL failed to start')
 port=json.loads(run(['docker','inspect',name]).stdout)[0]['NetworkSettings']['Ports']['5432/tcp'][0]['HostPort']
 sql(f"CREATE ROLE erp_migrator LOGIN PASSWORD '{passwords['migration']}' NOSUPERUSER NOBYPASSRLS; CREATE ROLE erp_runtime LOGIN PASSWORD '{passwords['runtime']}' NOSUPERUSER NOBYPASSRLS; ALTER DATABASE journal_browser OWNER TO erp_migrator; REVOKE CREATE ON SCHEMA public FROM PUBLIC;")
 env['ERP_MIGRATION_CONNECTION']=f"Host=127.0.0.1;Port={port};Database=journal_browser;Username=erp_migrator;Password={passwords['migration']}"
 env['ERP_RUNTIME_CONNECTION']=f"Host=127.0.0.1;Port={port};Database=journal_browser;Username=erp_runtime;Password={passwords['runtime']};Maximum Pool Size=20"
 configuration=env.get('ERP_BUILD_CONFIGURATION','Release')
 run(['dotnet',f'src/Hosts/Erp.Migrations/bin/{configuration}/net10.0/Erp.Migrations.dll'])
 env.pop('ERP_MIGRATION_CONNECTION')
 sql(f"""INSERT INTO finance.accounting_books(tenant_id,company_id,currency,minor_units) VALUES('{tenant}','{company}','JOD',3);
 INSERT INTO finance.fiscal_periods(id,tenant_id,company_id,fiscal_year,starts_on,ends_on,status) VALUES(gen_random_uuid(),'{tenant}','{company}',2026,'2026-01-01','2026-01-31','Open');
 INSERT INTO finance.accounts(id,tenant_id,company_id,code,name,status,posting_allowed) VALUES
 ('{expense}','{tenant}','{company}','5000','مصروف / Expense','Active',true),('{cash}','{tenant}','{company}','1000','نقد / Cash','Active',true);""")
 env.update(ASPNETCORE_ENVIRONMENT='Development',DOTNET_ENVIRONMENT='Development',ERP_DEV_AUTH_TOKEN=passwords['token'],DevelopmentIdentity__Enabled='true',DevelopmentIdentity__Subject='reversal-browser-actor',DevelopmentIdentity__Grants__0__TenantId=tenant,DevelopmentIdentity__Grants__0__CompanyId=company)
 for i,permission in enumerate(['foundation.context.read','Finance.Journal.View','Finance.Journal.Create','Finance.Journal.Edit','Finance.Journal.Post','Finance.Journal.Reverse','Finance.Period.View','Finance.Period.Close','outbox.dispatch']):env[f'DevelopmentIdentity__Grants__0__Permissions__{i}']=permission
 api=spawn('api',['dotnet',f'src/Hosts/Erp.Api/bin/{configuration}/net10.0/Erp.Api.dll','--urls','http://127.0.0.1:5081']);ready('http://127.0.0.1:5081/health/ready',api)
 env.update(ERP_WEB_PORT='4209',ERP_TEST_API_ORIGIN='http://127.0.0.1:5081',ERP_UI_ORIGIN='http://127.0.0.1:4209',ERP_E2E_TOKEN=passwords['token'])
 web=spawn('web',['python3','scripts/serve-web.py']);ready('http://127.0.0.1:4209',web)
 env['ERP_REVERSAL_PHASE']='prepare'
 browser=spawn('browser-prepare',['node','src/Clients/Web/reversal-browser-tests.mjs']);assert browser.wait(timeout=120)==0,'Prepare browser failed'
 setup=json.loads((OUT/'setup.json').read_text());original=setup['originalId']
 def original_digest():
  return sql(f"SELECT md5(string_agg(value,'|' ORDER BY value)) FROM (SELECT to_jsonb(j)::text value FROM finance.journal_entries j WHERE id='{original}' UNION ALL SELECT to_jsonb(l)::text FROM finance.journal_lines l WHERE journal_id='{original}') data")
 before=original_digest()
 close_period('http://127.0.0.1:5081',passwords['token'],tenant,company,sql("SELECT id FROM finance.fiscal_periods WHERE starts_on='2026-01-01'"))
 sql(f"INSERT INTO finance.fiscal_periods(id,tenant_id,company_id,fiscal_year,starts_on,ends_on,status) VALUES(gen_random_uuid(),'{tenant}','{company}',2026,'2026-02-01','2026-02-28','Open');")
 env['ERP_REVERSAL_PHASE']='reverse'
 browser=spawn('browser',['node','src/Clients/Web/reversal-browser-tests.mjs']);assert browser.wait(timeout=120)==0,'Reversal browser acceptance failed'
 checks={
 'originalUnchanged':before==original_digest(),
 'originalPeriod':sql("SELECT status FROM finance.fiscal_periods WHERE starts_on='2026-01-01'"),
 'reversalPeriod':sql("SELECT status FROM finance.fiscal_periods WHERE starts_on='2026-02-01'"),
 'postedJournals':sql("SELECT count(*) FROM finance.journal_entries WHERE status='Posted'"),
 'links':sql("SELECT count(*) FROM finance.journal_reversals"),
 'reversalRequestedAudit':sql("SELECT count(*) FROM audit.entries WHERE action='JournalReversalRequested'"),
 'reversedAudit':sql("SELECT count(*) FROM audit.entries WHERE action='JournalReversed'"),
 'postedAudit':sql("SELECT count(*) FROM audit.entries WHERE action='JournalPosted'"),
 'reversedOutbox':sql("SELECT count(*) FROM integration.outbox WHERE type='finance.journal-entry-reversed.v1'"),
 'postedOutbox':sql("SELECT count(*) FROM integration.outbox WHERE type='finance.journal-entry-posted.v1'"),
 'reversalReceipts':sql("SELECT count(*) FROM integration.idempotency WHERE command='Finance.ReverseManualJournal' AND state='Completed'"),
 'unbalanced':sql("SELECT count(*) FROM (SELECT journal_id FROM finance.journal_lines GROUP BY journal_id HAVING sum(debit)<>sum(credit)) bad")}
 assert checks['originalUnchanged'] and checks['originalPeriod']=='Closed' and checks['reversalPeriod']=='Open',checks
 assert all(checks[k]=='1' for k in ['links','reversalRequestedAudit','reversedAudit','reversedOutbox','reversalReceipts']),checks
 assert all(checks[k]=='2' for k in ['postedJournals','postedAudit','postedOutbox']) and checks['unbalanced']=='0',checks
 evidence=json.loads((OUT/'browser-results.json').read_text());evidence.update(databaseChecks=checks,status='PASS')
 (OUT/'golden-results.json').write_text(json.dumps(evidence,indent=2,ensure_ascii=False));print(json.dumps({'status':'PASS','browserChecks':len(evidence['checks']),'databaseChecks':checks}))
finally:
 for process in reversed(processes):
  if process.poll() is None:os.killpg(process.pid,signal.SIGTERM)
 for process in processes:
  try:process.wait(timeout=10)
  except subprocess.TimeoutExpired:os.killpg(process.pid,signal.SIGKILL)
 for file in logs:file.close()
 subprocess.run(['docker','rm','--force','--volumes',name],capture_output=True)
