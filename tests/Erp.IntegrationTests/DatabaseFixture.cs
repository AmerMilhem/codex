using Erp.Application.Abstractions;
using Erp.Platform.Infrastructure;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Erp.IntegrationTests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    public const string Image = "postgres:18.2@sha256:a688ae5c99f86c1424e046ace0de890608cc0ddb5ec8ef72a6ff8305fdc8f4d2";
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image).WithDatabase("foundation")
        .WithUsername("postgres").WithPassword("disposable-foundation-tests").WithCleanUp(false).WithAutoRemove(true).Build();
    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    public static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid CompanyA2 = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid CompanyB = Guid.Parse("33333333-3333-4333-8333-333333333333");
    public string Admin => container.GetConnectionString();
    public string Runtime { get; private set; } = "";
    public string Migration { get; private set; } = "";
    public NpgsqlDataSource Source { get; private set; } = null!;
    public IUnitOfWorkExecutor Uow { get; private set; } = null!;
    public IIdempotentCommands Commands { get; private set; } = null!;
    public IOutboxWriter Outbox { get; } = new OutboxWriter(TimeProvider.System);
    public IAuditWriter Audit { get; } = new AuditWriter(TimeProvider.System, new MaskAllSnapshots());
    public OutboxStore Store { get; private set; } = null!;
    public InboxProcessor Inbox { get; private set; } = null!;
    public Membership Memberships { get; } = new();
    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using var admin = new NpgsqlConnection(Admin); await admin.OpenAsync();
        await Sql.ExecuteAsync(admin, """
            CREATE ROLE erp_migrator LOGIN PASSWORD 'migration-tests-only' NOSUPERUSER NOBYPASSRLS;
            CREATE ROLE erp_runtime LOGIN PASSWORD 'runtime-tests-only' NOSUPERUSER NOBYPASSRLS;
            ALTER DATABASE foundation OWNER TO erp_migrator;
            """);
        Migration = new NpgsqlConnectionStringBuilder(Admin) { Username = "erp_migrator", Password = "migration-tests-only" }.ToString();
        Runtime = new NpgsqlConnectionStringBuilder(Admin) { Username = "erp_runtime", Password = "runtime-tests-only", MaxPoolSize = 20 }.ToString();
        await MigrationRunner.ApplyAsync(Migration);
        await Erp.Finance.Infrastructure.FinanceMigrations.ApplyAsync(Migration);
        await Sql.ExecuteAsync(admin, TestSchema);
        foreach (var (tenant, company) in new[] { (TenantA, CompanyA), (TenantA, CompanyA2), (TenantB, CompanyB) })
            await Sql.ExecuteAsync(admin, "INSERT INTO foundation_test.scope_markers VALUES(@id,@tenant,@company)", default,
                ("id", Guid.NewGuid()), ("tenant", tenant), ("company", company));
        Source = new NpgsqlDataSourceBuilder(Runtime).Build(); Uow = new PostgresUnitOfWork(Source);
        Commands = new IdempotentCommands(Uow, TimeProvider.System); Store = new OutboxStore(Uow); Inbox = new InboxProcessor(Uow);
    }
    public async Task DisposeAsync() { await Source.DisposeAsync(); await container.DisposeAsync(); }
    public async Task<VerifiedContext> Context(Guid? tenant = null, Guid? company = null, string correlation = "integration-correlation", ExecutionSource source = ExecutionSource.Test, string action = "foundation.execute")
        => await new ContextResolver(Memberships).ResolveAsync(new("tests", "runner"), tenant ?? TenantA, company ?? CompanyA,
            action, correlation, Guid.NewGuid().ToString("N"), source);
    public async Task<T> Scalar<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var c = new NpgsqlConnection(Admin); await c.OpenAsync();
        await using var command = Sql.Command(c, sql, parameters); return (T)(await command.ExecuteScalarAsync())!;
    }
    public async Task Execute(string sql, params (string Name, object Value)[] parameters)
    {
        await using var c = new NpgsqlConnection(Admin); await c.OpenAsync(); await Sql.ExecuteAsync(c, sql, default, parameters);
    }
    public async Task<VerifiedContext> PeriodContext(Guid tenant, Guid company, string action = Erp.Finance.Contracts.PeriodPermissions.Close)
    {
        var membership = new PeriodCloseMembership(tenant, company);
        return await new ContextResolver(membership).ResolveAsync(new("tests", "period-closer"), tenant, company,
            action, "period-close-tests", Guid.NewGuid().ToString("N"), ExecutionSource.Test);
    }
    public async Task<CommandReceipt> ClosePeriod(Guid id, Guid tenant, Guid company, string key = "test-close",
        string? comment = "Regression period close", long expected = 1)
    {
        var context = await PeriodContext(tenant, company);
        var request = new Erp.Finance.Contracts.ClosePeriodRequest(comment);
        var identity = new CommandIdentity("Finance.CloseFiscalPeriod", id.ToString(), key,
            RequestFingerprint.Create("POST", id.ToString(), expected.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Text.Json.JsonSerializer.Serialize(request)));
        return await new Erp.Finance.Application.CloseFiscalPeriodCommand(Commands,
            new Erp.Finance.Infrastructure.FiscalPeriodStore(), Audit, Outbox, TimeProvider.System)
            .ExecuteAsync(context, id, expected, request, identity);
    }
    public async Task<CommandReceipt> CreatePeriod(Guid tenant, Guid company, DateOnly start, DateOnly end,
        string key = "test-create")
    {
        var context = await PeriodContext(tenant, company, Erp.Finance.Contracts.PeriodPermissions.Create);
        var request = new Erp.Finance.Contracts.CreateFiscalPeriodRequest(start, end);
        var identity = new CommandIdentity("Finance.CreateFiscalPeriod", "periods", key,
            RequestFingerprint.Create("POST", "periods", "0", System.Text.Json.JsonSerializer.Serialize(request)));
        return await new Erp.Finance.Application.CreateFiscalPeriodCommand(Commands,
            new Erp.Finance.Infrastructure.FiscalPeriodStore(), Audit, Outbox, TimeProvider.System)
            .ExecuteAsync(context, request, identity);
    }
    private sealed class PeriodCloseMembership(Guid tenant, Guid company) : IMembershipStore
    {
        public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken token) =>
            ValueTask.FromResult<PrincipalMembership?>(new(true, [new(tenant, company,
                new HashSet<string> { Erp.Finance.Contracts.PeriodPermissions.Close, Erp.Finance.Contracts.PeriodPermissions.View,
                    Erp.Finance.Contracts.PeriodPermissions.Create })]));
    }
    public async Task<CommandReceipt> Record(VerifiedContext context, string source, string key, string payload = "{\"value\":1}", bool fail = false)
        => await Commands.ExecuteAsync(context, new("RecordProbe", source, key, RequestFingerprint.Create("POST", source, "0", payload)),
            async (transaction, token) => await RecordEffect(transaction, source, fail, token));
    public async Task<CommandReceipt> RecordEffect(IApplicationTransaction transaction, string source, bool fail, CancellationToken token)
    {
        var tx = PostgresTransaction.Require(transaction); var id = Guid.CreateVersion7();
        var state = tx.Enlist(c => new ProbeStateContext(new DbContextOptionsBuilder<ProbeStateContext>().UseNpgsql(c).Options));
        state.Rows.Add(new ProbeState { Id = id, TenantId = tx.Context.TenantId, CompanyId = tx.Context.CompanyId, Source = source });
        await state.SaveChangesAsync(token);
        var ledger = tx.Enlist(c => new ProbeEffectContext(new DbContextOptionsBuilder<ProbeEffectContext>().UseNpgsql(c).Options));
        ledger.Rows.Add(new ProbeEffect { Id = Guid.NewGuid(), StateId = id, TenantId = tx.Context.TenantId, CompanyId = tx.Context.CompanyId });
        await ledger.SaveChangesAsync(token);
        Audit.Append(tx, new("RecordProbe", "FoundationProbe", id.ToString(), After: new Dictionary<string, object?> { ["secret"] = "never-store" }));
        Outbox.Append(tx, new(Guid.CreateVersion7(), "test.probe.v1", source, "{\"value\":1}", DateTimeOffset.UtcNow));
        if (fail) throw new InvalidOperationException("Injected before commit.");
        return new(id, 1);
    }
    public sealed class Membership : IMembershipStore
    {
        public bool Enabled = true;
        public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken token) => ValueTask.FromResult<PrincipalMembership?>(
            new(Enabled, new[] { (TenantA, CompanyA), (TenantA, CompanyA2), (TenantB, CompanyB) }
                .Select(x => new CompanyGrant(x.Item1, x.Item2, new HashSet<string> { "foundation.execute", "outbox.dispatch",
                    "Finance.Journal.View", "Finance.Journal.Create", "Finance.Journal.Edit", "Finance.Journal.Post" })).ToArray()));
    }
    public const string TestSchema = """
        CREATE SCHEMA foundation_test;
        GRANT USAGE ON SCHEMA foundation_test TO erp_runtime;
        CREATE TABLE foundation_test.scope_markers(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL);
        CREATE TABLE foundation_test.states(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,source text NOT NULL,revision bigint NOT NULL DEFAULT 0,UNIQUE(tenant_id,company_id,source),UNIQUE(tenant_id,company_id,id));
        CREATE TABLE foundation_test.effects(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,state_id uuid NOT NULL,FOREIGN KEY(tenant_id,company_id,state_id) REFERENCES foundation_test.states(tenant_id,company_id,id));
        CREATE TABLE foundation_test.consumer_effects(event_id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,correlation text NOT NULL,trace text NOT NULL,db_correlation text NOT NULL,db_trace text NOT NULL);
        CREATE FUNCTION foundation_test.required_effect() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
          IF NOT EXISTS(SELECT 1 FROM foundation_test.effects WHERE state_id=NEW.id)
            OR NOT EXISTS(SELECT 1 FROM integration.outbox WHERE source_id=NEW.source)
            OR NOT EXISTS(SELECT 1 FROM audit.entries WHERE entity_id=NEW.id::text)
          THEN RAISE EXCEPTION 'test.required_effect_missing' USING ERRCODE='23514';END IF;RETURN NEW;END $$;
        CREATE CONSTRAINT TRIGGER require_all_effects AFTER INSERT ON foundation_test.states
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION foundation_test.required_effect();
        DO $$ DECLARE item text; BEGIN FOREACH item IN ARRAY ARRAY['scope_markers','states','effects','consumer_effects'] LOOP
          EXECUTE format('ALTER TABLE foundation_test.%I ENABLE ROW LEVEL SECURITY',item);
          EXECUTE format('ALTER TABLE foundation_test.%I FORCE ROW LEVEL SECURITY',item);
          EXECUTE format('CREATE POLICY scope ON foundation_test.%I USING
            (tenant_id=nullif(current_setting(''erp.tenant_id'',true),'''')::uuid AND company_id=nullif(current_setting(''erp.company_id'',true),'''')::uuid)
            WITH CHECK (tenant_id=nullif(current_setting(''erp.tenant_id'',true),'''')::uuid AND company_id=nullif(current_setting(''erp.company_id'',true),'''')::uuid)',item);
        END LOOP;END $$;
        GRANT SELECT,INSERT,UPDATE ON ALL TABLES IN SCHEMA foundation_test TO erp_runtime;
        """;
}

