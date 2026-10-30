using System.Globalization;
using System.Numerics;
using Npgsql;

partial class P {
 // Exact bounded final values; unbounded integer intermediates avoid decimal multiplication overflow.
 static (BigInteger N,BigInteger D) Rational(decimal x){var s=x.ToString(CultureInfo.InvariantCulture);var negative=s.StartsWith('-');s=s.TrimStart('-');var split=s.Split('.');var scale=split.Length==2?split[1].Length:0;return(BigInteger.Parse(string.Join("",split))*(negative?-1:1),BigInteger.Pow(10,scale));}
 static decimal Quantize(BigInteger n,BigInteger d,int scale){Check(d!=0,"division by zero");if(d<0){n=-n;d=-d;}var scaled=n*BigInteger.Pow(10,scale);var abs=BigInteger.Abs(scaled);var q=BigInteger.DivRem(abs,d,out var rem);if(rem*2>d||(rem*2==d&&!q.IsEven))q++;if(scaled<0)q=-q;return (decimal)q/(decimal)BigInteger.Pow(10,scale);}
 static decimal Multiply(decimal a,decimal b,int scale=6){var x=Rational(a);var y=Rational(b);return Quantize(x.N*y.N,x.D*y.D,scale);}
 static decimal Divide(decimal a,decimal b,int scale=6){var x=Rational(a);var y=Rational(b);return Quantize(x.N*y.D,x.D*y.N,scale);}
 static decimal Proportion(decimal q,decimal v,decimal total){var x=Rational(q);var y=Rational(v);var z=Rational(total);return Quantize(x.N*y.N*z.D,x.D*y.D*z.N,6);}
 static async Task DecimalTests(){
  await Run("decimal-postgresql-even-rounding-vectors","decimal",async()=>{
   await using var admin=await Db.Open(true);await Db.Exec(admin,"""
    CREATE OR REPLACE FUNCTION poc.round_even(n numeric,s int) RETURNS numeric LANGUAGE plpgsql IMMUTABLE AS $$
    DECLARE v numeric; i numeric; f numeric; BEGIN v:=abs(n)*power(10::numeric,s);i:=trunc(v);f:=v-i;
    IF f>0.5 OR (f=0.5 AND mod(i,2)=1) THEN i:=i+1;END IF;RETURN sign(n)*i/power(10::numeric,s);END $$;
    GRANT EXECUTE ON FUNCTION poc.round_even(numeric,int) TO poc_runtime;
    """
   );
   var vectors=new[]{("money-tiny",0.0000005m,1m,6),("money-tie-up",0.0000015m,1m,6),("negative-tie",-0.0000005m,1m,6),("JOD-3",12.3455m,1m,3),("JPY-0",2.5m,1m,0),("USD-2",10.025m,1m,2),("quantity-8",0.000000015m,1m,8),("FX-12",0.710000000001m,100m,6),("tax-illustrative",99.995m,0.16m,3),("discount",123.456789m,0.075m,6),("weighted-value",17m,12.3456789m,6),("production",100m,10.123456789m,6),("large-safe",999999999999m,999.999999m,6)};
   var results=new List<object>();await using var c=await Db.Open();
   foreach(var(n,a,b,scale) in vectors){var app=Multiply(a,b,scale);var pg=await Db.Scalar<decimal>(c,"SELECT poc.round_even(@p0::numeric*@p1::numeric,@p2)",a,b,scale);Check(app==pg,n+" differs");results.Add(new{name=n,a,b,scale,app,postgres=pg});}
   var thirds=Divide(1,3,8);Check(thirds==await Db.Scalar<decimal>(c,"SELECT poc.round_even(1::numeric/3,8)"),"UOM mismatch");
   decimal repeated=0;for(int i=0;i<10000;i++)repeated+=Multiply(.0000005m,1m);var aggregate=Multiply(.0000005m,10000m);Check(repeated==0&&aggregate==.005m,"rounding point effect");
   var pgDefault=await Db.Scalar<decimal>(c,"SELECT round(2.5::numeric,0)");Check(pgDefault==3&&Multiply(2.5m,1m,0)==2,"expected default mismatch not detected");
   var alloc=Divide(100,3);var final=100-alloc-alloc;Check(alloc+alloc+final==100,"residual not conserved");
   await File.WriteAllTextAsync(Path.Combine(Evidence,"decimal-vectors.json"),System.Text.Json.JsonSerializer.Serialize(new{results,thirds,repeated,aggregate,pgDefault,partialAllocation=new[]{alloc,alloc,final}},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
   return new{vectors=results.Count+4,mismatchesUnderExplicitPolicy=0,postgresDefault="away from zero; differs from .NET ToEven",roundingPoint="ledger line boundary, explicit final residual; no repeated hidden intermediate rounding"};
  });
  await Run("decimal-intermediate-overflow-range-and-time-uuid","decimal",async()=>{
   bool overflow=false;var huge=9999999999999999.99999999m;var price=99999999999999.9999999999m;try{var ignored=huge*price;}catch(OverflowException){overflow=true;}Check(overflow,"expected decimal overflow absent");var x=Rational(huge);var y=Rational(price);var exact=x.N*y.N;Check(exact>BigInteger.Pow(10,47),"big intermediate missing");
   await using var c=await Db.Open();await Expect(()=>Db.Exec(c,"SELECT @p0::numeric(24,6)",9999999999999999999m),"22003");
   var id=Guid.CreateVersion7();var date=new DateOnly(2026,10,18);var instant=new DateTimeOffset(2026,10,18,23,15,0,TimeSpan.Zero);Check(await Db.Scalar<Guid>(c,"SELECT @p0::uuid",id)==id,"uuid");Check(await Db.Scalar<DateOnly>(c,"SELECT @p0::date",date)==date,"date");Check(await Db.Scalar<DateTime>(c,"SELECT @p0::timestamptz",instant)==instant.UtcDateTime,"instant");
   return new{decimalOverflowDetected=overflow,exactIntermediateDigits=exact.ToString().Length,amountOverflow="rejected 22003",uuidVersion=id.Version,date,instant,legalTaxRounding="pending verification; examples not statutory rules"};
  });
 }
 static async Task JournalSql(NpgsqlConnection c,string source,decimal amount,string debit,string credit){
  if(amount==0)return;if(amount<0){(debit,credit)=(credit,debit);amount=-amount;}
  var id=Guid.CreateVersion7();await Db.Exec(c,"INSERT INTO poc.journals VALUES(@p0,@p1,@p2,@p3,1)",id,A.Tenant,A.Company,source);
  await Db.Exec(c,"INSERT INTO poc.lines VALUES(@p0,@p1,@p2,@p3,@p4,@p5,0),(@p6,@p1,@p2,@p3,@p7,0,@p5)",Guid.CreateVersion7(),A.Tenant,A.Company,id,debit,amount,Guid.CreateVersion7(),credit);
 }
 static async Task NewPool(string product)=>await Db.Scoped(A,c=>Db.Exec(c,"INSERT INTO poc.stock VALUES(@p0,@p1,@p2,0,0,0,0)",A.Tenant,A.Company,product));
 static async Task<(decimal Q,decimal V,decimal Delta)> Move(string product,string source,decimal qty,decimal? receiptValue=null,string contra="GRNI"){
  return await Db.Scoped(A,async c=>{await Db.Exec(c,"SELECT * FROM poc.periods WHERE id=1 FOR SHARE");decimal q,v;long rev;
   await using(var cmd=Db.Cmd(c,"SELECT on_hand,value,revision FROM poc.stock WHERE product=@p0 FOR UPDATE",product)){await using var rd=await cmd.ExecuteReaderAsync();Check(await rd.ReadAsync(),"missing pool");q=rd.GetDecimal(0);v=rd.GetDecimal(1);rev=rd.GetInt64(2);}
   Check(q+qty>=0,"negative stock");decimal delta=receiptValue??(qty==-q?-v:-Proportion(-qty,v,q));Check(q+qty!=0||v+delta==0,"zero quantity residual value");
   var id=Guid.CreateVersion7();await Db.Exec(c,"INSERT INTO poc.stock_ledger VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7)",id,A.Tenant,A.Company,product,source,qty,delta,rev+1);
   await Db.Exec(c,"INSERT INTO poc.valuation_ledger VALUES(@p0,@p1,@p2,@p3,@p4)",Guid.CreateVersion7(),A.Tenant,A.Company,id,delta);
   await Db.Exec(c,"UPDATE poc.stock SET on_hand=on_hand+@p0,value=value+@p1,revision=revision+1 WHERE product=@p2",qty,delta,product);
   await JournalSql(c,product+":"+source,delta,"Inventory",contra);
   if(contra=="WIP")await Db.Exec(c,"INSERT INTO poc.wip VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",Guid.CreateVersion7(),A.Tenant,A.Company,product,source,-delta);
   await Db.Exec(c,"INSERT INTO poc.outbox(id,tenant_id,company_id,source,kind) VALUES(@p0,@p1,@p2,@p3,'InventoryMoved')",Guid.CreateVersion7(),A.Tenant,A.Company,product+":"+source);
   return(q+qty,v+delta,delta);
  });
 }
 static async Task ReconcilePool(string product){await Db.Scoped(A,async c=>{var mismatches=await Db.Scalar<long>(c,"""
  SELECT count(*) FROM poc.stock s WHERE product=@p0 AND
  (s.on_hand<>(SELECT coalesce(sum(qty),0) FROM poc.stock_ledger l WHERE l.product=s.product)
  OR s.value<>(SELECT coalesce(sum(value),0) FROM poc.stock_ledger l WHERE l.product=s.product)
  OR s.value<>(SELECT coalesce(sum(v.delta),0) FROM poc.valuation_ledger v JOIN poc.stock_ledger l ON l.id=v.movement_id WHERE l.product=s.product)
  OR s.value<>(SELECT coalesce(sum(debit-credit),0) FROM poc.lines l JOIN poc.journals j ON j.id=l.journal_id WHERE j.source LIKE @p1 AND l.account='Inventory'))
  """,product,product+":%");Check(mismatches==0,"stock/valuation/GL reconciliation "+product);return 1;});}
 static async Task Costing(){
  await Run("mwa-sequence-ledger-value-gl-reconciliation","costing",async()=>{const string p="MWA";await NewPool(p);var steps=new List<object>();async Task Step(string source,decimal qty,decimal? value,string contra="GRNI"){var r=await Move(p,source,qty,value,contra);await ReconcilePool(p);steps.Add(new{source,qty=r.Q,value=r.V,average=r.Q==0?0:Divide(r.V,r.Q,10),delta=r.Delta});}
   await Step("opening",10,100);await Step("receipt-one",10,140);await Step("issue",-5,null,"COGS");await Step("receipt-two",5,90);await Step("linked-return-of-issue",2,24,"COGS");await Step("approved-value-adjustment",0,6);await Step("last-issue-residual",-22,null,"COGS");
   await File.WriteAllTextAsync(Path.Combine(Evidence,"mwa-steps.json"),System.Text.Json.JsonSerializer.Serialize(steps,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));return new{steps=steps.Count,finalQuantity=0,finalValue=0,reconciliation="after every event",history="append-only"};});
  await Run("mwa-concurrent-receipts-issues-and-retries","costing",async()=>{const string p="MWA-concurrent";await NewPool(p);await Move(p,"opening",100,1000);var tasks=Enumerable.Range(0,20).Select(async i=>{if(i%2==0)await Move(p,"r"+i,1,12);else await Move(p,"i"+i,-1,null,"COGS");});await Task.WhenAll(tasks);await ReconcilePool(p);var q=await Db.Scoped(A,c=>Db.Scalar<decimal>(c,"SELECT on_hand FROM poc.stock WHERE product=@p0",p));Check(q==100,"lost quantity");return new{concurrentEvents=20,finalQuantity=q,serializedPool=true,reconciliation="exact; order-specific MWA recorded by sequence"};});
  await Run("late-cost-append-only-onhand-and-consumed-split","costing",async()=>{const string p="late-cost";await NewPool(p);await Move(p,"receipt",10,100);await Move(p,"issue",-5,null,"COGS");var old=await Db.Scoped(A,c=>Db.Scalar<decimal>(c,"SELECT value FROM poc.stock_ledger WHERE product=@p0 AND source='issue'",p));await Move(p,"linked-current-period-adjustment",0,5,"GRNI");await Db.Scoped(A,async c=>{await JournalSql(c,p+":consumed-adjustment",5,"COGS","GRNI");return 1;});await ReconcilePool(p);Check(old==-50,"history wrong");var now=await Db.Scoped(A,c=>Db.Scalar<decimal>(c,"SELECT value FROM poc.stock_ledger WHERE product=@p0 AND source='issue'",p));Check(now==old,"history changed");return new{originalReceipt=100,correctedReceipt=110,remainingQuantity=5,remainingValue=55,consumedCost=55,inventoryDelta=5,cogsDelta=5,oldIssue=now,limitation="single-source shadow-delta case only; mixed-source downstream production graph not proven"};});
  await Run("open-item-partial-allocation-reconciliation","reconciliation",async()=>{var id=Guid.NewGuid();await Db.Scoped(A,async c=>{await Db.Exec(c,"INSERT INTO poc.open_items VALUES(@p0,@p1,@p2,100)",id,A.Tenant,A.Company);foreach(var amount in new[]{60m,40m}){var remaining=await Db.Scalar<decimal>(c,"SELECT original-(SELECT coalesce(sum(amount),0) FROM poc.allocations WHERE item_id=@p0) FROM poc.open_items WHERE id=@p0 FOR UPDATE",id);Check(amount<=remaining,"overallocated");await Db.Exec(c,"INSERT INTO poc.allocations VALUES(@p0,@p1,@p2,@p3,@p4)",Guid.NewGuid(),A.Tenant,A.Company,id,amount);}return 1;});var sum=await Db.Scoped(A,c=>Db.Scalar<decimal>(c,"SELECT sum(amount) FROM poc.allocations WHERE item_id=@p0",id));Check(sum==100,"allocation differs");return new{original=100,allocations=sum,remaining=0,scope="AR representative; FX/AP full flow pending"};});
 }
 static async Task Production(){
  foreach(var policy in new[]{"estimated-credit-allowed","incurred-cap","wait-for-funded-estimate"})await Run("wip-partial-100-40-30-30-"+policy,"wip",async()=>{
   var p="FG-"+policy;await NewPool(p);var events=new List<object>();decimal wip=0;var receipts=new List<(int Q,decimal Value)>();var qtys=new[]{40,30,30};var inputs=new[]{500m,300m,200m};int denied=0;
   for(int i=0;i<3;i++){
    await Db.Scoped(A,async c=>{await Db.Exec(c,"INSERT INTO poc.wip VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",Guid.NewGuid(),A.Tenant,A.Company,p,"input"+i,inputs[i]);await JournalSql(c,p+":input"+i,inputs[i],"WIP","GRNI");return 1;});wip+=inputs[i];events.Add(new{step="input"+i,wip});
    var estimate=qtys[i]*12m;if(policy=="wait-for-funded-estimate"&&estimate>wip){denied++;events.Add(new{step="receipt"+i,state="BLOCKED insufficient incurred WIP",wip});continue;}var value=policy=="incurred-cap"?Math.Min(estimate,wip):estimate;await Move(p,"receipt"+i,qtys[i],value,"WIP");wip-=value;receipts.Add((qtys[i],value));events.Add(new{step="receipt"+i,quantity=qtys[i],value,wip});
    if(i==0)await Move(p,"sold20",-20,null,"COGS");
   }
   if(denied>0){await ReconcilePool(p);return new{policy,events,denied,decision="cannot provide the requested partial schedule without receipt deferral/replanning; policy decision required"};}
   var total=receipts.Sum(r=>r.Value);var deltas=receipts.Select(r=>r.Q*10m-r.Value).ToArray();var cogsCorrection=deltas[0]*.5m;var inventoryCorrection=deltas.Sum()-cogsCorrection;
   if(inventoryCorrection!=0)await Move(p,"final-cost-inventory-correction",0,inventoryCorrection,"WIP");
   if(cogsCorrection!=0)await Db.Scoped(A,async c=>{await JournalSql(c,p+":final-cost-consumed-correction",cogsCorrection,"COGS","WIP");await Db.Exec(c,"INSERT INTO poc.wip VALUES(@p0,@p1,@p2,@p3,'final-cogs-correction',@p4)",Guid.NewGuid(),A.Tenant,A.Company,p,-cogsCorrection);return 1;});
   wip-=inventoryCorrection+cogsCorrection;Check(wip==0,"WIP not cleared");await ReconcilePool(p);
   var wipFacts=await Db.Scoped(A,c=>Db.Scalar<decimal>(c,"SELECT sum(delta) FROM poc.wip WHERE policy=@p0",p));var gl=await Db.Scoped(A,c=>Db.Scalar<decimal>(c,"SELECT sum(debit-credit) FROM poc.lines l JOIN poc.journals j ON j.id=l.journal_id WHERE j.source LIKE @p0 AND account='WIP'",p+":%"));Check(wipFacts==0&&gl==0,"WIP facts/GL mismatch");
   await File.WriteAllTextAsync(Path.Combine(Evidence,policy+".json"),System.Text.Json.JsonSerializer.Serialize(new{events,total,actual=1000,deltas,inventoryCorrection,cogsCorrection,wipFacts,gl},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
   return new{policy,provisionalValue=total,actual=1000,finalVariance=1000-total,inventoryCorrection,cogsCorrection,wipFacts,gl,finalOutput=100,remainingQuantity=80,remainingValue=800,consumedCost=200,decision="technical comparison only; no final financial policy approval"};
  });
 }
}
