using System.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

partial class P {
 static async Task<(Guid Id,Guid Lease)> Claim(string source){return await Db.Scoped(A,async c=>{var lease=Guid.NewGuid();await using var cmd=Db.Cmd(c,"""
  UPDATE poc.outbox SET lease_id=@p0,lease_until=clock_timestamp()+interval '30 seconds',attempts=attempts+1
  WHERE id=(SELECT id FROM poc.outbox WHERE source=@p1 AND NOT done AND (lease_until IS NULL OR lease_until<clock_timestamp()) FOR UPDATE SKIP LOCKED LIMIT 1)
  RETURNING id
  """,lease,source);var id=await cmd.ExecuteScalarAsync();return(id is Guid g?g:Guid.Empty,lease);});}
 static async Task<bool> Consume(Guid id,string hash){return await Db.Scoped(A,async c=>{var n=await Db.Exec(c,"INSERT INTO poc.inbox VALUES(@p0,@p1,'consumer',@p2,@p3) ON CONFLICT DO NOTHING",A.Tenant,A.Company,id,hash);if(n==0){Check(await Db.Scalar<string>(c,"SELECT payload_hash FROM poc.inbox WHERE event_id=@p0",id)==hash,"inbox.payload_conflict");return false;}await Db.Exec(c,"INSERT INTO poc.consumer_effects VALUES(@p0,@p1,@p2,1)",A.Tenant,A.Company,id);return true;});}
 static async Task OutboxTests(){
  await Run("outbox-real-worker-process-kill-before-and-after-consumer","outbox",async()=>{
   await Post("process-outbox","process-key",10);var exits=new List<int>();
   foreach(var mode in new[]{"worker-crash-before","worker-crash-after"}){
    var child=new Process{StartInfo=new ProcessStartInfo(Environment.ProcessPath!){RedirectStandardOutput=true,RedirectStandardError=true}};child.StartInfo.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);child.StartInfo.ArgumentList.Add(mode);child.Start();var output=await child.StandardOutput.ReadToEndAsync();await child.WaitForExitAsync();Check(child.ExitCode!=0&&output.Contains("DURABLE_STATE_SAVED"),"worker crash not proven");exits.Add(child.ExitCode);
    await Db.Scoped(A,c=>Db.Exec(c,"UPDATE poc.outbox SET lease_until=clock_timestamp()-interval '1 second' WHERE source='process-outbox'"));
   }
   var claim=await Claim("process-outbox");Check(claim.Id!=Guid.Empty,"not recovered");Check(!await Consume(claim.Id,"stable-process-payload"),"duplicate effect after crash");var n=await Db.Scoped(A,c=>Db.Exec(c,"UPDATE poc.outbox SET done=true WHERE id=@p0 AND lease_id=@p1",claim.Id,claim.Lease));Check(n==1,"recovery ack failed");return new{workerProcessExits=exits,claims=3,consumerDeliveries=2,consumerEffects=1,ack=1,clock="lease expiration accelerated explicitly in fixture",delivery="at least once"};
  });
  await Run("outbox-crash-before-send-after-send-lease-fencing","outbox",async()=>{
   await Post("outbox-recovery","key",10);var first=await Claim("outbox-recovery");Check(first.Id!=Guid.Empty,"no claim");var duplicate=await Claim("outbox-recovery");Check(duplicate.Id==Guid.Empty,"duplicate live claim");
   // Durable state after a worker disappears before dispatch. Advance only the disposable lease.
   await Db.Scoped(A,c=>Db.Exec(c,"UPDATE poc.outbox SET lease_until=clock_timestamp()-interval '1 second' WHERE id=@p0",first.Id));
   var second=await Claim("outbox-recovery");Check(second.Id==first.Id&&second.Lease!=first.Lease,"lease recovery failed");Check(await Consume(second.Id,"payload-v1"),"first effect missing");
   // Send succeeded; worker dies before ACK. A new lease must redeliver the same ID.
   await Db.Scoped(A,c=>Db.Exec(c,"UPDATE poc.outbox SET lease_until=clock_timestamp()-interval '1 second' WHERE id=@p0",first.Id));var third=await Claim("outbox-recovery");Check(third.Id==first.Id,"event ID changed");Check(!await Consume(third.Id,"payload-v1"),"duplicate consumer effect");
   var stale=await Db.Scoped(A,c=>Db.Exec(c,"UPDATE poc.outbox SET done=true WHERE id=@p0 AND lease_id=@p1",first.Id,second.Lease));Check(stale==0,"stale worker ack accepted");var ack=await Db.Scoped(A,c=>Db.Exec(c,"UPDATE poc.outbox SET done=true WHERE id=@p0 AND lease_id=@p1",third.Id,third.Lease));Check(ack==1,"current ack missing");
   bool conflict=false;try{await Consume(third.Id,"mutated-payload");}catch(Exception e)when(e.Message=="inbox.payload_conflict"){conflict=true;}Check(conflict,"payload collision accepted");
   return new{claims=3,dispatchAttempts=2,consumerEffects=1,staleAck=stale,ack,delivery="at least once; not exactly once",crashMechanism="lease-owner disappearance modeled by expired durable lease; actual process kill tested separately"};
  });
  await Run("two-workers-skip-locked-one-pickup","outbox",async()=>{await Post("two-workers","key",10);var leases=await Task.WhenAll(Claim("two-workers"),Claim("two-workers"));Check(leases.Count(x=>x.Id!=Guid.Empty)==1,"two workers same active lease");return new{workers=2,claims=1};});
  await Run("consumer-inbox-and-effect-atomic-rollback","outbox",async()=>{var id=Guid.NewGuid();await Expect(()=>Db.Scoped(A,async c=>{await Db.Exec(c,"INSERT INTO poc.inbox VALUES(@p0,@p1,'consumer',@p2,'hash')",A.Tenant,A.Company,id);await Db.Exec(c,"INSERT INTO poc.consumer_effects VALUES(@p0,@p1,@p2,1),(@p0,@p1,@p2,1)",A.Tenant,A.Company,id);return 1;}),"23505");var n=await Db.Scoped(A,c=>Db.Scalar<long>(c,"SELECT count(*) FROM poc.inbox WHERE event_id=@p0",id));Check(n==0,"inbox without effect");return new{inbox=0,effect=0};});
  await Run("database-command-timeout-and-worker-restart","failure",async()=>{await using var c=await Db.Open();await using var tx=await c.BeginTransactionAsync();await Db.SetScope(c,A);await using var cmd=new NpgsqlCommand("SELECT pg_sleep(4)",c){CommandTimeout=1};bool timed=false;try{await cmd.ExecuteNonQueryAsync();}catch(NpgsqlException){timed=true;}Check(timed,"timeout absent");await tx.RollbackAsync();var count=await Db.Scoped(B,x=>Db.Scalar<long>(x,"SELECT count(*) FROM poc.scoped"));Check(count==1,"worker fresh UoW not recovered");return new{timedOut=true,freshWorkerScope="B",recovered=true};});
  await Run("external-timeout-retry-stable-event-id","failure",async()=>{
   var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:5017");await using var app=builder.Build();var seen=new System.Collections.Concurrent.ConcurrentDictionary<string,int>();var sends=0;
   app.MapPost("/sink/{id}",async(string id)=>{Interlocked.Increment(ref sends);seen.TryAdd(id,1);await Task.Delay(250);return ResultsOk();});await app.StartAsync();
   using var client=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromMilliseconds(50)};var id=Guid.NewGuid().ToString();bool timeout=false;try{await client.PostAsync("http://127.0.0.1:5017/sink/"+id,null);}catch(TaskCanceledException){timeout=true;}Check(timeout,"external timeout not injected");await Task.Delay(300);using var retryClient=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(2)};await retryClient.PostAsync("http://127.0.0.1:5017/sink/"+id,null);Check(sends==2&&seen.Count==1,"external idempotency failed");await app.StopAsync();return new{sends,externalEffects=seen.Count,timeout,manualIntervention=false,scope="local idempotent provider stub; not JoFotara/provider proof"};
  });
  await Run("global-ledger-openitem-reconciliation","reconciliation",async()=>{var diff=await Db.Scoped(A,c=>Db.Scalar<decimal>(c,"SELECT coalesce(sum(debit-credit),0) FROM poc.lines"));Check(diff==0,"GL imbalance");var broken=await Db.Scoped(A,c=>Db.Scalar<long>(c,"SELECT count(*) FROM poc.open_items o WHERE original<coalesce((SELECT sum(amount) FROM poc.allocations a WHERE a.item_id=o.id),0)"));Check(broken==0,"open item overallocated");return new{glDifference=diff,overallocatedItems=broken};});
 }
 static IResult ResultsOk()=>Microsoft.AspNetCore.Http.Results.Ok();
 static async Task Migration(){
  await Run("expand-dual-compatible-backfill-contract","migration",async()=>{
   await using var admin=await Db.Open(true);await Db.Exec(admin,"INSERT INTO poc.migration_demo VALUES(1,@p0,@p1,'old-one'),(2,@p0,@p1,'old-two')",A.Tenant,A.Company);
   var sw=Stopwatch.StartNew();await Db.Exec(admin,"""
    SET lock_timeout='2s';
    ALTER TABLE poc.migration_demo ADD COLUMN new_name text;
    CREATE FUNCTION poc.migration_sync() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
     IF TG_OP='INSERT' THEN NEW.new_name:=coalesce(NEW.new_name,NEW.old_name);NEW.old_name:=coalesce(NEW.old_name,NEW.new_name);
     ELSIF NEW.old_name IS DISTINCT FROM OLD.old_name THEN NEW.new_name:=NEW.old_name;
     ELSIF NEW.new_name IS DISTINCT FROM OLD.new_name THEN NEW.old_name:=NEW.new_name; END IF;RETURN NEW;END $$;
    CREATE TRIGGER dual_names BEFORE INSERT OR UPDATE ON poc.migration_demo FOR EACH ROW EXECUTE FUNCTION poc.migration_sync();
    """
   );var expandMs=sw.Elapsed.TotalMilliseconds;
   await Db.Scoped(A,c=>Db.Exec(c,"INSERT INTO poc.migration_demo(id,tenant_id,company_id,old_name) VALUES(3,@p0,@p1,'written-by-N')",A.Tenant,A.Company));
   Check(await Db.Scoped(A,c=>Db.Scalar<string>(c,"SELECT coalesce(new_name,old_name) FROM poc.migration_demo WHERE id=3"))=="written-by-N","N+1 cannot read N write");
   await Db.Scoped(A,c=>Db.Exec(c,"UPDATE poc.migration_demo SET new_name='written-by-N-plus-1' WHERE id=3"));Check(await Db.Scoped(A,c=>Db.Scalar<string>(c,"SELECT old_name FROM poc.migration_demo WHERE id=3"))=="written-by-N-plus-1","N cannot read N+1 write");
   await Db.Exec(admin,"UPDATE poc.migration_demo SET new_name=old_name WHERE id IN(SELECT id FROM poc.migration_demo WHERE new_name IS NULL ORDER BY id LIMIT 1)");
   await Db.Scoped(A,c=>Db.Exec(c,"UPDATE poc.migration_demo SET old_name='N-race' WHERE id=1"));
   while(await Db.Exec(admin,"UPDATE poc.migration_demo SET new_name=old_name WHERE id IN(SELECT id FROM poc.migration_demo WHERE new_name IS NULL ORDER BY id LIMIT 1)")>0){}
   Check(await Db.Scalar<long>(admin,"SELECT count(*) FROM poc.migration_demo WHERE new_name IS DISTINCT FROM old_name")==0,"backfill mismatch");
   await Db.Exec(admin,"ALTER TABLE poc.migration_demo ADD CONSTRAINT new_name_present CHECK(new_name IS NOT NULL) NOT VALID;ALTER TABLE poc.migration_demo VALIDATE CONSTRAINT new_name_present;ALTER TABLE poc.migration_demo ALTER COLUMN new_name SET NOT NULL");
   // Contract is explicitly after old application writers are retired, never during the mixed-version window.
   await Db.Exec(admin,"DROP TRIGGER dual_names ON poc.migration_demo;ALTER TABLE poc.migration_demo DROP COLUMN old_name");Check(await Db.Scoped(A,c=>Db.Scalar<string>(c,"SELECT new_name FROM poc.migration_demo WHERE id=1"))=="N-race","contract lost data");
   return new{rows=3,expandMs,consecutiveVersionsCompatible=true,backfillMismatches=0,contract="only after N writers retired",limitation="small local fixture; no fleet-scale lock/replication proof"};
  });
 }
 static async Task Containers(){await Run("testcontainers-postgresql18-runtime-smoke","compatibility",async()=>{await using var db=new PostgreSqlBuilder("postgres:18.2@sha256:a688ae5c99f86c1424e046ace0de890608cc0ddb5ec8ef72a6ff8305fdc8f4d2").WithDatabase("poc_probe").WithUsername("postgres").WithPassword("disposable-poc-only").WithCleanUp(false).Build();await db.StartAsync();await using var c=await Db.Open(cs:db.GetConnectionString());var version=await Db.Scalar<string>(c,"SHOW server_version");Check(version.StartsWith("18.2"),"wrong PG");Check(await Db.Scalar<int>(c,"SELECT 1+1")==2,"functional probe");return new{version,testcontainers=typeof(PostgreSqlBuilder).Assembly.GetName().Version?.ToString(),image="postgres:18.2@sha256:a688ae5c99f86c1424e046ace0de890608cc0ddb5ec8ef72a6ff8305fdc8f4d2"};});}
}
