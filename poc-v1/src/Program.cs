using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;

partial class P {
 public static readonly Scope A=new(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),Guid.Parse("11111111-1111-4111-8111-111111111111"));
 public static readonly Scope A2=new(A.Tenant,Guid.Parse("22222222-2222-4222-8222-222222222222"));
 public static readonly Scope B=new(Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),Guid.Parse("33333333-3333-4333-8333-333333333333"));
 static readonly List<object> Results=[];
 static readonly string Evidence=Environment.GetEnvironmentVariable("POC_EVIDENCE")??"evidence";
 static void Check(bool value,string why){if(!value)throw new Exception(why);}
 static async Task<object?> Run(string name,string category,Func<Task<object?>> f){var sw=Stopwatch.StartNew();try{var detail=await f();Results.Add(new{name,category,status="PASS",milliseconds=sw.Elapsed.TotalMilliseconds,detail});Console.WriteLine($"PASS {name}");return detail;}catch(Exception e){Results.Add(new{name,category,status="FAIL",milliseconds=sw.Elapsed.TotalMilliseconds,error=e.ToString()});Console.WriteLine($"FAIL {name}: {e.Message}");return null;}}
 static async Task Expect(Func<Task> f,params string[] codes){try{await f();}catch(PostgresException e)when(codes.Contains(e.SqlState)){return;}catch(DbUpdateException e)when(e.InnerException is PostgresException p&&codes.Contains(p.SqlState)){return;}throw new Exception("Expected failure "+string.Join(',',codes));}
 static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
 public static async Task<int> Main(string[] args){
  Directory.CreateDirectory(Evidence);
  if(args.FirstOrDefault()=="crash-post"){await Post("crash-source","crash-key",100m);Console.WriteLine("COMMITTED_BEFORE_PROCESS_KILL");Console.Out.Flush();Process.GetCurrentProcess().Kill();return 137;}
  if(args.FirstOrDefault() is "worker-crash-before" or "worker-crash-after"){
   var claim=await Claim("process-outbox");Check(claim.Id!=Guid.Empty,"worker did not claim");
   if(args[0]=="worker-crash-after")await Consume(claim.Id,"stable-process-payload");
   Console.WriteLine(args[0]+" DURABLE_STATE_SAVED");Console.Out.Flush();Process.GetCurrentProcess().Kill();return 137;
  }
  await Db.Seed();
  await Security();await Transactions();await Reservations();await Idempotency();await DecimalTests();await Costing();await Production();await OutboxTests();await Migration();await Identity();await CompatibilityExtras();await Containers();
  var json=JsonSerializer.Serialize(new{runId=Guid.NewGuid(),utc=DateTimeOffset.UtcNow,framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,results=Results},new JsonSerializerOptions{WriteIndented=true});
  await File.WriteAllTextAsync(Path.Combine(Evidence,"results.json"),json);
  var fail=Results.Count(x=>JsonSerializer.Serialize(x).Contains("\"status\":\"FAIL\""));Console.WriteLine($"Executed {Results.Count}; Failed {fail}");return fail==0?0:1;
 }
 static async Task<Guid> Post(string source,string key,decimal amount,bool broken=false,int period=1,string credit="Revenue",bool deny=false,Scope? scope=null){
  if(deny)throw new UnauthorizedAccessException("invoice.finalize denied before replay");
  var s=scope??A;var fp=Hash($"{source}|{amount}|v1|original-if-match=1");var id=Guid.CreateVersion7();
  await using var c=await Db.Open();await using var tx=await c.BeginTransactionAsync();await Db.SetScope(c,s);
  // Unique key claim blocks concurrent contenders until the winner commits/rolls back.
  var claimed=await Db.Exec(c,"INSERT INTO poc.idempotency VALUES(@p0,@p1,'actor','FinalizeInvoice',@p2,@p3,@p4,@p5) ON CONFLICT DO NOTHING",s.Tenant,s.Company,source,key,fp,id);
  if(claimed==0){await using var cmd=Db.Cmd(c,"SELECT fingerprint,result_id FROM poc.idempotency WHERE target=@p0 AND key=@p1 AND caller='actor' AND command='FinalizeInvoice'",source,key);await using var rd=await cmd.ExecuteReaderAsync();Check(await rd.ReadAsync(),"receipt missing");Check(rd.GetString(0)==fp,"idempotency.key_conflict");return rd.GetGuid(1);}
  var closed=await Db.Scalar<bool>(c,"SELECT closed FROM poc.periods WHERE id=@p0 FOR SHARE",period);Check(!closed,"period.closed");
  foreach(var account in new[]{"AR",credit})Check(await Db.Scalar<long>(c,"SELECT count(*) FROM poc.accounts WHERE id=@p0 AND active",account)==1,"account.invalid_or_inactive");
  await using var business=new BusinessDb(new DbContextOptionsBuilder<BusinessDb>().UseNpgsql(c).Options);
  await using var finance=new FinanceDb(new DbContextOptionsBuilder<FinanceDb>().UseNpgsql(c).Options);
  await using var integration=new IntegrationDb(new DbContextOptionsBuilder<IntegrationDb>().UseNpgsql(c).Options);
  await business.Database.UseTransactionAsync(tx);await finance.Database.UseTransactionAsync(tx);await integration.Database.UseTransactionAsync(tx);
  business.Documents.Add(new(){id=id,tenant_id=s.Tenant,company_id=s.Company,source=source,amount=amount});await business.SaveChangesAsync();
  finance.Journals.Add(new(){id=id,tenant_id=s.Tenant,company_id=s.Company,source=source,period_id=period});
  finance.Lines.AddRange(new Line{id=Guid.CreateVersion7(),tenant_id=s.Tenant,company_id=s.Company,journal_id=id,account="AR",debit=amount},new Line{id=Guid.CreateVersion7(),tenant_id=s.Tenant,company_id=s.Company,journal_id=id,account=credit,credit=broken?amount-1:amount});await finance.SaveChangesAsync();
  await Db.Exec(c,"INSERT INTO poc.open_items VALUES(@p0,@p1,@p2,@p3)",id,s.Tenant,s.Company,amount);
  await Db.Exec(c,"INSERT INTO poc.audit VALUES(@p0,@p1,@p2,@p3,'actor','InvoicePosted','poc-fixed-rule-v1')",Guid.CreateVersion7(),s.Tenant,s.Company,source);
  integration.Events.Add(new(){id=Guid.CreateVersion7(),tenant_id=s.Tenant,company_id=s.Company,source=source});await integration.SaveChangesAsync();
  await tx.CommitAsync();return id;
 }
 static async Task<long> Count(string table,string source)=>await Db.Scoped(A,c=>Db.Scalar<long>(c,$"SELECT count(*) FROM poc.{table} WHERE source=@p0",source));
}
