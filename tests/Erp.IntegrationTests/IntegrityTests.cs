using System.Diagnostics;
using Erp.Application.Abstractions;
using Erp.Kernel;
using Erp.Platform.Infrastructure;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class IntegrityTests(DatabaseFixture db)
{
    [Fact]
    public async Task Commit_enlists_two_module_contexts_with_audit_outbox_and_receipt()
    {
        var source = Guid.NewGuid().ToString(); var receipt = await db.Record(await db.Context(), source, "commit");
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM foundation_test.states WHERE id=@id", ("id", receipt.Reference)));
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM foundation_test.effects WHERE state_id=@id", ("id", receipt.Reference)));
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@source", ("source", source)));
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id", ("id", receipt.Reference.ToString())));
        Assert.DoesNotContain("never-store", await db.Scalar<string>("SELECT after::text FROM audit.entries WHERE entity_id=@id", ("id", receipt.Reference.ToString())));
    }
    [Fact]
    public async Task Rollback_after_module_SaveChanges_leaves_no_partial_facts_or_receipt()
    {
        var source = Guid.NewGuid().ToString();
        var context = await db.Context();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Record(context, source, "rollback", fail: true));
        foreach (var query in new[] { "SELECT count(*) FROM foundation_test.states WHERE source=@source", "SELECT count(*) FROM integration.outbox WHERE source_id=@source", "SELECT count(*) FROM integration.idempotency WHERE target=@source" })
            Assert.Equal(0, await db.Scalar<long>(query, ("source", source)));
    }
    [Fact]
    public async Task Missing_required_local_effect_prevents_commit()
    {
        var context = await db.Context();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Uow.ExecuteAsync(context, async (transaction, token) =>
            await Sql.ExecuteAsync(PostgresTransaction.Require(transaction).Connection,
                "INSERT INTO foundation_test.states(id,tenant_id,company_id,source) VALUES(@id,@tenant,@company,@source)", token,
                ("id", Guid.NewGuid()), ("tenant", context.TenantId), ("company", context.CompanyId), ("source", Guid.NewGuid().ToString()))));
        Assert.Equal("23514", error.SqlState);
    }
    [Fact]
    public async Task Concurrent_idempotent_requests_and_response_loss_return_one_effect()
    {
        var source = Guid.NewGuid().ToString(); var context = await db.Context();
        var receipts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => db.Record(context, source, "same-key")));
        Assert.Single(receipts.Select(x => x.Reference).Distinct());
        // Commit succeeded, client response is deliberately discarded; reconnect/replay retains original identity.
        var replay = await db.Record(await db.Context(), source, "same-key"); Assert.Equal(receipts[0], replay);
        var timeoutSource = Guid.NewGuid().ToString(); var inFlight = db.Record(context, timeoutSource, "timeout-key");
        await Assert.ThrowsAsync<TimeoutException>(() => inFlight.WaitAsync(TimeSpan.Zero));
        var original = await inFlight; Assert.Equal(original, await db.Record(context, timeoutSource, "timeout-key"));
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM foundation_test.states WHERE source=@source", ("source", source)));
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@source", ("source", source)));
    }
    [Fact]
    public async Task Changed_fingerprint_conflicts_and_changed_key_cannot_duplicate_source()
    {
        var context = await db.Context(); var source = Guid.NewGuid().ToString(); await db.Record(context, source, "key");
        var conflict = await Assert.ThrowsAsync<FoundationException>(() => db.Record(context, source, "key", "{\"value\":2}"));
        Assert.Equal(Errors.API_IDEMPOTENCY_CONFLICT, conflict.Error);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.Record(context, source, "other-key"));
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM integration.idempotency WHERE target=@source", ("source", source)));
    }
    [Fact]
    public async Task Full_unit_of_work_retries_serialization_once_without_duplicate_effect()
    {
        var context = await db.Context(); var source = Guid.NewGuid().ToString(); var attempts = 0;
        var receipt = await db.Commands.ExecuteAsync(context, new("RetryProbe", source, "retry", new string('A', 64)), async (tx, ct) =>
        {
            var result = await db.RecordEffect(tx, source, false, ct);
            if (++attempts == 1)
                await Sql.ExecuteAsync(PostgresTransaction.Require(tx).Connection,
                    "DO $$ BEGIN RAISE EXCEPTION 'injected serialization' USING ERRCODE='40001'; END $$", ct);
            return result;
        });
        Assert.Equal(2, attempts); Assert.NotEqual(Guid.Empty, receipt.Reference);
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM foundation_test.states WHERE source=@source", ("source", source)));
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@source", ("source", source)));
    }
    [Fact]
    public async Task Retry_is_bounded_and_rolls_back_each_attempt()
    {
        var attempts = 0;
        var context = await db.Context();
        await Assert.ThrowsAsync<PostgresException>(() => db.Uow.ExecuteAsync(context, async (tx, ct) =>
        {
            attempts++;
            return await Sql.ExecuteAsync(PostgresTransaction.Require(tx).Connection,
                "DO $$ BEGIN RAISE EXCEPTION 'injected deadlock' USING ERRCODE='40P01'; END $$", ct);
        }));
        Assert.Equal(3, attempts);
    }
    [Fact]
    public async Task EF_concurrency_token_rejects_stale_update()
    {
        var context = await db.Context(); var result = await db.Record(context, Guid.NewGuid().ToString(), "revision");
        async Task Update(long expected) => await db.Uow.ExecuteAsync(context, async (transaction, token) =>
        {
            var tx = PostgresTransaction.Require(transaction);
            var state = tx.Enlist(c => new ProbeStateContext(new DbContextOptionsBuilder<ProbeStateContext>().UseNpgsql(c).Options));
            var item = await state.Rows.SingleAsync(x => x.Id == result.Reference, token);
            state.Entry(item).Property(x => x.Revision).OriginalValue = expected; item.Revision++;
            await state.SaveChangesAsync(token); return 1;
        });
        await Update(0); await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Update(0));
    }
    [Fact]
    public async Task Tenant_company_RLS_and_missing_context_fail_closed()
    {
        var a = await db.Context();
        Assert.Equal(0, await db.Uow.ExecuteAsync(a, async (tx, token) =>
        {
            await using var command = Sql.Command(PostgresTransaction.Require(tx).Connection,
                "SELECT count(*) FROM foundation_test.scope_markers WHERE tenant_id=@other OR company_id=@company",
                ("other", DatabaseFixture.TenantB), ("company", DatabaseFixture.CompanyA2));
            return (long)(await command.ExecuteScalarAsync(token))!;
        }));
        await using var c = await db.Source.OpenConnectionAsync();
        await using var query = Sql.Command(c, "SELECT count(*) FROM foundation_test.scope_markers");
        Assert.Equal(0L, await query.ExecuteScalarAsync());
        var denied = await Assert.ThrowsAsync<PostgresException>(() => db.Uow.ExecuteAsync(a, (tx, token) =>
            Sql.ExecuteAsync(PostgresTransaction.Require(tx).Connection, "INSERT INTO foundation_test.scope_markers VALUES(@id,@tenant,@company)", token,
                ("id", Guid.NewGuid()), ("tenant", DatabaseFixture.TenantB), ("company", DatabaseFixture.CompanyB))));
        Assert.Equal("42501", denied.SqlState);
    }
    [Fact]
    public async Task Same_physical_connection_does_not_leak_scope_after_commit_rollback_or_cancel()
    {
        await using var pool = new NpgsqlDataSourceBuilder(new NpgsqlConnectionStringBuilder(db.Runtime) { MaxPoolSize = 1 }.ToString()).Build();
        var executor = new PostgresUnitOfWork(pool);
        var pidA = await executor.ExecuteAsync(await db.Context(), (tx, _) => Task.FromResult(PostgresTransaction.Require(tx).Connection.ProcessID));
        var pidB = await executor.ExecuteAsync(await db.Context(DatabaseFixture.TenantB, DatabaseFixture.CompanyB), async (tx, token) =>
        {
            await using var command = Sql.Command(PostgresTransaction.Require(tx).Connection, "SELECT count(*) FROM foundation_test.scope_markers WHERE tenant_id=@a", ("a", DatabaseFixture.TenantA));
            Assert.Equal(0L, await command.ExecuteScalarAsync(token)); return PostgresTransaction.Require(tx).Connection.ProcessID;
        });
        Assert.Equal(pidA, pidB);
        var context = await db.Context();
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync<int>(context, (_, _) => throw new InvalidOperationException()));
        using var cancel = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(context, async (tx, token) =>
        { await using var command = Sql.Command(PostgresTransaction.Require(tx).Connection, "SELECT pg_sleep(5)"); return await command.ExecuteNonQueryAsync(token); }, cancel.Token));
        await using var reused = await pool.OpenConnectionAsync(); await using var check = Sql.Command(reused,
            "SELECT count(*) FROM foundation_test.scope_markers"); Assert.Equal(0L, await check.ExecuteScalarAsync());
    }
    [Fact]
    public async Task Audit_cannot_be_updated_or_deleted_by_runtime()
    {
        var context = await db.Context();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Uow.ExecuteAsync(context,
            (tx, token) => Sql.ExecuteAsync(PostgresTransaction.Require(tx).Connection, "DELETE FROM audit.entries", token)));
        Assert.Equal("42501", error.SqlState);
    }
    [Fact]
    public async Task Clean_schema_migrations_reapply_idempotently_and_N_upgrades_preserve_data()
    {
        await db.Execute("CREATE DATABASE foundation_upgrade OWNER erp_migrator");
        var cs = new NpgsqlConnectionStringBuilder(db.Migration) { Database = "foundation_upgrade" }.ToString();
        await using var model = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(cs).Options);
        await model.GetService<IMigrator>().MigrateAsync("20261018000100_InitialFoundation");
        await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(db.Admin) { Database = "foundation_upgrade" }.ToString()))
        {
            await admin.OpenAsync(); await Sql.ExecuteAsync(admin, "INSERT INTO integration.outbox(id,tenant_id,company_id,type,source_id,payload,occurred_at,correlation_id,state,available_at,attempts) VALUES(@id,@tenant,@company,'upgrade.v1','before-upgrade','{}',now(),'upgrade','Pending',now(),0)", default,
                ("id", Guid.NewGuid()), ("tenant", DatabaseFixture.TenantA), ("company", DatabaseFixture.CompanyA));
        }
        await MigrationRunner.ApplyAsync(cs); await MigrationRunner.ApplyAsync(cs);
        await using var current = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(db.Admin) { Database = "foundation_upgrade" }.ToString()); await current.OpenAsync();
        await using var command = Sql.Command(current, "SELECT count(*) FROM integration.outbox WHERE source_id='before-upgrade'"); Assert.Equal(1L, await command.ExecuteScalarAsync());
        Assert.Equal(2, (await model.Database.GetAppliedMigrationsAsync()).Count()); Assert.False(model.Database.HasPendingModelChanges());
    }
}
