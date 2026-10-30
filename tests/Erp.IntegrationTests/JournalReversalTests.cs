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
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class JournalReversalTests(DatabaseFixture db)
{
    private ReverseJournalCommand Command => new(db.Commands, new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
    private JournalQueries Queries => new(db.Uow, new AuditReader());
    internal static CommandIdentity Identity(Guid id, string key, ReverseJournalRequest request, long revision = 2) => new("Finance.ReverseManualJournal", id.ToString(), key,
        RequestFingerprint.Create("POST", id.ToString(), revision.ToString(System.Globalization.CultureInfo.InvariantCulture), JsonSerializer.Serialize(request)));
    private async Task<CommandReceipt> Reverse(TrialBalanceScenario s, Guid id, string key = "reverse", DateOnly? date = null, string reason = "Correction", long expected = 2, ReverseJournalCommand? command = null)
    {
        var request = new ReverseJournalRequest(date ?? new(2026, 1, 1), reason);
        return await (command ?? Command).ExecuteAsync(await s.Context(JournalPermissions.Reverse), id, expected, request, Identity(id, key, request, expected));
    }
    private Task<string> OriginalDigest(Guid id) => db.Scalar<string>("SELECT md5(string_agg(value,'|' ORDER BY value)) FROM (SELECT to_jsonb(j)::text value FROM finance.journal_entries j WHERE id=@id UNION ALL SELECT to_jsonb(l)::text FROM finance.journal_lines l WHERE journal_id=@id) original", ("id", id));
    [Fact]
    public async Task Golden_reversal_has_exact_opposite_lines_new_number_immutable_original_and_both_relationships()
    {
        var s = await TrialBalanceScenario.Create(db); var original = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        var before = await OriginalDigest(original.Reference); var result = await Reverse(s, original.Reference);
        var o = await Queries.GetAsync(await s.Context(), original.Reference); var r = await Queries.GetAsync(await s.Context(), result.Reference);
        Assert.NotEqual(o.Id, r.Id); Assert.NotEqual(o.JournalNumber, r.JournalNumber); Assert.Equal("Posted", o.Status); Assert.Equal("Posted", r.Status); Assert.Equal(before, await OriginalDigest(o.Id));
        Assert.Equal(o.TotalCredit, r.TotalDebit); Assert.Equal(o.TotalDebit, r.TotalCredit); Assert.Equal("0", r.Difference);
        Assert.All(o.Lines.Zip(r.Lines), pair => { Assert.Equal(pair.First.AccountId, pair.Second.AccountId); Assert.Equal(pair.First.Debit, pair.Second.Credit); Assert.Equal(pair.First.Credit, pair.Second.Debit); Assert.NotEqual(pair.First.Id, pair.Second.Id); });
        Assert.Equal(o.Reversal, r.Reversal); Assert.Equal(o.Id, r.Reversal!.OriginalJournalId); Assert.Equal(r.Id, o.Reversal!.ReversalJournalId); Assert.Equal("Correction", o.Reversal.Reason);
        var audit = await Queries.AuditAsync(await s.Context(), o.Id);
        foreach (var action in new[] { "JournalReversalRequested", "JournalReversed" })
        { var a = Assert.Single(audit, x => x.Action == action); Assert.Equal(o.Reversal, a.Reversal); Assert.Equal(s.Tenant, a.TenantId); Assert.Equal(s.Company, a.CompanyId); Assert.Equal("reader", a.ActorSubject); Assert.Equal(o.Reversal.RequestId, a.RequestId); }
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.journal-entry-reversed.v1'", ("id", o.Id.ToString())));
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.journal-entry-posted.v1'", ("id", r.Id.ToString())));
    }
    [Fact]
    public async Task Closed_original_period_stays_closed_open_reversal_period_and_reports_preserve_real_history()
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, "100", new(2025, 12, 31)); var before = await OriginalDigest(o.Reference);
        var priorPeriod = await db.Scalar<Guid>("SELECT id FROM finance.fiscal_periods WHERE tenant_id=@t AND company_id=@c AND fiscal_year=2025", ("t", s.Tenant), ("c", s.Company));
        await db.ClosePeriod(priorPeriod, s.Tenant, s.Company);
        await Reverse(s, o.Reference);
        Assert.Equal(before, await OriginalDigest(o.Reference)); Assert.Equal("Closed", await db.Scalar<string>("SELECT status FROM finance.fiscal_periods WHERE tenant_id=@t AND company_id=@c AND fiscal_year=2025", ("t", s.Tenant), ("c", s.Company)));
        var first = await s.Report(new(2025, 12, 1), new(2025, 12, 31)); var second = await s.Report(new(2026, 1, 1), new(2026, 1, 31)); var both = await s.Report(new(2025, 12, 1), new(2026, 1, 31));
        Assert.Equal(new("0", "0", "100", "0", "100", "0"), s.Account(first, s.Expense)); Assert.Equal(new("100", "0", "0", "100", "0", "0"), s.Account(second, s.Expense));
        Assert.Equal(new("0", "0", "100", "100", "0", "0"), s.Account(both, s.Expense)); Assert.True(first.Reconciliation.Passed && second.Reconciliation.Passed && both.Reconciliation.Passed);
        var gl = await new GeneralLedgerQuery(new GeneralLedgerReader(db.Source, NullLogger<GeneralLedgerReader>.Instance)).RunAsync(await s.Context(), s.Expense, new(2025, 12, 1), new(2026, 1, 31));
        Assert.Equal(["100", "0"], gl.Movements.Select(x => x.RunningBalance)); Assert.Equal(2, gl.TotalCount); Assert.True(gl.Reconciliation.Passed);
    }
    [Fact]
    public async Task Draft_already_reversed_and_reversal_chain_rejected_with_stable_errors()
    {
        var s = await TrialBalanceScenario.Create(db); var draft = await s.Draft(s.Expense, s.Cash, "100", new(2026, 1, 1));
        Assert.Equal(ReversalErrors.NotPosted, (await Assert.ThrowsAsync<FoundationException>(() => Reverse(s, draft.Reference, expected: 1))).Error);
        var posted = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1)); var r = await Reverse(s, posted.Reference);
        Assert.Equal(ReversalErrors.AlreadyReversed, (await Assert.ThrowsAsync<FoundationException>(() => Reverse(s, posted.Reference, "different-key"))).Error);
        Assert.Equal(ReversalErrors.NotAllowed, (await Assert.ThrowsAsync<FoundationException>(() => Reverse(s, r.Reference))).Error);
    }
    [Fact]
    public async Task Reversal_date_reason_closed_target_and_stale_revision_validation_roll_back()
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, "100", new(2025, 12, 31)); var before = await s.StateDigest();
        Assert.Equal(Errors.DOC_CONCURRENCY_CONFLICT, (await Assert.ThrowsAsync<FoundationException>(() => Reverse(s, o.Reference, expected: 1))).Error);
        Assert.Equal(ReversalErrors.DateInvalid, (await Assert.ThrowsAsync<FoundationException>(() => Reverse(s, o.Reference, date: new(2027, 1, 1)))).Error);
        Assert.Equal(Errors.VALIDATION_FAILED, (await Assert.ThrowsAsync<FoundationException>(() => Reverse(s, o.Reference, reason: " "))).Error);
        var request = new ReverseJournalRequest(null, "Correction"); Assert.Equal(ReversalErrors.DateInvalid, (await Assert.ThrowsAsync<FoundationException>(async () => await Command.ExecuteAsync(await s.Context(JournalPermissions.Reverse), o.Reference, 2, request, Identity(o.Reference, "missing-date", request)))).Error);
        Assert.Equal(before, await s.StateDigest()); await db.ClosePeriod(s.Period2026, s.Tenant, s.Company); before = await s.StateDigest();
        Assert.Equal(ReversalErrors.PeriodClosed, (await Assert.ThrowsAsync<FoundationException>(() => Reverse(s, o.Reference))).Error); Assert.Equal(before, await s.StateDigest());
    }
    [Fact]
    public async Task Current_inactive_or_nonpostable_accounts_cannot_bypass_posting_invariants()
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        await db.Execute("UPDATE finance.accounts SET status='Inactive' WHERE id=@id", ("id", s.Cash));
        Assert.Equal(FinanceErrors.InactiveAccount, (await Assert.ThrowsAsync<FoundationException>(() => Reverse(s, o.Reference))).Error);
        await db.Execute("UPDATE finance.accounts SET status='Active',posting_allowed=false WHERE id=@id", ("id", s.Cash));
        Assert.Equal(FinanceErrors.NonPostableAccount, (await Assert.ThrowsAsync<FoundationException>(() => Reverse(s, o.Reference))).Error);
    }
    [Fact]
    public async Task Sequential_concurrent_same_key_and_response_loss_retries_have_one_effect()
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Reverse(s, o.Reference))); Assert.Single(results.Select(x => x.Reference).Distinct());
        var before = await s.StateDigest(); var replay = await Reverse(s, o.Reference); Assert.Equal(results[0], replay); Assert.Equal(before, await s.StateDigest());
        // Real DB commit completes, but the client-facing wrapper loses the receipt/throws timeout. Retrying recovers it.
        var second = await s.Post(s.Expense, s.Cash, "40", new(2026, 1, 2));
        async Task LoseResponse() { await Reverse(s, second.Reference); throw new TimeoutException("Simulated transport loss AFTER commit"); }
        await Assert.ThrowsAsync<TimeoutException>(LoseResponse); before = await s.StateDigest(); await Reverse(s, second.Reference); Assert.Equal(before, await s.StateDigest());
        Assert.Equal(2L, await db.Scalar<long>("SELECT count(*) FROM finance.journal_reversals WHERE tenant_id=@t AND company_id=@c", ("t", s.Tenant), ("c", s.Company)));
    }
    [Fact]
    public async Task Different_keys_competing_for_same_original_produce_one_success()
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        async Task<bool> Attempt(string key) { try { await Reverse(s, o.Reference, key); return true; } catch (FoundationException e) { Assert.Equal(ReversalErrors.AlreadyReversed, e.Error); return false; } }
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(n => Attempt("reverse-" + n))); Assert.Single(results, x => x);
    }
    [Theory]
    [InlineData("Changed", 1)]
    [InlineData("Correction", 2)]
    public async Task Same_key_with_changed_reason_or_date_is_conflict(string reason, int day)
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1)); await Reverse(s, o.Reference); var before = await s.StateDigest();
        Assert.Equal(Errors.API_IDEMPOTENCY_CONFLICT, (await Assert.ThrowsAsync<FoundationException>(() => Reverse(s, o.Reference, reason: reason, date: new(2026, 1, day)))).Error); Assert.Equal(before, await s.StateDigest());
    }
    [Fact]
    public async Task Failure_after_financial_flush_rolls_back_journal_lines_link_number_audit_outbox_receipt()
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1)); var before = await s.StateDigest();
        var command = new ReverseJournalCommand(new FailAfterWork(db.Commands), new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reverse(s, o.Reference, command: command)); Assert.Equal(before, await s.StateDigest());
        await Reverse(s, o.Reference); // Rolled-back idempotency key and number are reusable.
    }
    private sealed class FailAfterWork(IIdempotentCommands inner) : IIdempotentCommands
    {
        public Task<CommandReceipt> ExecuteAsync(VerifiedContext context, CommandIdentity identity, Func<IApplicationTransaction, CancellationToken, Task<CommandReceipt>> work, CancellationToken cancellationToken = default)
            => inner.ExecuteAsync(context, identity, async (tx, ct) => { await work(tx, ct); throw new InvalidOperationException("Injected after reversal flush"); }, cancellationToken);
    }
    [Fact]
    public async Task Reversal_permission_is_independent_and_cross_scope_targets_are_unavailable()
    {
        var a = await TrialBalanceScenario.Create(db); var b = await TrialBalanceScenario.Create(db, tenant: a.Tenant); var c = await TrialBalanceScenario.Create(db);
        var o = await a.Post(a.Expense, a.Cash, "100", new(2026, 1, 1)); var request = new ReverseJournalRequest(new(2026, 1, 1), "Correction");
        foreach (var action in new[] { JournalPermissions.View, JournalPermissions.Create, JournalPermissions.Edit, JournalPermissions.Post })
            Assert.Equal(Errors.AUTH_SCOPE_DENIED, (await Assert.ThrowsAsync<FoundationException>(async () => await Command.ExecuteAsync(await a.Context(action), o.Reference, 2, request, Identity(o.Reference, action, request)))).Error);
        foreach (var other in new[] { b, c }) Assert.Equal(Errors.RESOURCE_NOT_FOUND, (await Assert.ThrowsAsync<FoundationException>(() => Reverse(other, o.Reference))).Error);
        await Reverse(a, o.Reference); // Reverse context alone does not call a separately authorized Post command.
        Assert.Equal(0L, await db.Scalar<long>("SELECT count(*) FROM finance.journal_reversals WHERE company_id=@c", ("c", b.Company)));
    }
    [Theory]
    [InlineData("0.001")]
    [InlineData("999999999999999999.999")]
    public async Task Stored_decimal_values_are_swapped_exactly(string value)
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, value, new(2026, 1, 1)); var receipt = await Reverse(s, o.Reference);
        var r = await Queries.GetAsync(await s.Context(), receipt.Reference); Assert.Equal(value, r.Lines[0].Credit); Assert.Equal(value, r.Lines[1].Debit); Assert.Equal(value, r.TotalDebit);
    }
    [Fact]
    public async Task Database_constraint_blocks_duplicates_links_are_immutable_and_RLS_fails_closed()
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1)); await Reverse(s, o.Reference);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Execute("INSERT INTO finance.journal_reversals SELECT * FROM finance.journal_reversals WHERE original_journal_id=@id", ("id", o.Reference))); Assert.Equal("23505", duplicate.SqlState);
        foreach (var sql in new[] { "UPDATE finance.journal_reversals SET reason='changed' WHERE original_journal_id=@id", "DELETE FROM finance.journal_reversals WHERE original_journal_id=@id" }) Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Execute(sql, ("id", o.Reference)))).SqlState);
        await using var connection = await db.Source.OpenConnectionAsync(); await using var cmd = Sql.Command(connection, "SELECT count(*) FROM finance.journal_reversals"); Assert.Equal(0L, await cmd.ExecuteScalarAsync());
        Assert.True(await db.Scalar<bool>("SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='finance.journal_reversals'::regclass"));
    }
    [Fact]
    public async Task Http_scope_permission_idempotency_and_precondition_contract()
    {
        await db.Execute("INSERT INTO finance.accounting_books(tenant_id,company_id,currency,minor_units) VALUES(@t,@c,'JOD',3) ON CONFLICT DO NOTHING", ("t", DatabaseFixture.TenantA), ("c", DatabaseFixture.CompanyA));
        await using var factory = new ApiFactory(db); using var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token);
        var path = $"/api/v1/tenants/{DatabaseFixture.TenantA}/companies/{DatabaseFixture.CompanyA}/finance/journals/{Guid.NewGuid()}/reverse";
        async Task<HttpResponseMessage> Send(string url, bool match = true, bool key = true)
        { using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new ReverseJournalRequest(new(2026, 1, 1), "Correction")) }; if (match) req.Headers.TryAddWithoutValidation("If-Match", "\"2\""); if (key) req.Headers.Add("Idempotency-Key", "http-reverse"); return await client.SendAsync(req); }
        foreach (var permission in new[] { JournalPermissions.View, JournalPermissions.Create, JournalPermissions.Edit, JournalPermissions.Post }) { factory.Members.Permissions.Clear(); factory.Members.Permissions.Add(permission); Assert.Equal(HttpStatusCode.Forbidden, (await Send(path)).StatusCode); }
        factory.Members.Permissions.Clear(); factory.Members.Permissions.Add(JournalPermissions.Reverse);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(path, match: false)).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await Send(path, key: false)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await Send(path)).StatusCode);
        foreach (var scope in new[] { DatabaseFixture.TenantA, DatabaseFixture.CompanyA }) Assert.Equal(HttpStatusCode.Forbidden, (await Send(path.Replace(scope.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal))).StatusCode);
    }
    [Fact]
    public async Task Reversed_event_duplicate_delivery_is_inbox_deduplicated_and_never_posts_again()
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1)); await Reverse(s, o.Reference);
        var payload = await db.Scalar<string>("SELECT payload::text FROM integration.outbox WHERE source_id=@id AND type='finance.journal-entry-reversed.v1'", ("id", o.Reference.ToString()));
        var id = await db.Scalar<Guid>("SELECT id FROM integration.outbox WHERE source_id=@id AND type='finance.journal-entry-reversed.v1'", ("id", o.Reference.ToString()));
        var message = new IntegrationEvent(id, JournalEntryReversedV1.EventType, o.Reference.ToString(), payload, DateTimeOffset.UtcNow);
        var memberships = new WorkerMembership(s.Tenant, s.Company);
        var context = await new ContextResolver(memberships).ResolveAsync(new("tests", "worker"), s.Tenant, s.Company, "outbox.dispatch", "reverse-delivery", "worker-request", ExecutionSource.Worker);
        var handler = new JournalReversedTraceHandler(new JournalSessions(), db.Audit);
        for (var n = 0; n < 2; n++) await db.Inbox.ProcessAsync(context, handler.Consumer, message, (tx, ct) => handler.HandleAsync(tx, message, ct));
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND action='JournalReversedEventDelivered'", ("id", o.Reference.ToString())));
        Assert.Equal(2L, await db.Scalar<long>("SELECT count(*) FROM finance.journal_entries WHERE tenant_id=@t AND company_id=@c", ("t", s.Tenant), ("c", s.Company)));
    }
    private sealed class WorkerMembership(Guid tenant, Guid company) : IMembershipStore
    {
        public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken token) => ValueTask.FromResult<PrincipalMembership?>(new(true, [new(tenant, company, new HashSet<string> { "outbox.dispatch" })]));
    }
    [Fact]
    public async Task Database_rejects_non_inverse_relationship_and_foreign_scope_insert()
    {
        var s = await TrialBalanceScenario.Create(db); var a = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1)); var b = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        const string insert = "INSERT INTO finance.journal_reversals(tenant_id,company_id,original_journal_id,reversal_journal_id,posting_date,reason,actor_issuer,actor_subject,request_id,correlation_id,timestamp) VALUES(@t,@c,@a,@b,'2026-01-01','Bad link','tests','reader','request','correlation',now())";
        var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Execute(insert, ("t", s.Tenant), ("c", s.Company), ("a", a.Reference), ("b", b.Reference)));
        Assert.Equal("23514", failure.SqlState); Assert.Contains("reversal_amount_mismatch", failure.MessageText);
        var other = await TrialBalanceScenario.Create(db);
        var denied = await Assert.ThrowsAsync<PostgresException>(async () => await db.Uow.ExecuteAsync(await other.Context(), async (tx, ct) =>
            await Sql.ExecuteAsync(PostgresTransaction.Require(tx).Connection, insert, ct, ("t", s.Tenant), ("c", s.Company), ("a", a.Reference), ("b", b.Reference))));
        Assert.Equal("42501", denied.SqlState);
    }
    [Fact]
    public async Task Previous_finance_migration_upgrades_and_clean_schema_matches_model()
    {
        var name = "reversal_upgrade_" + Guid.NewGuid().ToString("N"); await db.Execute($"CREATE DATABASE {name} OWNER erp_migrator");
        try
        {
            var migration = new NpgsqlConnectionStringBuilder(db.Migration) { Database = name }.ToString(); await MigrationRunner.ApplyAsync(migration);
            await using var c = new NpgsqlConnection(migration); await c.OpenAsync(); await using var schema = new FinanceDbContext(FinanceDbContext.Options(c));
            await schema.GetService<IMigrator>().MigrateAsync("20261018141639_ManualJournalV1"); Assert.Single(await schema.Database.GetAppliedMigrationsAsync());
            await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(db.Admin) { Database = name }.ToString()))
            { await admin.OpenAsync(); await Sql.ExecuteAsync(admin, "INSERT INTO finance.accounting_books(tenant_id,company_id,currency,minor_units) VALUES(@t,@c,'JOD',3)", default, ("t", Guid.Empty), ("c", Guid.Empty)); }
            await schema.GetService<IMigrator>().MigrateAsync(); Assert.Equal(7, (await schema.Database.GetAppliedMigrationsAsync()).Count()); Assert.False(schema.Database.HasPendingModelChanges());
            await using var check = Sql.Command(c, "SELECT count(*) FROM finance.journal_reversals"); Assert.Equal(0L, await check.ExecuteScalarAsync());
        }
        finally { await db.Execute($"DROP DATABASE {name} WITH (FORCE)"); }
    }
}
