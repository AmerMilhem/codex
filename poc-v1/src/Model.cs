using Microsoft.EntityFrameworkCore;
using Npgsql;

record Scope(Guid Tenant,Guid Company);
class Document { public Guid id{get;set;} public Guid tenant_id{get;set;} public Guid company_id{get;set;} public string source{get;set;}=""; public decimal amount{get;set;} public string state{get;set;}="Posted"; }
class Journal { public Guid id{get;set;} public Guid tenant_id{get;set;} public Guid company_id{get;set;} public string source{get;set;}=""; public int period_id{get;set;}=1; }
class Line { public Guid id{get;set;} public Guid tenant_id{get;set;} public Guid company_id{get;set;} public Guid journal_id{get;set;} public string account{get;set;}=""; public decimal debit{get;set;} public decimal credit{get;set;} }
class Outbox { public Guid id{get;set;} public Guid tenant_id{get;set;} public Guid company_id{get;set;} public string source{get;set;}=""; public string kind{get;set;}="Posted"; }
class BusinessDb(DbContextOptions<BusinessDb> options):DbContext(options) {
 public DbSet<Document> Documents=>Set<Document>();
 protected override void OnModelCreating(ModelBuilder b){b.Entity<Document>().ToTable("documents","poc").HasKey(x=>x.id);b.Entity<Document>().Property(x=>x.amount).HasPrecision(24,6);}
}
class FinanceDb(DbContextOptions<FinanceDb> options):DbContext(options) {
 public DbSet<Journal> Journals=>Set<Journal>(); public DbSet<Line> Lines=>Set<Line>();
 protected override void OnModelCreating(ModelBuilder b){b.Entity<Journal>().ToTable("journals","poc").HasKey(x=>x.id);b.Entity<Line>().ToTable("lines","poc").HasKey(x=>x.id);b.Entity<Line>().Property(x=>x.debit).HasPrecision(24,6);b.Entity<Line>().Property(x=>x.credit).HasPrecision(24,6);}
}
class IntegrationDb(DbContextOptions<IntegrationDb> options):DbContext(options){
 public DbSet<Outbox> Events=>Set<Outbox>();
 protected override void OnModelCreating(ModelBuilder b){b.Entity<Outbox>().ToTable("outbox","poc").HasKey(x=>x.id);}
}
static class Db {
 public static string Admin=>Environment.GetEnvironmentVariable("POC_ADMIN")??"Host=127.0.0.1;Port=55432;Database=poc;Username=postgres;Password=disposable-poc-only";
 public static string Runtime=>new NpgsqlConnectionStringBuilder(Admin){Username="poc_runtime",Password="disposable-poc-only",MaxPoolSize=30,MinPoolSize=0}.ToString();
 public static async Task<NpgsqlConnection> Open(bool admin=false,string? cs=null){var c=new NpgsqlConnection(cs??(admin?Admin:Runtime));await c.OpenAsync();return c;}
 public static NpgsqlCommand Cmd(NpgsqlConnection c,string sql,params object[] values){var x=new NpgsqlCommand(sql,c);for(int i=0;i<values.Length;i++)x.Parameters.AddWithValue("p"+i,values[i]);return x;}
 public static async Task<int> Exec(NpgsqlConnection c,string sql,params object[] values){await using var cmd=Cmd(c,sql,values);return await cmd.ExecuteNonQueryAsync();}
 public static async Task<T> Scalar<T>(NpgsqlConnection c,string sql,params object[] values){await using var cmd=Cmd(c,sql,values);return (T)(await cmd.ExecuteScalarAsync()??throw new Exception("no scalar"));}
 public static async Task SetScope(NpgsqlConnection c,Scope s){await Exec(c,"SELECT set_config('app.tenant',@p0,true),set_config('app.company',@p1,true)",s.Tenant.ToString(),s.Company.ToString());}
 public static async Task<T> Scoped<T>(Scope s,Func<NpgsqlConnection,Task<T>> action){await using var c=await Open();await using var tx=await c.BeginTransactionAsync();await SetScope(c,s);var r=await action(c);await tx.CommitAsync();return r;}
 public static async Task Seed(){await using var c=await Open(true);var sql=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"schema.sql"));await Exec(c,sql);foreach(var s in new[]{P.A,P.B,P.A2}){await Exec(c,"INSERT INTO poc.periods VALUES(@p0,@p1,1,false),(@p0,@p1,2,true)",s.Tenant,s.Company);foreach(var a in new[]{"AR","Revenue","Inventory","COGS","GRNI","WIP","Variance","Inactive"})await Exec(c,"INSERT INTO poc.accounts VALUES(@p0,@p1,@p2,@p3)",s.Tenant,s.Company,a,a!="Inactive");await Exec(c,"INSERT INTO poc.scoped VALUES(@p0,@p1,@p2,@p3)",Guid.CreateVersion7(),s.Tenant,s.Company,s==P.B?"Tenant B":"Tenant A");}}
}
