using System.Diagnostics;
using Erp.Application.Abstractions;
using Erp.Kernel;
using Erp.Platform.Infrastructure;
using Erp.Platform.Infrastructure.Persistence;

namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class OutboxTests(DatabaseFixture db)
{
    [Fact]
    public async Task Worker_is_company_scoped_and_requires_worker_permission()
    {
        await db.Execute("UPDATE integration.outbox SET state='Failed' WHERE state<>'Completed'");
        await db.Record(await db.Context(), Guid.NewGuid().ToString(), "worker-scope");
        var pump = new OutboxPump(db.Store, db.Inbox, [new ProbeHandler()]);
        var apiContext = await db.Context();
        await Assert.ThrowsAsync<FoundationException>(() => pump.RunOnceAsync(apiContext));
        Assert.False(await pump.RunOnceAsync(await db.Context(DatabaseFixture.TenantB, DatabaseFixture.CompanyB, source: ExecutionSource.Worker, action: "outbox.dispatch")));
        Assert.True(await pump.RunOnceAsync(await db.Context(source: ExecutionSource.Worker, action: "outbox.dispatch")));
    }
    [Fact]
    public async Task Physical_worker_processes_killed_before_send_and_after_consumer_commit_recover_once()
    {
        await db.Execute("UPDATE integration.outbox SET state='Failed' WHERE state<>'Completed'");
        var context = await db.Context(source: ExecutionSource.Worker); await db.Record(context, Guid.NewGuid().ToString(), "physical-crash");
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Erp.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = Path.Combine(directory.FullName, "tests", "Erp.CrashHarness", "bin", configuration, "net10.0", "Erp.CrashHarness.dll");
        foreach (var mode in new[] { "before-send", "after-consumer" })
        {
            using var child = new Process { StartInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { RedirectStandardOutput = true, RedirectStandardError = true } };
            child.StartInfo.ArgumentList.Add(dll); child.StartInfo.ArgumentList.Add(mode);
            child.StartInfo.Environment["ERP_HARNESS_CONNECTION"] = db.Runtime;
            child.StartInfo.Environment["ERP_HARNESS_TENANT"] = context.TenantId.ToString(); child.StartInfo.Environment["ERP_HARNESS_COMPANY"] = context.CompanyId.ToString();
            child.Start(); var output = await child.StandardOutput.ReadToEndAsync(); await child.WaitForExitAsync();
            Assert.NotEqual(0, child.ExitCode); Assert.Contains("DURABLE_WORKER_STATE", output);
            await db.Execute("UPDATE integration.outbox SET lease_until=now()-interval '1 second' WHERE state='Processing'");
        }
        var recovered = await db.Store.ClaimAsync(context); Assert.NotNull(recovered);
        Assert.False(await db.Inbox.ProcessAsync(context, "crash-consumer", recovered.Message, (_, _) => throw new InvalidOperationException("must not run twice")));
        Assert.True(await db.Store.AcknowledgeAsync(context, recovered));
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM foundation_test.consumer_effects WHERE event_id=@id", ("id", recovered.Message.Id)));
    }
    [Fact]
    public async Task Worker_restart_redelivers_after_lease_loss_and_stale_ack_is_rejected()
    {
        await db.Execute("UPDATE integration.outbox SET state='Failed' WHERE state<>'Completed'");
        var context = await db.Context(source: ExecutionSource.Worker);
        await db.Record(context, Guid.NewGuid().ToString(), "restart");
        var first = await db.Store.ClaimAsync(context); Assert.NotNull(first);
        Assert.Null(await db.Store.ClaimAsync(context));
        // Worker disappeared before dispatch. A restarted instance observes only durable DB state.
        await db.Execute("UPDATE integration.outbox SET lease_until=now()-interval '1 second' WHERE id=@id", ("id", first.Message.Id));
        var restartedStore = new OutboxStore(new PostgresUnitOfWork(db.Source));
        var second = await restartedStore.ClaimAsync(context); Assert.NotNull(second);
        Assert.Equal(first.Message.Id, second.Message.Id); Assert.NotEqual(first.LeaseId, second.LeaseId);
        var handler = new ProbeHandler();
        Assert.True(await db.Inbox.ProcessAsync(context, handler.Consumer, second.Message, (tx, ct) => handler.HandleAsync(tx, second.Message, ct)));
        // Send/consumer committed, worker disappears before ACK. Redelivery is expected.
        await db.Execute("UPDATE integration.outbox SET lease_until=now()-interval '1 second' WHERE id=@id", ("id", first.Message.Id));
        var third = await new OutboxStore(new PostgresUnitOfWork(db.Source)).ClaimAsync(context); Assert.NotNull(third);
        Assert.False(await db.Inbox.ProcessAsync(context, handler.Consumer, third.Message, (tx, ct) => handler.HandleAsync(tx, third.Message, ct)));
        Assert.False(await db.Store.AcknowledgeAsync(context, second)); Assert.True(await db.Store.AcknowledgeAsync(context, third));
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM foundation_test.consumer_effects WHERE event_id=@id", ("id", first.Message.Id)));
    }
    [Fact]
    public async Task Concurrent_workers_claim_at_most_one_live_lease()
    {
        await db.Execute("UPDATE integration.outbox SET state='Failed' WHERE state<>'Completed'");
        var context = await db.Context(); await db.Record(context, Guid.NewGuid().ToString(), "workers");
        var claims = await Task.WhenAll(db.Store.ClaimAsync(context), db.Store.ClaimAsync(context));
        Assert.Single(claims, x => x is not null);
    }
    [Fact]
    public async Task Retry_backoff_and_permanent_failure_are_visible_not_silently_discarded()
    {
        await db.Execute("UPDATE integration.outbox SET state='Failed' WHERE state<>'Completed'");
        var context = await db.Context(); await db.Record(context, Guid.NewGuid().ToString(), "failure");
        var lease = await db.Store.ClaimAsync(context); Assert.NotNull(lease);
        Assert.True(await db.Store.FailAsync(context, lease, false)); Assert.Null(await db.Store.ClaimAsync(context));
        Assert.Equal("Pending", await db.Scalar<string>("SELECT state FROM integration.outbox WHERE id=@id", ("id", lease.Message.Id)));
        Assert.True(await db.Scalar<bool>("SELECT available_at>now() FROM integration.outbox WHERE id=@id", ("id", lease.Message.Id)));
        await db.Execute("UPDATE integration.outbox SET available_at=now()-interval '1 second' WHERE id=@id", ("id", lease.Message.Id));
        var retry = await db.Store.ClaimAsync(context); Assert.NotNull(retry); Assert.True(await db.Store.FailAsync(context, retry, true));
        Assert.Equal("Failed", await db.Scalar<string>("SELECT state FROM integration.outbox WHERE id=@id", ("id", lease.Message.Id)));
        Assert.Null(await db.Store.ClaimAsync(context));
    }
    [Fact]
    public async Task Exhausted_crashed_lease_becomes_failed()
    {
        await db.Execute("UPDATE integration.outbox SET state='Failed' WHERE state<>'Completed'");
        var context = await db.Context(); await db.Record(context, Guid.NewGuid().ToString(), "exhausted");
        var lease = await db.Store.ClaimAsync(context); Assert.NotNull(lease);
        await db.Execute("UPDATE integration.outbox SET attempts=5,lease_until=now()-interval '1 second' WHERE id=@id", ("id", lease.Message.Id));
        Assert.Equal(1, await db.Store.ReapExhaustedAsync(context));
        Assert.Equal("Failed", await db.Scalar<string>("SELECT state FROM integration.outbox WHERE id=@id", ("id", lease.Message.Id)));
    }
    [Fact]
    public async Task Inbox_effect_and_marker_commit_together_and_payload_collision_fails()
    {
        var context = await db.Context(); var message = new IntegrationEvent(Guid.NewGuid(), "test.probe.v1", "inbox", "{}", DateTimeOffset.UtcNow);
        var handler = new ProbeHandler();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Inbox.ProcessAsync(context, "consumer", message, async (tx, ct) =>
        { await handler.HandleAsync(tx, message, ct); throw new InvalidOperationException("consumer crash before commit"); }));
        Assert.Equal(0, await db.Scalar<long>("SELECT count(*) FROM integration.inbox WHERE event_id=@id", ("id", message.Id)));
        Assert.Equal(0, await db.Scalar<long>("SELECT count(*) FROM foundation_test.consumer_effects WHERE event_id=@id", ("id", message.Id)));
        var duplicates = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => db.Inbox.ProcessAsync(context, "consumer", message, (tx, ct) => handler.HandleAsync(tx, message, ct))));
        Assert.Single(duplicates, x => x);
        var conflict = await Assert.ThrowsAsync<FoundationException>(() => db.Inbox.ProcessAsync(context, "consumer", message with { Payload = "{\"changed\":true}" }, (_, _) => Task.CompletedTask));
        Assert.Equal(Errors.EVENT_PAYLOAD_CONFLICT, conflict.Error);
    }
}

public sealed class ProbeHandler : IIntegrationEventHandler
{
    public string EventType => "test.probe.v1";
    public string Consumer => "probe-consumer-v1";
    public async Task HandleAsync(IApplicationTransaction transaction, IntegrationEvent message, CancellationToken token)
    {
        var tx = PostgresTransaction.Require(transaction);
        await Sql.ExecuteAsync(tx.Connection, """
            INSERT INTO foundation_test.consumer_effects VALUES(@id,@tenant,@company,@correlation,@trace,
                current_setting('erp.correlation_id'),current_setting('erp.trace_id'))
            """, token, ("id", message.Id), ("tenant", tx.Context.TenantId), ("company", tx.Context.CompanyId),
            ("correlation", tx.Context.CorrelationId), ("trace", Activity.Current?.TraceId.ToString() ?? "no-trace"));
    }
}
