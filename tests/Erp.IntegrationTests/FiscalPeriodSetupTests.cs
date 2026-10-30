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
public sealed class FiscalPeriodSetupTests(DatabaseFixture db)
{
    private static readonly DateOnly JanStart = new(2027, 1, 1);
    private static readonly DateOnly JanEnd = new(2027, 1, 31);
    private CreateFiscalPeriodCommand Command => new(db.Commands, new FiscalPeriodStore(), db.Audit, db.Outbox, TimeProvider.System);
    private static CommandIdentity Identity(CreateFiscalPeriodRequest request, string key) => new(
        "Finance.CreateFiscalPeriod", "periods", key,
        RequestFingerprint.Create("POST", "periods", "0", JsonSerializer.Serialize(request)));

    [Fact]
    public async Task Golden_create_post_close_and_closed_post_protection()
    {
        var s = await TrialBalanceScenario.Create(db);
        var before = await s.Report(JanStart, JanEnd);
        var created = await db.CreatePeriod(s.Tenant, s.Company, JanStart, JanEnd);
        Assert.Equal(1, created.AppliedRevision);
        Assert.True(await db.Scalar<bool>("SELECT status='Open' AND revision=1 AND fiscal_year=2027 AND created_at IS NOT NULL AND created_by_subject='period-closer' FROM finance.fiscal_periods WHERE id=@id", ("id", created.Reference)));
        Assert.Equal(before.Totals, (await s.Report(JanStart, JanEnd)).Totals);
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND action='FiscalPeriodCreated'", ("id", created.Reference.ToString())));
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.fiscal-period-created.v1'", ("id", created.Reference.ToString())));
        var posted = await s.Post(s.Expense, s.Cash, "100", JanStart);
        Assert.Equal("Posted", await db.Scalar<string>("SELECT status FROM finance.journal_entries WHERE id=@id", ("id", posted.Reference)));
        var report = await s.Report(JanStart, JanEnd);
        Assert.Equal("100", report.Totals.PeriodDebit); Assert.True(report.Reconciliation.Passed);
        await db.ClosePeriod(created.Reference, s.Tenant, s.Company);
        var draft = await s.Draft(s.Expense, s.Cash, "10", JanStart);
        var journals = new JournalCommands(db.Uow, db.Commands, new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
        Assert.Equal(FinanceErrors.PeriodClosed, (await Assert.ThrowsAsync<FoundationException>(async () =>
            await journals.PostAsync(await s.Context(JournalPermissions.Post), draft.Reference, 1,
                new("Finance.PostManualJournal", draft.Reference.ToString(), "setup-after-close",
                    RequestFingerprint.Create("POST", draft.Reference.ToString(), "1", "{}"))))).Error);
        Assert.Equal(report.Totals, (await s.Report(JanStart, JanEnd)).Totals);
    }

    [Fact]
    public async Task Duplicate_overlap_inclusive_boundary_and_invalid_dates_rejected()
    {
        var s = await TrialBalanceScenario.Create(db);
        await db.CreatePeriod(s.Tenant, s.Company, JanStart, JanEnd);
        Assert.Equal(PeriodSetupErrors.Duplicate, (await Assert.ThrowsAsync<FoundationException>(() =>
            db.CreatePeriod(s.Tenant, s.Company, JanStart, JanEnd, "duplicate"))).Error);
        foreach (var (start, end) in new[] { (new DateOnly(2027, 1, 15), new DateOnly(2027, 2, 15)),
            (JanEnd, new DateOnly(2027, 2, 28)) })
            Assert.Equal(PeriodSetupErrors.Overlap, (await Assert.ThrowsAsync<FoundationException>(() =>
                db.CreatePeriod(s.Tenant, s.Company, start, end, Guid.NewGuid().ToString()))).Error);
        var adjacent = await db.CreatePeriod(s.Tenant, s.Company, new(2027, 2, 1), new(2027, 2, 28), "adjacent");
        Assert.NotEqual(Guid.Empty, adjacent.Reference);
        Assert.Equal(PeriodSetupErrors.InvalidDates, (await Assert.ThrowsAsync<FoundationException>(() =>
            db.CreatePeriod(s.Tenant, s.Company, new(2027, 4, 2), new(2027, 4, 1), "invalid"))).Error);
    }

    [Fact]
    public async Task Concurrent_overlap_and_same_key_retry_create_only_one_period()
    {
        var s = await TrialBalanceScenario.Create(db);
        var identical = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => db.CreatePeriod(s.Tenant, s.Company, JanStart, JanEnd, "same-key")));
        Assert.Single(identical.Select(x => x.Reference).Distinct());
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM finance.fiscal_periods WHERE id=@id", ("id", identical[0].Reference)));
        var attempts = await Task.WhenAll(new[] { (new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 31)),
            (new DateOnly(2027, 3, 15), new DateOnly(2027, 4, 15)) }.Select(async (range, index) =>
        {
            try { await db.CreatePeriod(s.Tenant, s.Company, range.Item1, range.Item2, "race-" + index); return true; }
            catch (FoundationException error) { Assert.Equal(PeriodSetupErrors.Overlap, error.Error); return false; }
        }));
        Assert.Single(attempts, x => x);
    }

    [Fact]
    public async Task Lost_response_retry_and_changed_key_payload_are_safe()
    {
        var s = await TrialBalanceScenario.Create(db);
        async Task Lose() { await db.CreatePeriod(s.Tenant, s.Company, JanStart, JanEnd, "lost"); throw new TimeoutException(); }
        await Assert.ThrowsAsync<TimeoutException>(Lose);
        var before = await s.StateDigest();
        var retry = await db.CreatePeriod(s.Tenant, s.Company, JanStart, JanEnd, "lost");
        Assert.Equal(before, await s.StateDigest());
        Assert.Equal(Errors.API_IDEMPOTENCY_CONFLICT, (await Assert.ThrowsAsync<FoundationException>(() =>
            db.CreatePeriod(s.Tenant, s.Company, new(2027, 2, 1), new(2027, 2, 28), "lost"))).Error);
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.fiscal-period-created.v1'", ("id", retry.Reference.ToString())));
    }

    [Fact]
    public async Task Cross_scope_and_permission_are_enforced()
    {
        var a = await TrialBalanceScenario.Create(db);
        var b = await TrialBalanceScenario.Create(db, tenant: a.Tenant);
        var c = await TrialBalanceScenario.Create(db);
        var request = new CreateFiscalPeriodRequest(JanStart, JanEnd);
        foreach (var permission in new[] { PeriodPermissions.View, PeriodPermissions.Close, JournalPermissions.Post })
            Assert.Equal(Errors.AUTH_SCOPE_DENIED, (await Assert.ThrowsAsync<FoundationException>(async () =>
                await Command.ExecuteAsync(await db.PeriodContext(a.Tenant, a.Company, permission), request,
                    Identity(request, permission)))).Error);
        var first = await db.CreatePeriod(a.Tenant, a.Company, JanStart, JanEnd);
        foreach (var other in new[] { b, c })
        {
            var own = await db.CreatePeriod(other.Tenant, other.Company, JanStart, JanEnd);
            Assert.NotEqual(first.Reference, own.Reference);
            var listed = await new FiscalPeriodQueries(db.Uow, new FiscalPeriodStore())
                .ListAsync(await db.PeriodContext(other.Tenant, other.Company, PeriodPermissions.View));
            Assert.Contains(listed, x => x.Id == own.Reference);
            Assert.DoesNotContain(listed, x => x.Id == first.Reference);
        }
    }

    [Fact]
    public async Task Failure_after_insert_rolls_back_period_audit_outbox_and_receipt()
    {
        var s = await TrialBalanceScenario.Create(db);
        var request = new CreateFiscalPeriodRequest(JanStart, JanEnd);
        var failing = new CreateFiscalPeriodCommand(new FailAfterWork(db.Commands), new FiscalPeriodStore(), db.Audit, db.Outbox, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await failing.ExecuteAsync(
            await db.PeriodContext(s.Tenant, s.Company, PeriodPermissions.Create), request, Identity(request, "failed")));
        Assert.Equal(0L, await db.Scalar<long>("SELECT count(*) FROM finance.fiscal_periods WHERE tenant_id=@t AND company_id=@c AND starts_on=@d", ("t", s.Tenant), ("c", s.Company), ("d", JanStart)));
        Assert.Equal(0L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE tenant_id=@t AND company_id=@c AND type='finance.fiscal-period-created.v1'", ("t", s.Tenant), ("c", s.Company)));
        await db.CreatePeriod(s.Tenant, s.Company, JanStart, JanEnd, "failed");
    }
    private sealed class FailAfterWork(IIdempotentCommands inner) : IIdempotentCommands
    {
        public Task<CommandReceipt> ExecuteAsync(VerifiedContext context, CommandIdentity identity,
            Func<IApplicationTransaction, CancellationToken, Task<CommandReceipt>> work, CancellationToken token = default) =>
            inner.ExecuteAsync(context, identity, async (tx, ct) => { await work(tx, ct); throw new InvalidOperationException("injected"); }, token);
    }

    [Fact]
    public async Task Runtime_direct_insert_without_audit_outbox_is_rejected_and_RLS_remains_forced()
    {
        var s = await TrialBalanceScenario.Create(db);
        Assert.True(await db.Scalar<bool>("SELECT relforcerowsecurity FROM pg_class WHERE oid='finance.fiscal_periods'::regclass"));
        await using var connection = new NpgsqlConnection(db.Runtime); await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await Sql.ExecuteAsync(connection, "SELECT set_config('erp.tenant_id',@t,true),set_config('erp.company_id',@c,true)", default,
            ("t", s.Tenant.ToString()), ("c", s.Company.ToString()));
        await Sql.ExecuteAsync(connection, "INSERT INTO finance.fiscal_periods(id,tenant_id,company_id,fiscal_year,starts_on,ends_on,status,revision,created_at,created_by_issuer,created_by_subject) VALUES(gen_random_uuid(),@t,@c,2027,'2027-01-01','2027-01-31','Open',1,transaction_timestamp(),'tests','direct')", default,
            ("t", s.Tenant), ("c", s.Company));
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync())).SqlState);
    }

    [Fact]
    public async Task HTTP_create_requires_independent_permission_and_stable_idempotency()
    {
        var s = await FinanceScenario.Create(db);
        await using var factory = new ApiFactory(db);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token);
        var path = $"/api/v1/tenants/{s.Tenant}/companies/{s.Company}/finance/periods";
        async Task<HttpResponseMessage> Create(string key, DateOnly start, DateOnly end)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            { Content = JsonContent.Create(new CreateFiscalPeriodRequest(start, end)) };
            if (key.Length > 0) request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }
        foreach (var permission in new[] { PeriodPermissions.Close, PeriodPermissions.View, JournalPermissions.Post })
        {
            factory.Members.Permissions.Clear(); factory.Members.Permissions.Add(permission);
            Assert.Equal(HttpStatusCode.Forbidden, (await Create("http-create", JanStart, JanEnd)).StatusCode);
        }
        factory.Members.Permissions.Clear(); factory.Members.Permissions.Add(PeriodPermissions.Create);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create("", JanStart, JanEnd)).StatusCode);
        var invalid = await Create("http-invalid", new(2027, 2, 1), new(2027, 1, 31));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Equal("FIN_PERIOD_INVALID_DATES", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorId").GetString());
        var first = await Create("http-create", JanStart, JanEnd); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var retry = await Create("http-create", JanStart, JanEnd); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(await first.Content.ReadFromJsonAsync<CommandReceipt>(), await retry.Content.ReadFromJsonAsync<CommandReceipt>());
        var conflict = await Create("http-create", new(2027, 2, 1), new(2027, 2, 28));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("API_IDEMPOTENCY_CONFLICT", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorId").GetString());
        using var foreign = new HttpRequestMessage(HttpMethod.Post,
            path.Replace(s.Company.ToString(), DatabaseFixture.CompanyA2.ToString(), StringComparison.Ordinal))
        { Content = JsonContent.Create(new CreateFiscalPeriodRequest(new(2027, 2, 1), new(2027, 2, 28))) };
        foreign.Headers.Add("Idempotency-Key", "foreign-company");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreign)).StatusCode);
    }

    [Fact]
    public async Task Created_event_is_inbox_deduplicated_and_does_not_modify_period()
    {
        var s = await TrialBalanceScenario.Create(db);
        var created = await db.CreatePeriod(s.Tenant, s.Company, JanStart, JanEnd);
        var payload = await db.Scalar<string>("SELECT payload::text FROM integration.outbox WHERE source_id=@id AND type='finance.fiscal-period-created.v1'", ("id", created.Reference.ToString()));
        var eventId = await db.Scalar<Guid>("SELECT id FROM integration.outbox WHERE source_id=@id AND type='finance.fiscal-period-created.v1'", ("id", created.Reference.ToString()));
        var message = new IntegrationEvent(eventId, FiscalPeriodCreatedV1.EventType, created.Reference.ToString(), payload, DateTimeOffset.UtcNow);
        var context = await new ContextResolver(new WorkerMembership(s.Tenant, s.Company)).ResolveAsync(
            new("tests", "period-setup-worker"), s.Tenant, s.Company, "outbox.dispatch", "setup-delivery", "setup-worker-request", ExecutionSource.Worker);
        var handler = new FiscalPeriodCreatedTraceHandler(new FiscalPeriodStore(), db.Audit);
        var before = await db.Scalar<string>("SELECT to_jsonb(p)::text FROM finance.fiscal_periods p WHERE id=@id", ("id", created.Reference));
        for (var attempt = 0; attempt < 2; attempt++)
            await db.Inbox.ProcessAsync(context, handler.Consumer, message, (tx, ct) => handler.HandleAsync(tx, message, ct));
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND action='FiscalPeriodCreatedEventDelivered'", ("id", created.Reference.ToString())));
        Assert.Equal(before, await db.Scalar<string>("SELECT to_jsonb(p)::text FROM finance.fiscal_periods p WHERE id=@id", ("id", created.Reference)));
    }
    private sealed class WorkerMembership(Guid tenant, Guid company) : IMembershipStore
    {
        public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken token) =>
            ValueTask.FromResult<PrincipalMembership?>(new(true,
                [new(tenant, company, new HashSet<string> { "outbox.dispatch" })]));
    }
}
