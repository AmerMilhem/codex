#!/usr/bin/env python3
import hashlib,json,pathlib,shutil,socket,subprocess,time
ROOT=pathlib.Path(__file__).resolve().parents[1];E=ROOT/'evidence';E.mkdir(exist_ok=True)
def docker(*args,**kw):return subprocess.run(['docker',*args],check=True,capture_output=True,**kw)
def sql(db,q):return docker('exec','erp-poc-v1-pg','psql','-XAt','-v','ON_ERROR_STOP=1','-U','postgres','-d',db,'-c',q).stdout.decode().strip()
assert docker('inspect','--format','{{index .Config.Labels "purpose"}}','erp-poc-v1-pg').stdout.strip()==b'erp-throwaway-poc'
tables=sql('poc',"SELECT tablename FROM pg_tables WHERE schemaname='poc' ORDER BY tablename").splitlines()
def state(db):
 return {t:sql(db,f"SELECT count(*)||':'||md5(coalesce(string_agg(row_to_json(x)::text,'|' ORDER BY row_to_json(x)::text),'')) FROM poc.{t} x") for t in tables}
before=state('poc');start=time.monotonic()
dump=docker('exec','erp-poc-v1-pg','pg_dump','-U','postgres','-d','poc','-Fc','--no-owner').stdout
(E/'poc.dump').write_bytes(dump);backup_seconds=time.monotonic()-start
sql('postgres','DROP DATABASE IF EXISTS poc_restore WITH (FORCE)');sql('postgres','CREATE DATABASE poc_restore')
start=time.monotonic();docker('exec','-i','erp-poc-v1-pg','pg_restore','-U','postgres','-d','poc_restore','--exit-on-error','--no-owner',input=dump)
after=state('poc_restore');assert before==after,(before,after)
gl=sql('poc_restore','SELECT coalesce(sum(debit-credit),0) FROM poc.lines');assert float(gl)==0
stock=sql('poc_restore',"SELECT count(*) FROM poc.stock s WHERE s.on_hand<>(SELECT coalesce(sum(qty),0) FROM poc.stock_ledger l WHERE l.product=s.product AND l.tenant_id=s.tenant_id AND l.company_id=s.company_id)")
cost_stock=sql('poc_restore',"SELECT count(*) FROM poc.stock s WHERE s.on_hand<>(SELECT coalesce(sum(qty),0) FROM poc.stock_ledger l WHERE l.product=s.product AND l.tenant_id=s.tenant_id AND l.company_id=s.company_id) OR s.value<>(SELECT coalesce(sum(v.delta),0) FROM poc.valuation_ledger v JOIN poc.stock_ledger l ON l.id=v.movement_id WHERE l.product=s.product AND l.tenant_id=s.tenant_id AND l.company_id=s.company_id)")
assert int(cost_stock)==0 and int(stock)==0
restore_seconds=time.monotonic()-start
source=E/'object-fixture';backup=E/'object-backup';restored=E/'object-restored'
source.mkdir(exist_ok=True);(source/'evidence.bin').write_bytes(b'synthetic private evidence object v1')
shutil.copytree(source,backup,dirs_exist_ok=True);expected=hashlib.sha256((source/'evidence.bin').read_bytes()).hexdigest();shutil.rmtree(source);shutil.copytree(backup,restored,dirs_exist_ok=True);assert hashlib.sha256((restored/'evidence.bin').read_bytes()).hexdigest()==expected
# Real local database outage and restart, scoped only to this named disposable service.
docker('stop','--time','1','erp-poc-v1-pg');unavailable=False
try:
 with socket.create_connection(('127.0.0.1',55432),timeout=1):pass
except OSError:unavailable=True
assert unavailable
start=time.monotonic();docker('start','erp-poc-v1-pg')
for _ in range(50):
 try:sql('poc','SELECT 1');break
 except subprocess.CalledProcessError:time.sleep(.2)
assert state('poc')==before
result={'utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'status':'PASS','database':'isolated poc_restore, original poc preserved','dumpSha256':hashlib.sha256(dump).hexdigest(),'tables':len(tables),'before':before,'restored':after,'backupSeconds':backup_seconds,'restoreAndCheckSeconds':restore_seconds,'glDifference':gl,'valuationMismatches':int(cost_stock),'stockMismatches':int(stock),'objectHash':expected,'databaseOutageObserved':unavailable,'restartAndVerifySeconds':time.monotonic()-start,'limitations':['logical local point-in-time snapshot only, no PITR or cloud RPO/RTO proof','roles reused within same local cluster; independent role/identity restore pending','object is synthetic filesystem fixture, not managed storage versions/keys','KMS/secret recovery, identity configuration, provider accepted-outcome reconciliation pending']}
(E/'backup-restore.json').write_text(json.dumps(result,indent=2));print(json.dumps({k:v for k,v in result.items() if k not in ('before','restored')},indent=2))
