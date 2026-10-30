using System.Diagnostics;
using Erp.Application.Abstractions;
using Erp.Platform.Infrastructure;
using Erp.Platform.Infrastructure.Persistence;
using Npgsql;

var connection = Environment.GetEnvironmentVariable("ERP_HARNESS_CONNECTION") ?? throw new InvalidOperationException();
await using var source = new NpgsqlDataSourceBuilder(connection).Build();
var tenant = Guid.Parse(Environment.GetEnvironmentVariable("ERP_HARNESS_TENANT")!);
var company = Guid.Parse(Environment.GetEnvironmentVariable("ERP_HARNESS_COMPANY")!);
var context = await new ContextResolver(new HarnessMembership(tenant, company)).ResolveAsync(new("crash-test", "worker"),
    tenant, company, "outbox.dispatch", "crash-worker", "crash-request", ExecutionSource.Worker);
var executor = new PostgresUnitOfWork(source); var store = new OutboxStore(executor);
var claim = await store.ClaimAsync(context) ?? throw new InvalidOperationException("No event available.");
if (args[0] == "after-consumer")
{
    await new InboxProcessor(executor).ProcessAsync(context, "crash-consumer", claim.Message,
        async (tx, ct) =>
        {
            await Sql.ExecuteAsync(PostgresTransaction.Require(tx).Connection,
            "INSERT INTO foundation_test.consumer_effects VALUES(@id,@tenant,@company,'crash','crash','crash','crash')", ct,
            ("id", claim.Message.Id), ("tenant", tenant), ("company", company));
        });
}
Console.WriteLine("DURABLE_WORKER_STATE"); Console.Out.Flush();
Process.GetCurrentProcess().Kill();

internal sealed class HarnessMembership(Guid tenant, Guid company) : IMembershipStore
{
    public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken token) =>
        ValueTask.FromResult<PrincipalMembership?>(new(true, [new(tenant, company, new HashSet<string> { "outbox.dispatch" })]));
}
