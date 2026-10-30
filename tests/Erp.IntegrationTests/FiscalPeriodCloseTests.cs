using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Application;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Finance.Infrastructure;
using Erp.Kernel;
using Erp.Platform.Infrastructure;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class FiscalPeriodCloseTests(DatabaseFixture db)
{
    private CloseFiscalPeriodCommand Command => new(db.Commands, new FiscalPeriodStore(), db.Audit, db.Outbox, TimeProvider.System);
    private static CommandIdentity Identity(Guid id, long expected, string key, ClosePeriodRequest request) =>
        new("Finance.CloseFiscalPeriod", id.ToString(), key, RequestFingerprint.Create("POST", id.ToString(),
            expected.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Text.Json.JsonSerializer.Serialize(request)));
    [Fact]
    public async Task Golden_period_closes_after_reconciliation_and_preserves_report_values()
    {
        var s = await TrialBalanceScenario.Create(db);
        var posted = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        var before = await s.Report(new(2026, 1, 1), new(2026, 12, 31));
        var receipt = await db.ClosePeriod(s.Period2026, s.Tenant, s.Company);
        Assert.Equal(s.Period2026, receipt.Reference);
        Assert.Equal(2, receipt.AppliedRevision);
        Assert.True(await db.Scalar<bool>("SELECT status='Closed' AND revision=2 AND closed_at IS NOT NULL AND closed_by_subject='period-closer' FROM finance.fiscal_periods WHERE id=@id", ("id", s.Period2026)));
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND action='FiscalPeriodClosed'", ("id", s.Period2026.ToString())));
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.fiscal-period-closed.v1'", ("id", s.Period2026.ToString())));
        var after = await s.Report(new(2026, 1, 1), new(2026, 12, 31));
        Assert.Equal(before.Totals, after.Totals);
        Assert.True(after.Reconciliation.Passed);
        Assert.Equal("Posted", await db.Scalar<string>("SELECT status FROM finance.journal_entries WHERE id=@id", ("id", posted.Reference)));
    }

    [Fact]
    public async Task Pre_close_preview_is_read_only_and_reports_balanced_ledger_and_GL()
    {
        var s = await TrialBalanceScenario.Create(db); await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        var before = await s.StateDigest();
        var queries = new FiscalPeriodQueries(db.Uow, new FiscalPeriodStore());
        var context = await db.PeriodContext(s.Tenant, s.Company);
        var preview = await queries.PreviewAsync(context, s.Period2026);
        Assert.True(preview.Passed);
        Assert.Equal("100", preview.PeriodDebit); Assert.Equal("100", preview.PeriodCredit);
        Assert.Contains(preview.Checks, x => x.Code == "GL_TB_PERIOD_ACCOUNTS" && x.Passed);
        Assert.Contains(preview.Checks, x => x.Code == "TB_LEDGER_JOURNAL_INTEGRITY" && x.Passed);
        Assert.Equal(before, await s.StateDigest());
        Assert.Contains(await queries.ListAsync(await db.PeriodContext(s.Tenant, s.Company, PeriodPermissions.View)), x => x.Id == s.Period2026 && x.Status == "Open");
    }

    [Fact]
    public async Task Same_key_retries_after_lost_response_without_extra_effect_and_changed_request_conflicts()
    {
        var s = await TrialBalanceScenario.Create(db); var id = s.Period2026;
        async Task LostResponse() { await db.ClosePeriod(id, s.Tenant, s.Company); throw new TimeoutException("Lost after database commit"); }
        await Assert.ThrowsAsync<TimeoutException>(LostResponse);
        var before = await s.StateDigest();
        var result = await db.ClosePeriod(id, s.Tenant, s.Company);
        Assert.Equal(2, result.AppliedRevision); Assert.Equal(before, await s.StateDigest());
        Assert.Equal(PeriodCloseErrors.AlreadyClosed, (await Assert.ThrowsAsync<FoundationException>(() =>
            db.ClosePeriod(id, s.Tenant, s.Company, "new-key"))).Error);
        Assert.Equal(Errors.API_IDEMPOTENCY_CONFLICT, (await Assert.ThrowsAsync<FoundationException>(() =>
            db.ClosePeriod(id, s.Tenant, s.Company, comment: "Different"))).Error);
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.fiscal-period-closed.v1'", ("id", id.ToString())));
    }

    [Fact]
    public async Task Concurrent_same_and_different_keys_produce_one_close()
    {
        var s = await TrialBalanceScenario.Create(db); var id = s.Period2026;
        var same = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => db.ClosePeriod(id, s.Tenant, s.Company)));
        Assert.Single(same.Select(x => x.AppliedRevision).Distinct());
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND action='FiscalPeriodClosed'", ("id", id.ToString())));
        var another = await TrialBalanceScenario.Create(db);
        async Task<bool> Attempt(int n)
        {
            try { await db.ClosePeriod(another.Period2026, another.Tenant, another.Company, "close-" + n); return true; }
            catch (FoundationException error) { Assert.Equal(PeriodCloseErrors.AlreadyClosed, error.Error); return false; }
        }
        Assert.Single(await Task.WhenAll(Enumerable.Range(0, 6).Select(Attempt)), x => x);
    }

    [Fact]
    public async Task Permission_and_cross_scope_are_enforced_before_close()
    {
        var a = await TrialBalanceScenario.Create(db);
        var sameTenant = await TrialBalanceScenario.Create(db, tenant: a.Tenant);
        var otherTenant = await TrialBalanceScenario.Create(db);
        var request = new ClosePeriodRequest(null);
        foreach (var permission in new[] { JournalPermissions.View, JournalPermissions.Post, JournalPermissions.Reverse })
        {
            var context = await a.Context(permission);
            Assert.Equal(Errors.AUTH_SCOPE_DENIED, (await Assert.ThrowsAsync<FoundationException>(() =>
                Command.ExecuteAsync(context, a.Period2026, 1, request, Identity(a.Period2026, 1, permission, request)))).Error);
        }
        foreach (var other in new[] { sameTenant, otherTenant })
            Assert.Equal(Errors.RESOURCE_NOT_FOUND, (await Assert.ThrowsAsync<FoundationException>(() =>
                db.ClosePeriod(a.Period2026, other.Tenant, other.Company))).Error);
        Assert.Equal("Open", await db.Scalar<string>("SELECT status FROM finance.fiscal_periods WHERE id=@id", ("id", a.Period2026)));
    }

    [Fact]
    public async Task Failed_reconciliation_leaves_period_open_and_no_success_effect()
    {
        var s = await TrialBalanceScenario.Create(db);
        var posted = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        var line = await db.Scalar<Guid>("SELECT id FROM finance.journal_lines WHERE journal_id=@id AND credit>0", ("id", posted.Reference));
        await db.Execute("ALTER TABLE finance.journal_lines DISABLE TRIGGER guard_line");
        try { await db.Execute("UPDATE finance.journal_lines SET credit=99 WHERE id=@id", ("id", line)); }
        finally { await db.Execute("ALTER TABLE finance.journal_lines ENABLE TRIGGER guard_line"); }
        try
        {
            var query = new FiscalPeriodQueries(db.Uow, new FiscalPeriodStore());
            var preview = await query.PreviewAsync(await db.PeriodContext(s.Tenant, s.Company), s.Period2026);
            Assert.False(preview.Passed);
            Assert.Contains(preview.Checks, x => x.Code == "TB_PERIOD_BALANCED" && !x.Passed);
            Assert.Contains(preview.Checks, x => x.Code == "TB_LEDGER_JOURNAL_INTEGRITY" && !x.Passed);
            Assert.Equal(PeriodCloseErrors.ReconciliationFailed, (await Assert.ThrowsAsync<FoundationException>(() =>
                db.ClosePeriod(s.Period2026, s.Tenant, s.Company))).Error);
            Assert.True(await db.Scalar<bool>("SELECT status='Open' AND closed_at IS NULL FROM finance.fiscal_periods WHERE id=@id", ("id", s.Period2026)));
            Assert.Equal(0L, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND action='FiscalPeriodClosed'", ("id", s.Period2026.ToString())));
            Assert.Equal(0L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.fiscal-period-closed.v1'", ("id", s.Period2026.ToString())));
        }
        finally
        {
            await db.Execute("ALTER TABLE finance.journal_lines DISABLE TRIGGER guard_line");
            try { await db.Execute("UPDATE finance.journal_lines SET credit=100 WHERE id=@id", ("id", line)); }
            finally { await db.Execute("ALTER TABLE finance.journal_lines ENABLE TRIGGER guard_line"); }
        }
    }

    [Fact]
    public async Task Direct_status_toggle_duplicate_period_and_closed_metadata_mutation_are_blocked()
    {
        var s = await TrialBalanceScenario.Create(db);
        var direct = await Assert.ThrowsAsync<PostgresException>(() => db.Execute("UPDATE finance.fiscal_periods SET status='Closed' WHERE id=@id", ("id", s.Period2026)));
        Assert.Equal("23514", direct.SqlState);
        var overlap = await Assert.ThrowsAsync<PostgresException>(() => db.Execute("INSERT INTO finance.fiscal_periods(id,tenant_id,company_id,fiscal_year,starts_on,ends_on,status) VALUES(gen_random_uuid(),@t,@c,2026,'2026-06-01','2026-06-30','Open')", ("t", s.Tenant), ("c", s.Company)));
        Assert.Equal("23514", overlap.SqlState);
        await db.ClosePeriod(s.Period2026, s.Tenant, s.Company);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Execute("UPDATE finance.fiscal_periods SET closed_by_subject='other' WHERE id=@id", ("id", s.Period2026)))).SqlState);
    }

    [Fact]
    public async Task Close_blocks_post_and_reversal_in_period_but_allows_other_open_period()
    {
        var s = await TrialBalanceScenario.Create(db);
        var original = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        await db.ClosePeriod(s.Period2026, s.Tenant, s.Company);
        var draft = await s.Draft(s.Expense, s.Cash, "10", new(2026, 1, 2));
        var journals = new JournalCommands(db.Uow, db.Commands, new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
        var identity = new CommandIdentity("Finance.PostManualJournal", draft.Reference.ToString(), "post-after-close",
            RequestFingerprint.Create("POST", draft.Reference.ToString(), "1", "{}"));
        Assert.Equal(FinanceErrors.PeriodClosed, (await Assert.ThrowsAsync<FoundationException>(async () =>
            await journals.PostAsync(await s.Context(JournalPermissions.Post), draft.Reference, 1, identity))).Error);
        var request = new ReverseJournalRequest(new(2026, 1, 2), "Correct");
        var reversal = new ReverseJournalCommand(db.Commands, new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
        Assert.Equal(ReversalErrors.PeriodClosed, (await Assert.ThrowsAsync<FoundationException>(async () =>
            await reversal.ExecuteAsync(await s.Context(JournalPermissions.Reverse), original.Reference, 2, request,
                new("Finance.ReverseManualJournal", original.Reference.ToString(), "reverse-after-close",
                    RequestFingerprint.Create("POST", original.Reference.ToString(), "2", System.Text.Json.JsonSerializer.Serialize(request)))))).Error);
        var later = await s.Post(s.Expense, s.Cash, "20", new(2025, 12, 31));
        Assert.NotEqual(original.Reference, later.Reference);
        Assert.Equal("Posted", await db.Scalar<string>("SELECT status FROM finance.journal_entries WHERE id=@id", ("id", later.Reference)));
    }

    [Fact]
    public async Task Failure_after_period_write_rolls_back_metadata_audit_outbox_and_receipt()
    {
        var s = await TrialBalanceScenario.Create(db);
        var command = new CloseFiscalPeriodCommand(new FailAfterWork(db.Commands), new FiscalPeriodStore(), db.Audit, db.Outbox, TimeProvider.System);
        var request = new ClosePeriodRequest("Rollback test");
        var context = await db.PeriodContext(s.Tenant, s.Company);
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync(
            context, s.Period2026, 1, request,
            Identity(s.Period2026, 1, "fail-close", request)));
        Assert.True(await db.Scalar<bool>("SELECT status='Open' AND revision=1 AND closed_at IS NULL AND closed_by_subject IS NULL FROM finance.fiscal_periods WHERE id=@id", ("id", s.Period2026)));
        Assert.Equal(0L, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND entity_type='FiscalPeriod'", ("id", s.Period2026.ToString())));
        Assert.Equal(0L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.fiscal-period-closed.v1'", ("id", s.Period2026.ToString())));
        await db.ClosePeriod(s.Period2026, s.Tenant, s.Company, "fail-close", "Rollback test");
    }
    private sealed class FailAfterWork(IIdempotentCommands inner) : IIdempotentCommands
    {
        public Task<CommandReceipt> ExecuteAsync(VerifiedContext context, CommandIdentity identity,
            Func<IApplicationTransaction, CancellationToken, Task<CommandReceipt>> work, CancellationToken token = default) =>
            inner.ExecuteAsync(context, identity, async (tx, ct) =>
            {
                await work(tx, ct);
                throw new InvalidOperationException("Injected after period close work");
            }, token);
    }

    [Fact]
    public async Task HTTP_contract_requires_independent_permission_precondition_and_idempotency()
    {
        var s = await FinanceScenario.Create(db);
        await using var factory = new ApiFactory(db);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token);
        var basePath = $"/api/v1/tenants/{s.Tenant}/companies/{s.Company}/finance/periods";
        async Task<HttpResponseMessage> Close(string id, string key, string comment = "HTTP close", bool match = true)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, basePath + "/" + id + "/close")
            { Content = JsonContent.Create(new ClosePeriodRequest(comment)) };
            if (match) req.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            if (key.Length > 0) req.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(req);
        }
        foreach (var permission in new[] { JournalPermissions.View, JournalPermissions.Post, JournalPermissions.Reverse, PeriodPermissions.View })
        {
            factory.Members.Permissions.Clear(); factory.Members.Permissions.Add(permission);
            Assert.Equal(HttpStatusCode.Forbidden, (await Close(s.Period.ToString(), "http-close")).StatusCode);
        }
        factory.Members.Permissions.Clear(); factory.Members.Permissions.UnionWith([PeriodPermissions.View, PeriodPermissions.Close]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(basePath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(basePath + "/" + s.Period + "/pre-close")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Close(s.Period.ToString(), "http-close", match: false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Close(s.Period.ToString(), "")).StatusCode);
        var first = await Close(s.Period.ToString(), "http-close"); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var retry = await Close(s.Period.ToString(), "http-close"); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(await first.Content.ReadFromJsonAsync<CommandReceipt>(), await retry.Content.ReadFromJsonAsync<CommandReceipt>());
        var conflict = await Close(s.Period.ToString(), "http-close", "Changed");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("API_IDEMPOTENCY_CONFLICT", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorId").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Close(s.Period.ToString(), "second-key")).StatusCode);
        var foreign = basePath.Replace(s.Company.ToString(), DatabaseFixture.CompanyA2.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(foreign)).StatusCode);
    }

    [Fact]
    public async Task Versioned_close_event_is_inbox_deduplicated_and_cannot_change_financial_state()
    {
        var s = await TrialBalanceScenario.Create(db);
        await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        await db.ClosePeriod(s.Period2026, s.Tenant, s.Company);
        var payload = await db.Scalar<string>("SELECT payload::text FROM integration.outbox WHERE source_id=@id AND type='finance.fiscal-period-closed.v1'", ("id", s.Period2026.ToString()));
        var eventId = await db.Scalar<Guid>("SELECT id FROM integration.outbox WHERE source_id=@id AND type='finance.fiscal-period-closed.v1'", ("id", s.Period2026.ToString()));
        var message = new IntegrationEvent(eventId, FiscalPeriodClosedV1.EventType, s.Period2026.ToString(), payload, DateTimeOffset.UtcNow);
        var context = await new ContextResolver(new WorkerMembership(s.Tenant, s.Company)).ResolveAsync(
            new("tests", "period-worker"), s.Tenant, s.Company, "outbox.dispatch", "period-delivery", "period-worker-request", ExecutionSource.Worker);
        var handler = new FiscalPeriodClosedTraceHandler(new FiscalPeriodStore(), db.Audit);
        var before = await s.Report(new(2026, 1, 1), new(2026, 12, 31));
        for (var attempt = 0; attempt < 2; attempt++)
            await db.Inbox.ProcessAsync(context, handler.Consumer, message, (tx, ct) => handler.HandleAsync(tx, message, ct));
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND action='FiscalPeriodClosedEventDelivered'", ("id", s.Period2026.ToString())));
        Assert.Equal(before.Totals, (await s.Report(new(2026, 1, 1), new(2026, 12, 31))).Totals);
    }
    private sealed class WorkerMembership(Guid tenant, Guid company) : IMembershipStore
    {
        public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken token) =>
            ValueTask.FromResult<PrincipalMembership?>(new(true, [new(tenant, company, new HashSet<string> { "outbox.dispatch" })]));
    }

    [Fact]
    public async Task Previous_finance_schema_with_legacy_closed_period_upgrades_without_invented_actor()
    {
        var name = "period_upgrade_" + Guid.NewGuid().ToString("N");
        await db.Execute($"CREATE DATABASE {name} OWNER erp_migrator");
        try
        {
            var migration = new NpgsqlConnectionStringBuilder(db.Migration) { Database = name, IncludeErrorDetail = true }.ToString();
            await MigrationRunner.ApplyAsync(migration);
            await using var connection = new NpgsqlConnection(migration); await connection.OpenAsync();
            await using var schema = new FinanceDbContext(FinanceDbContext.Options(connection));
            await schema.GetService<IMigrator>().MigrateAsync("20261020202816_ManualJournalReversalV1");
            await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(db.Admin) { Database = name }.ToString()))
            {
                await admin.OpenAsync();
                await Sql.ExecuteAsync(admin, "INSERT INTO finance.accounting_books(tenant_id,company_id,currency,minor_units) VALUES(@t,@c,'JOD',3); INSERT INTO finance.fiscal_periods(id,tenant_id,company_id,fiscal_year,starts_on,ends_on,status) VALUES(@id,@t,@c,2026,'2026-01-01','2026-01-31','Closed')", default,
                    ("t", DatabaseFixture.TenantA), ("c", DatabaseFixture.CompanyA), ("id", Guid.NewGuid()));
            }
            await schema.GetService<IMigrator>().MigrateAsync();
            Assert.Equal(7, (await schema.Database.GetAppliedMigrationsAsync()).Count());
            Assert.False(schema.Database.HasPendingModelChanges());
            await using var inspect = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(db.Admin) { Database = name }.ToString());
            await inspect.OpenAsync();
            await using var check = Sql.Command(inspect, "SELECT count(*) FROM finance.fiscal_periods WHERE status='Closed' AND revision=2 AND closed_at IS NOT NULL AND closed_by_issuer='migration' AND closed_by_subject='legacy-closed-period'");
            Assert.Equal(1L, await check.ExecuteScalarAsync());
        }
        finally { await db.Execute($"DROP DATABASE {name} WITH (FORCE)"); }
    }
}
