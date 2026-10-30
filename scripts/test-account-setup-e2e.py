#!/usr/bin/env python3
"""Real Angular/HTTP/PostgreSQL account-creation acceptance on a disposable database."""
import json, os, pathlib, secrets, signal, socket, subprocess, tempfile, time, urllib.request, uuid

ROOT=pathlib.Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/account-setup'; OUT.mkdir(parents=True,exist_ok=True)
env=os.environ.copy(); processes=[]; logs=[]
name='erp-account-setup-browser-'+uuid.uuid4().hex[:12]
passwords={key:secrets.token_hex(24) for key in ['admin','migration','runtime','token']}
tenant='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'; company='11111111-1111-4111-8111-111111111111'
image='postgres:18.2@sha256:a688ae5c99f86c1424e046ace0de890608cc0ddb5ec8ef72a6ff8305fdc8f4d2'
def run(args,**kwargs): return subprocess.run(args,cwd=ROOT,env=env,check=True,capture_output=True,**kwargs)
def sql(query): return run(['docker','exec','-i',name,'psql','-XAt','-v','ON_ERROR_STOP=1','-U','postgres','-d','journal_browser'],input=query.encode()).stdout.decode().strip()
def spawn(label,args,process_env=None):
    handle=(OUT/(label+'.log')).open('w'); logs.append(handle)
    process=subprocess.Popen(args,cwd=ROOT,env=process_env or env,stdout=handle,stderr=subprocess.STDOUT,start_new_session=True)
    processes.append(process); return process
def ready(url,process):
    for _ in range(120):
        assert process.poll() is None,'Process exited; inspect artifacts/account-setup logs.'
        try:
            with urllib.request.urlopen(url,timeout=1) as response:
                if response.status==200:return
        except OSError:time.sleep(.25)
    raise AssertionError('Readiness timed out: '+url)
try:
    for port in [5085,5086,4212]:
        with socket.socket() as probe:
            probe.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1); probe.bind(('127.0.0.1',port))
    with tempfile.NamedTemporaryFile(mode='w',dir='/tmp') as secret_file:
        secret_file.write('POSTGRES_PASSWORD='+passwords['admin']+'\nPOSTGRES_DB=journal_browser\n'); secret_file.flush()
        run(['docker','run','--detach','--name',name,'--label','purpose=erp-account-setup-browser-test','--env-file',secret_file.name,'--publish','127.0.0.1::5432',image])
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
    sql(f"INSERT INTO finance.accounting_books(tenant_id,company_id,currency,minor_units) VALUES('{tenant}','{company}','JOD',3);")
    env.update(ASPNETCORE_ENVIRONMENT='Development',DOTNET_ENVIRONMENT='Development',ERP_DEV_AUTH_TOKEN=passwords['token'],DevelopmentIdentity__Enabled='true',DevelopmentIdentity__Subject='account-browser-actor',DevelopmentIdentity__Grants__0__TenantId=tenant,DevelopmentIdentity__Grants__0__CompanyId=company)
    for index,permission in enumerate(['foundation.context.read','Finance.Journal.View','Finance.Journal.Create','Finance.Journal.Edit','Finance.Journal.Post','Finance.Journal.Reverse','Finance.Period.View','Finance.Period.Create','Finance.Period.Close','Finance.Account.View','Finance.Account.Create','outbox.dispatch']):env[f'DevelopmentIdentity__Grants__0__Permissions__{index}']=permission
    api=spawn('api',['dotnet',f'src/Hosts/Erp.Api/bin/{configuration}/net10.0/Erp.Api.dll','--urls','http://127.0.0.1:5085']); ready('http://127.0.0.1:5085/health/ready',api)
    limited_env={key:value for key,value in env.items() if not key.startswith('DevelopmentIdentity__Grants__0__Permissions__')}
    for index,permission in enumerate(['foundation.context.read','Finance.Account.View','Finance.Journal.Post']):limited_env[f'DevelopmentIdentity__Grants__0__Permissions__{index}']=permission
    limited=spawn('limited-api',['dotnet',f'src/Hosts/Erp.Api/bin/{configuration}/net10.0/Erp.Api.dll','--urls','http://127.0.0.1:5086'],limited_env); ready('http://127.0.0.1:5086/health/ready',limited)
    env.update(ERP_WEB_PORT='4212',ERP_TEST_API_ORIGIN='http://127.0.0.1:5085',ERP_UI_ORIGIN='http://127.0.0.1:4212',ERP_E2E_TOKEN=passwords['token'],ERP_LIMITED_API_ORIGIN='http://127.0.0.1:5086')
    web=spawn('web',['python3','scripts/serve-web.py']); ready('http://127.0.0.1:4212',web)
    browser=spawn('browser',['node','src/Clients/Web/account-setup-browser-tests.mjs']); assert browser.wait(timeout=120)==0,'Browser acceptance failed; inspect artifacts/account-setup/browser.log'
    result=json.loads((OUT/'browser-results.json').read_text())
    period=result['periodId']; journal=result['journalId']; expense=result['expenseId']; cash=result['cashId']; header=result['headerId']
    checks={
      'periodState':sql(f"SELECT status||':'||revision FROM finance.fiscal_periods WHERE id='{period}'"),
      'accountRows':sql("SELECT count(*) FROM finance.accounts"),
      'createRequestedAudit':sql("SELECT count(*) FROM audit.entries WHERE action='AccountCreateRequested'"),
      'createdAudit':sql("SELECT count(*) FROM audit.entries WHERE action='AccountCreated'"),
      'createdOutbox':sql("SELECT count(*) FROM integration.outbox WHERE type='finance.account-created.v1'"),
      'createReceipt':sql("SELECT count(*) FROM integration.idempotency WHERE command='Finance.CreateAccount' AND state='Completed'"),
      'postedJournals':sql("SELECT count(*) FROM finance.journal_entries WHERE status='Posted'"),
      'newAccountJournal':sql(f"SELECT count(*) FROM finance.journal_entries j JOIN finance.journal_lines l ON l.journal_id=j.id WHERE j.id='{journal}' AND j.status='Posted' AND l.account_id IN ('{expense}','{cash}')"),
      'nonpostable':sql(f"SELECT NOT posting_allowed FROM finance.accounts WHERE id='{header}'"),
      'unbalanced':sql("SELECT count(*) FROM (SELECT journal_id FROM finance.journal_lines GROUP BY journal_id HAVING sum(debit)<>sum(credit)) bad"),
      'rlsForced':sql("SELECT relforcerowsecurity FROM pg_class WHERE oid='finance.accounts'::regclass"),
    }
    assert checks['periodState']=='Open:1' and checks['rlsForced']=='t' and checks['unbalanced']=='0',checks
    assert all(checks[key]=='3' for key in ['accountRows','createRequestedAudit','createdAudit','createdOutbox','createReceipt']),checks
    assert checks['postedJournals']=='1' and checks['newAccountJournal']=='2' and checks['nonpostable']=='t',checks
    result.update(databaseChecks=checks,status='PASS')
    (OUT/'golden-results.json').write_text(json.dumps(result,indent=2,ensure_ascii=False))
    print(json.dumps({'status':'PASS','browserChecks':len(result['checks']),'databaseChecks':checks}))
finally:
    for process in reversed(processes):
        if process.poll() is None:os.killpg(process.pid,signal.SIGTERM)
    for process in processes:
        try:process.wait(timeout=10)
        except subprocess.TimeoutExpired:os.killpg(process.pid,signal.SIGKILL)
    for handle in logs:handle.close()
    subprocess.run(['docker','rm','--force','--volumes',name],capture_output=True)
