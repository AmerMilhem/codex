#!/usr/bin/env python3
"""Real Angular -> HTTP -> PostgreSQL -> worker acceptance on an owned disposable database.
No fixture database, API response, posting or permission is mocked. Secrets stay out of artifacts.
"""
import json,os,pathlib,secrets,signal,subprocess,time,urllib.request,uuid,tempfile

ROOT=pathlib.Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/journal';OUT.mkdir(parents=True,exist_ok=True)
env=os.environ.copy();processes=[];logs=[]
name='erp-journal-browser-'+uuid.uuid4().hex[:12]
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
  assert process.poll() is None,'Process failed; inspect artifacts/journal logs.'
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
  run(['docker','run','--detach','--name',name,'--label','purpose=erp-journal-browser-test','--env-file',secret_file.name,'--publish','127.0.0.1::5432',image])
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
 INSERT INTO finance.fiscal_periods(id,tenant_id,company_id,fiscal_year,starts_on,ends_on,status) VALUES(gen_random_uuid(),'{tenant}','{company}',2026,'2026-01-01','2026-12-31','Open');
 INSERT INTO finance.accounts(id,tenant_id,company_id,code,name,status,posting_allowed) VALUES
 ('{expense}','{tenant}','{company}','5000','مصروف / Expense','Active',true),('{cash}','{tenant}','{company}','1000','نقد / Cash','Active',true);""")
 env.update(ASPNETCORE_ENVIRONMENT='Development',DOTNET_ENVIRONMENT='Development',ERP_DEV_AUTH_TOKEN=passwords['token'],DevelopmentIdentity__Enabled='true',DevelopmentIdentity__Subject='journal-browser-actor',DevelopmentIdentity__Grants__0__TenantId=tenant,DevelopmentIdentity__Grants__0__CompanyId=company)
 for i,permission in enumerate(['foundation.context.read','Finance.Journal.View','Finance.Journal.Create','Finance.Journal.Edit','Finance.Journal.Post','outbox.dispatch']):env[f'DevelopmentIdentity__Grants__0__Permissions__{i}']=permission
 api=spawn('api',['dotnet',f'src/Hosts/Erp.Api/bin/{configuration}/net10.0/Erp.Api.dll','--urls','http://127.0.0.1:5081']);ready('http://127.0.0.1:5081/health/ready',api)
 worker=spawn('worker',['dotnet',f'src/Hosts/Erp.Worker/bin/{configuration}/net10.0/Erp.Worker.dll'])
 env.update(ERP_WEB_PORT='4209',ERP_TEST_API_ORIGIN='http://127.0.0.1:5081',ERP_UI_ORIGIN='http://127.0.0.1:4209',ERP_E2E_TOKEN=passwords['token'])
 web=spawn('web',['python3','scripts/serve-web.py']);ready('http://127.0.0.1:4209',web)
 browser=spawn('browser',['node','src/Clients/Web/journal-browser-tests.mjs']);assert browser.wait(timeout=120)==0,'Browser acceptance failed; inspect artifacts/journal/browser.log'
 for _ in range(80):
  assert worker.poll() is None,'Worker failed'
  if sql("SELECT count(*) FROM integration.outbox WHERE type='finance.journal-entry-posted.v1' AND state='Completed'")=='1':break
  time.sleep(.25)
 else:raise AssertionError('Finance event was not delivered')
 evidence=json.loads((OUT/'browser-results.json').read_text());journal=evidence['journalId'];uuid.UUID(journal)
 checks={
 'postedJournals':sql("SELECT count(*) FROM finance.journal_entries WHERE status='Posted'"),
 'postedAudit':sql(f"SELECT count(*) FROM audit.entries WHERE entity_id='{journal}' AND action='JournalPosted'"),
 'outbox':sql(f"SELECT count(*) FROM integration.outbox WHERE source_id='{journal}'"),
 'receipt':sql(f"SELECT count(*) FROM integration.idempotency WHERE target='{journal}' AND state='Completed'"),
 'inbox':sql("SELECT count(*) FROM integration.inbox WHERE consumer='finance.posted-trace.v1'"),
 'deliveredTrace':sql(f"SELECT count(*) FROM audit.entries WHERE entity_id='{journal}' AND action='JournalPostedEventDelivered'"),
 'correlationChain':sql(f"SELECT count(*) FROM integration.outbox o JOIN audit.entries p ON p.tenant_id=o.tenant_id AND p.company_id=o.company_id AND p.entity_id=o.source_id AND p.action='JournalPosted' JOIN audit.entries d ON d.tenant_id=o.tenant_id AND d.company_id=o.company_id AND d.entity_id=o.source_id AND d.action='JournalPostedEventDelivered' WHERE o.source_id='{journal}' AND o.correlation_id=p.correlation_id AND o.correlation_id=d.correlation_id AND o.trace_parent IS NOT NULL"),
 'globalUnbalanced':sql("SELECT count(*) FROM (SELECT j.id FROM finance.journal_entries j LEFT JOIN finance.journal_lines l ON l.journal_id=j.id WHERE j.status='Posted' GROUP BY j.id HAVING count(l.id)<2 OR coalesce(sum(l.debit),0)<>coalesce(sum(l.credit),0)) failures"),
 'totals':sql("SELECT sum(debit)::text||' / '||sum(credit)::text FROM finance.journal_lines"),
 }
 assert all(value=='1' for key,value in checks.items() if key not in ['globalUnbalanced','totals']),checks
 assert checks['globalUnbalanced']=='0' and checks['totals']=='100.000000 / 100.000000',checks
 evidence.update(databaseChecks=checks,status='PASS');(OUT/'golden-results.json').write_text(json.dumps(evidence,indent=2,ensure_ascii=False));print(json.dumps({'status':'PASS','browserChecks':len(evidence['checks']),'databaseChecks':checks}))
finally:
 for process in reversed(processes):
  if process.poll() is None:os.killpg(process.pid,signal.SIGTERM)
 for process in processes:
  try:process.wait(timeout=10)
  except subprocess.TimeoutExpired:os.killpg(process.pid,signal.SIGKILL)
 for file in logs:file.close()
 subprocess.run(['docker','rm','--force','--volumes',name],capture_output=True)
