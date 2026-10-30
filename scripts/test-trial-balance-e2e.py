#!/usr/bin/env python3
"""Real Angular -> HTTP -> PostgreSQL acceptance on an owned disposable database.
No fixture database, API response, posting or permission is mocked. Secrets stay out of artifacts.
"""
import json,os,pathlib,secrets,signal,subprocess,time,urllib.request,uuid,tempfile
from period_close_client import close_period

ROOT=pathlib.Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/trial-balance';OUT.mkdir(parents=True,exist_ok=True)
env=os.environ.copy();processes=[];logs=[]
name='erp-trial-balance-browser-'+uuid.uuid4().hex[:12]
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
  assert process.poll() is None,'Process failed; inspect artifacts/trial-balance logs.'
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
  run(['docker','run','--detach','--name',name,'--label','purpose=erp-trial-balance-browser-test','--env-file',secret_file.name,'--publish','127.0.0.1::5432',image])
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
 env.update(ASPNETCORE_ENVIRONMENT='Development',DOTNET_ENVIRONMENT='Development',ERP_DEV_AUTH_TOKEN=passwords['token'],DevelopmentIdentity__Enabled='true',DevelopmentIdentity__Subject='trial-balance-browser-actor',DevelopmentIdentity__Grants__0__TenantId=tenant,DevelopmentIdentity__Grants__0__CompanyId=company)
 for i,permission in enumerate(['foundation.context.read','Finance.Journal.View','Finance.Journal.Create','Finance.Journal.Edit','Finance.Journal.Post','Finance.Period.View','Finance.Period.Close','outbox.dispatch']):env[f'DevelopmentIdentity__Grants__0__Permissions__{i}']=permission
 api=spawn('api',['dotnet',f'src/Hosts/Erp.Api/bin/{configuration}/net10.0/Erp.Api.dll','--urls','http://127.0.0.1:5081']);ready('http://127.0.0.1:5081/health/ready',api)
 sql(f"""INSERT INTO finance.accounts(id,tenant_id,company_id,code,name,status,posting_allowed) VALUES('77777777-7777-4777-8777-777777777777','{tenant}','{company}','4000','إيراد / Revenue','Active',true);
 INSERT INTO finance.fiscal_periods(id,tenant_id,company_id,fiscal_year,starts_on,ends_on,status) VALUES
 (gen_random_uuid(),'{tenant}','{company}',2025,'2025-01-01','2025-12-31','Open'),
 (gen_random_uuid(),'{tenant}','{company}',2026,'2026-02-01','2026-02-28','Open');""")
 env.update(ERP_WEB_PORT='4209',ERP_TEST_API_ORIGIN='http://127.0.0.1:5081',ERP_UI_ORIGIN='http://127.0.0.1:4209',ERP_E2E_TOKEN=passwords['token'])
 web=spawn('web',['python3','scripts/serve-web.py']);ready('http://127.0.0.1:4209',web)
 browser=spawn('browser',['node','src/Clients/Web/trial-balance-browser-tests.mjs']);assert browser.wait(timeout=120)==0,'Browser acceptance failed; inspect artifacts/trial-balance/browser.log'
 close_period('http://127.0.0.1:5081',passwords['token'],tenant,company,sql("SELECT id FROM finance.fiscal_periods WHERE starts_on='2026-01-01'"))
 def state_digest():
  parts=[f"SELECT to_jsonb(t)::text AS value FROM {table} t" for table in ['finance.journal_entries','finance.journal_lines','finance.accounts','finance.accounting_books','finance.fiscal_periods','finance.journal_numbers','finance.journal_reversals','audit.entries','integration.outbox','integration.idempotency','integration.inbox']]
  return sql("SELECT md5(coalesce(string_agg(value,'|' ORDER BY value),'')) FROM ("+' UNION ALL '.join(parts)+") state")
 before=state_digest();env['ERP_TRIAL_RECHECK_ONLY']='true'
 refresh=spawn('browser-closed-period',['node','src/Clients/Web/trial-balance-browser-tests.mjs']);assert refresh.wait(timeout=90)==0,'Closed-period read-only browser acceptance failed'
 after=state_digest();assert before==after,'Report mutated persisted state'
 evidence=json.loads((OUT/'browser-results.json').read_text())
 checks={
  'postedJournals':sql("SELECT count(*) FROM finance.journal_entries WHERE status='Posted'"),
  'draftJournals':sql("SELECT count(*) FROM finance.journal_entries WHERE status='Draft'"),
  'postingOutboxRows':sql("SELECT count(*) FROM integration.outbox WHERE type='finance.journal-entry-posted.v1'"),
  'postingReceipts':sql("SELECT count(*) FROM integration.idempotency WHERE state='Completed' AND command='Finance.PostManualJournal'"),
  'periodDebit':sql("SELECT sum(l.debit)::text FROM finance.journal_entries j JOIN finance.journal_lines l ON l.journal_id=j.id WHERE j.status='Posted' AND posting_date BETWEEN '2026-01-01' AND '2026-01-31'"),
  'periodCredit':sql("SELECT sum(l.credit)::text FROM finance.journal_entries j JOIN finance.journal_lines l ON l.journal_id=j.id WHERE j.status='Posted' AND posting_date BETWEEN '2026-01-01' AND '2026-01-31'"),
  'globalUnbalanced':sql("SELECT count(*) FROM (SELECT j.id FROM finance.journal_entries j JOIN finance.journal_lines l ON l.journal_id=j.id WHERE j.status='Posted' GROUP BY j.id HAVING sum(l.debit)<>sum(l.credit)) failures"),
  'readOnlyStateUnchanged':before==after,
 }
 assert checks['postedJournals']==checks['postingOutboxRows']==checks['postingReceipts']=='4' and checks['draftJournals']=='1',checks
 assert checks['periodDebit']==checks['periodCredit']=='140.000000' and checks['globalUnbalanced']=='0',checks
 evidence.update(databaseChecks=checks,status='PASS');(OUT/'golden-results.json').write_text(json.dumps(evidence,indent=2,ensure_ascii=False));print(json.dumps({'status':'PASS','browserChecks':len(evidence['checks']),'databaseChecks':checks}))
finally:
 for process in reversed(processes):
  if process.poll() is None:os.killpg(process.pid,signal.SIGTERM)
 for process in processes:
  try:process.wait(timeout=10)
  except subprocess.TimeoutExpired:os.killpg(process.pid,signal.SIGKILL)
 for file in logs:file.close()
 subprocess.run(['docker','rm','--force','--volumes',name],capture_output=True)