public sealed class ProbeState { public Guid Id { get; set; } public Guid TenantId { get; set; } public Guid CompanyId { get; set; } public string Source { get; set; } = ""; public long Revision { get; set; } }
public sealed class ProbeEffect { public Guid Id { get; set; } public Guid TenantId { get; set; } public Guid CompanyId { get; set; } public Guid StateId { get; set; } }
public sealed class ProbeStateContext(DbContextOptions<ProbeStateContext> options) : DbContext(options)
{
    public DbSet<ProbeState> Rows => Set<ProbeState>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ProbeState>().ToTable("states", "foundation_test").HasKey(x => x.Id);
        b.Entity<ProbeState>().Property(x => x.Revision).IsConcurrencyToken(); Names(b);
    }
    public static void Names(ModelBuilder b) { foreach (var entity in b.Model.GetEntityTypes()) foreach (var p in entity.GetProperties()) p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant()); }
}
public sealed class ProbeEffectContext(DbContextOptions<ProbeEffectContext> options) : DbContext(options)
{
    public DbSet<ProbeEffect> Rows => Set<ProbeEffect>();
    protected override void OnModelCreating(ModelBuilder b) { b.Entity<ProbeEffect>().ToTable("effects", "foundation_test").HasKey(x => x.Id); ProbeStateContext.Names(b); }
}

[CollectionDefinition("Postgres", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<DatabaseFixture> { }
