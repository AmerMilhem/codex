#!/usr/bin/env python3
"""Validate documented startup using only owned local service processes; always stop those processes."""
import json,os,pathlib,signal,subprocess,time,urllib.request,uuid
root=pathlib.Path(__file__).resolve().parents[1];out=root/'artifacts/local';out.mkdir(parents=True,exist_ok=True)
processes=[];files=[]
def spawn(kind):
 f=(out/(kind+'.log')).open('w');files.append(f)
 p=subprocess.Popen(['python3','scripts/local.py',kind],cwd=root,stdout=f,stderr=subprocess.STDOUT,start_new_session=True);processes.append(p);return p
def sql(q):
 return subprocess.check_output(['docker','exec','-i','erp-foundation-dev','psql','-XAt','-v','ON_ERROR_STOP=1','-U','postgres','-d','erp_foundation'],input=q.encode()).decode().strip()
try:
 api=spawn('api')
 for _ in range(90):
  assert api.poll() is None,'API startup failed; inspect artifacts/local/api.log'
  try:
   with urllib.request.urlopen('http://127.0.0.1:5080/health/ready',timeout=1) as response:
    if response.status==200:break
  except OSError:time.sleep(.3)
 else:raise AssertionError('API readiness timed out')
 result=json.loads(subprocess.check_output(['python3','scripts/smoke-local.py'],cwd=root,text=True))
 event=str(uuid.uuid4())
 sql(f"INSERT INTO integration.outbox(id,tenant_id,company_id,type,source_id,payload,occurred_at,correlation_id,state,available_at,attempts) VALUES('{event}','aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa','11111111-1111-4111-8111-111111111111','smoke.unknown.v1','local-startup-smoke','{{}}',now(),'local-foundation-smoke','Pending',now(),0)")
 worker=spawn('worker')
 for _ in range(90):
  assert worker.poll() is None,'Worker startup failed; inspect artifacts/local/worker.log'
  state=sql(f"SELECT state||':'||coalesce(failure_code,'') FROM integration.outbox WHERE id='{event}'")
  if state=='Failed:HANDLER_REJECTED':break
  time.sleep(.3)
 else:raise AssertionError('Worker did not process scoped probe')
 result.update(workerUnknownEvent=state,localMigrations='applied',status='PASS')
 (out/'startup-results.json').write_text(json.dumps(result,indent=2));print(json.dumps(result))
finally:
 for p in reversed(processes):
  if p.poll() is None:os.killpg(p.pid,signal.SIGTERM)
 for p in processes:
  try:p.wait(timeout=10)
  except subprocess.TimeoutExpired:os.killpg(p.pid,signal.SIGKILL)
 for f in files:f.close()
