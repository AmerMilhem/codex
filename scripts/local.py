#!/usr/bin/env python3
"""Prepare only this named local development database; preserve existing secrets and data."""
import json,os,pathlib,secrets,subprocess,sys,time
ROOT=pathlib.Path(__file__).resolve().parents[1];local=ROOT/'.local';local.mkdir(exist_ok=True,mode=0o700)
config=local/'settings.json'
if not config.exists():
 values={k:secrets.token_hex(24) for k in ['admin','migration','runtime','token']}
 config.write_text(json.dumps(values));config.chmod(0o600)
values=json.loads(config.read_text());env=os.environ.copy();env['ERP_LOCAL_ADMIN_PASSWORD']=values['admin']
def run(args,**kw):return subprocess.run(args,cwd=ROOT,env=env,check=True,capture_output=True,**kw)
run(['docker','compose','-f','ops/local/compose.yaml','up','-d','--wait'])
label=run(['docker','inspect','--format','{{index .Config.Labels "purpose"}}','erp-foundation-dev']).stdout.strip()
assert label==b'erp-foundation-development','Unexpected database owner; do not modify'
def sql(text):return run(['docker','exec','-i','erp-foundation-dev','psql','-X','-v','ON_ERROR_STOP=1','-U','postgres','-d','erp_foundation'],input=text.encode())
# Passwords are locally generated hex, not raw user input; never put them in the command line or logs.
sql(f"""DO $$ BEGIN
IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='erp_migrator') THEN
CREATE ROLE erp_migrator LOGIN PASSWORD '{values['migration']}' NOSUPERUSER NOBYPASSRLS; END IF;
IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='erp_runtime') THEN
CREATE ROLE erp_runtime LOGIN PASSWORD '{values['runtime']}' NOSUPERUSER NOBYPASSRLS; END IF;
END $$;
ALTER DATABASE erp_foundation OWNER TO erp_migrator;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
""")
env['ERP_RUNTIME_CONNECTION']=f"Host=127.0.0.1;Port=55433;Database=erp_foundation;Username=erp_runtime;Password={values['runtime']};Maximum Pool Size=20"
env['ERP_MIGRATION_CONNECTION']=f"Host=127.0.0.1;Port=55433;Database=erp_foundation;Username=erp_migrator;Password={values['migration']}"
env['ERP_DEV_AUTH_TOKEN']=values['token'];env['ASPNETCORE_ENVIRONMENT']='Development';env['DOTNET_ENVIRONMENT']='Development'
env['DevelopmentIdentity__Enabled']='true'
env['DevelopmentIdentity__Grants__0__TenantId']='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
env['DevelopmentIdentity__Grants__0__CompanyId']='11111111-1111-4111-8111-111111111111'
env['DevelopmentIdentity__Grants__0__Permissions__0']='foundation.context.read'
env['DevelopmentIdentity__Grants__0__Permissions__1']='outbox.dispatch'
for i,permission in enumerate(['Finance.Journal.View','Finance.Journal.Create','Finance.Journal.Edit','Finance.Journal.Post','Finance.Journal.Reverse','Finance.Journal.Correct','Finance.Period.View','Finance.Period.Create','Finance.Period.Close','Finance.Account.View','Finance.Account.Create','Finance.Book.View','Finance.Book.Create'],start=2):
 env[f'DevelopmentIdentity__Grants__0__Permissions__{i}']=permission
run(['dotnet','run','--project','src/Hosts/Erp.Migrations','--no-build'])
if len(sys.argv)>1 and sys.argv[1]=='finance-demo':
 print('Create the Accounting Book, Fiscal Period and accounts from their Web setup screens after starting API/Web.')
if len(sys.argv)>1 and sys.argv[1] in ['api','worker']:
 env.pop('ERP_MIGRATION_CONNECTION') # runtime hosts never receive the migration credential
 target='Erp.Api' if sys.argv[1]=='api' else 'Erp.Worker'
 args=['dotnet','run','--project','src/Hosts/'+target,'--no-build']
 if sys.argv[1]=='api':args+=['--','--urls','http://127.0.0.1:5080']
 # No secrets in arguments/output. Foreground process; Ctrl+C terminates only the selected host.
 subprocess.run(args,cwd=ROOT,env=env,check=True)
else:print('Local PostgreSQL and migrations ready. Credentials retained only in ignored .local/settings.json.')
