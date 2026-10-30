using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Application;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Finance.Infrastructure;
using Erp.Kernel;
using Erp.Platform.Infrastructure;
using Erp.Platform.Infrastructure.Persistence;
namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class JournalCorrectionTests(DatabaseFixture db)
{
    private CorrectJournalCommand Command => new(db.Commands, new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
    private static CorrectJournalRequest Request(TrialBalanceScenario s, string amount = "120") => new(new(2026, 1, 1), "Correct amount", [new(null, s.Expense, amount, "0", ""), new(null, s.Cash, "0", amount, "")]);
    private async Task<CommandReceipt> Correct(TrialBalanceScenario s, Guid id, string key = "correct", CorrectJournalRequest? request = null, CorrectJournalCommand? command = null)
    {
        request ??= Request(s);
        return await (command ?? Command).ExecuteAsync(await s.Context(JournalPermissions.Correct), id, 2, request,
            new("Finance.CorrectManualJournal", id.ToString(), key, RequestFingerprint.Create("POST", id.ToString(), "2", JsonSerializer.Serialize(request))));
    }
    [Fact]
    public async Task Closed_original_corrected_atomically_with_three_balanced_journals_and_trace()
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, "100", new(2025, 12, 31));
        var period = await db.Scalar<Guid>("SELECT fiscal_period_id FROM finance.journal_entries WHERE id=@id", ("id", o.Reference));
        await db.ClosePeriod(period, s.Tenant, s.Company);
        var before = await db.Scalar<string>("SELECT to_jsonb(j)::text FROM finance.journal_entries j WHERE id=@id", ("id", o.Reference));
        var receipt = await Correct(s, o.Reference);
        var q = new JournalQueries(db.Uow, new AuditReader()); var original = await q.GetAsync(await s.Context(), o.Reference);
        var replacement = await q.GetAsync(await s.Context(), receipt.Reference); var relation = original.Correction!;
        var reversal = await q.GetAsync(await s.Context(), relation.ReversalJournalId);
        Assert.Equal(original.Correction, reversal.Correction); Assert.Equal(original.Correction, replacement.Correction);
        Assert.Equal("120", replacement.TotalDebit); Assert.Equal("100", reversal.TotalDebit);
        Assert.Equal(3, new[] { original.JournalNumber, reversal.JournalNumber, replacement.JournalNumber }.Distinct().Count());
        Assert.All(new[] { original, reversal, replacement }, x => { Assert.Equal("Posted", x.Status); Assert.Equal("0", x.Difference); });
        Assert.Equal(before, await db.Scalar<string>("SELECT to_jsonb(j)::text FROM finance.journal_entries j WHERE id=@id", ("id", o.Reference)));
        Assert.Equal("Closed", await db.Scalar<string>("SELECT status FROM finance.fiscal_periods WHERE id=@id", ("id", period)));
        var tb = await s.Report(new(2025, 12, 1), new(2026, 1, 31)); Assert.True(tb.Reconciliation.Passed);
        Assert.Equal(new("0", "0", "220", "100", "120", "0"), s.Account(tb, s.Expense));
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE type='finance.journal-corrected.v1' AND source_id=@id", ("id", o.Reference.ToString())));
    }
    [Fact]
    public async Task Concurrent_retries_and_response_loss_return_one_result_changed_payload_conflicts()
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Correct(s, o.Reference)));
        Assert.Single(results.Select(x => x.Reference).Distinct());
        Assert.Equal(results[0], await Correct(s, o.Reference));
        Assert.Equal(Errors.API_IDEMPOTENCY_CONFLICT, (await Assert.ThrowsAsync<FoundationException>(() => Correct(s, o.Reference, request: Request(s, "121")))).Error);
        Assert.Equal(CorrectionErrors.AlreadyCorrected, (await Assert.ThrowsAsync<FoundationException>(() => Correct(s, o.Reference, "different"))).Error);
    }
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Failure_after_each_persistence_stage_rolls_back_every_effect(int stage)
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1)); var before = await s.StateDigest();
        var command = new CorrectJournalCommand(db.Commands, new FailingSessions(stage), db.Audit, db.Outbox, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Correct(s, o.Reference, command: command));
        Assert.Equal(before, await s.StateDigest());
        Assert.Equal(0L, await db.Scalar<long>("SELECT count(*) FROM finance.journal_corrections WHERE original_journal_id=@id", ("id", o.Reference)));
    }
    [Fact]
    public async Task Permission_and_foreign_scope_are_denied()
    {
        var s = await TrialBalanceScenario.Create(db); var o = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        var req = Request(s); var id = new CommandIdentity("Finance.CorrectManualJournal", o.Reference.ToString(), "denied", RequestFingerprint.Create("POST", o.Reference.ToString(), "2", JsonSerializer.Serialize(req)));
        foreach (var permission in new[] { JournalPermissions.Post, JournalPermissions.Reverse, JournalPermissions.View })
            Assert.Equal(Errors.AUTH_SCOPE_DENIED, (await Assert.ThrowsAsync<FoundationException>(async () => await Command.ExecuteAsync(await s.Context(permission), o.Reference, 2, req, id))).Error);
        var foreign = await TrialBalanceScenario.Create(db);
        Assert.Equal(Errors.RESOURCE_NOT_FOUND, (await Assert.ThrowsAsync<FoundationException>(async () => await Command.ExecuteAsync(await foreign.Context(JournalPermissions.Correct), o.Reference, 2, req, id))).Error);
    }
    [Fact]
    public async Task Draft_reversed_closed_target_and_unbalanced_replacement_are_rejected()
    {
        var s = await TrialBalanceScenario.Create(db); var draft = await s.Draft(s.Expense, s.Cash, "100", new(2026, 1, 1));
        Assert.Equal(CorrectionErrors.NotAllowed, (await Assert.ThrowsAsync<FoundationException>(() => Correct(s, draft.Reference))).Error);
        var o = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        Assert.Equal(FinanceErrors.Unbalanced, (await Assert.ThrowsAsync<FoundationException>(() => Correct(s, o.Reference, request: Request(s, "121") with { Lines = [new(null, s.Expense, "121", "0", ""), new(null, s.Cash, "0", "120", "")] }))).Error);
        await db.ClosePeriod(s.Period2026, s.Tenant, s.Company);
        Assert.Equal(CorrectionErrors.PeriodClosed, (await Assert.ThrowsAsync<FoundationException>(() => Correct(s, o.Reference))).Error);
    }
    [Fact]
    public async Task Different_keys_compete_for_one_correction()
    {
        var s = await TrialBalanceScenario.Create(db);
        var original = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        async Task<bool> Attempt(int n)
        {
            try { await Correct(s, original.Reference, "different-" + n); return true; }
            catch (FoundationException e) { Assert.Equal(CorrectionErrors.AlreadyCorrected, e.Error); return false; }
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(Attempt));
        Assert.Single(results, x => x);
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM finance.journal_corrections WHERE original_journal_id=@id", ("id", original.Reference)));
    }

    [Fact]
    public async Task Manual_reversal_blocks_correction_and_changed_date_conflicts()
    {
        var s = await TrialBalanceScenario.Create(db);
        var original = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        var reverse = new ReverseJournalCommand(db.Commands, new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
        var request = new ReverseJournalRequest(new(2026, 1, 2), "Independent reversal");
        await reverse.ExecuteAsync(await s.Context(JournalPermissions.Reverse), original.Reference, 2, request,
            new("Finance.ReverseManualJournal", original.Reference.ToString(), "independent-reversal",
                RequestFingerprint.Create("POST", original.Reference.ToString(), "2", JsonSerializer.Serialize(request))));
        Assert.Equal(CorrectionErrors.NotAllowed, (await Assert.ThrowsAsync<FoundationException>(() => Correct(s, original.Reference))).Error);
        var second = await s.Post(s.Expense, s.Cash, "40", new(2026, 1, 3));
        await Correct(s, second.Reference, "date-key");
        Assert.Equal(Errors.API_IDEMPOTENCY_CONFLICT,
            (await Assert.ThrowsAsync<FoundationException>(() => Correct(s, second.Reference, "date-key", Request(s) with { PostingDate = new(2026, 1, 4) }))).Error);
    }

    [Fact]
    public async Task Corrected_event_duplicate_delivery_is_inbox_deduplicated()
    {
        var s = await TrialBalanceScenario.Create(db);
        var original = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        await Correct(s, original.Reference);
        var payload = await db.Scalar<string>("SELECT payload::text FROM integration.outbox WHERE source_id=@id AND type='finance.journal-corrected.v1'", ("id", original.Reference.ToString()));
        var id = await db.Scalar<Guid>("SELECT id FROM integration.outbox WHERE source_id=@id AND type='finance.journal-corrected.v1'", ("id", original.Reference.ToString()));
        var message = new IntegrationEvent(id, JournalCorrectedV1.EventType, original.Reference.ToString(), payload, DateTimeOffset.UtcNow);
        var context = await new ContextResolver(new CorrectionWorkerMembership(s.Tenant, s.Company)).ResolveAsync(
            new("tests", "worker"), s.Tenant, s.Company, "outbox.dispatch", "correction-delivery", "worker-request", ExecutionSource.Worker);
        var handler = new JournalCorrectedTraceHandler(new JournalSessions(), db.Audit);
        for (var n = 0; n < 2; n++) await db.Inbox.ProcessAsync(context, handler.Consumer, message, (tx, ct) => handler.HandleAsync(tx, message, ct));
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND action='JournalCorrectedEventDelivered'", ("id", original.Reference.ToString())));
        Assert.Equal(3L, await db.Scalar<long>("SELECT count(*) FROM finance.journal_entries WHERE tenant_id=@t AND company_id=@c", ("t", s.Tenant), ("c", s.Company)));
    }

    private sealed class CorrectionWorkerMembership(Guid tenant, Guid company) : IMembershipStore
    {
        public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken token) =>
            ValueTask.FromResult<PrincipalMembership?>(new(true, [new(tenant, company, new HashSet<string> { "outbox.dispatch" })]));
    }

    private sealed class FailingSessions(int stage) : IJournalSessions
    { public IJournalSession Open(IApplicationTransaction tx) => new FailingSession(new JournalSessions().Open(tx), stage); }
    private sealed class FailingSession(IJournalSession inner, int stage) : IJournalSession
    {
        private int calls;
        public Task<JournalEntry> LoadAsync(Guid id, bool locked, CancellationToken ct) => inner.LoadAsync(id, locked, ct);
        public Task<BookPolicy> BookAsync(bool locked, CancellationToken ct) => inner.BookAsync(locked, ct);
        public Task<IReadOnlyList<PeriodSnapshot>> PeriodsAsync(DateOnly? date, bool locked, CancellationToken ct) => inner.PeriodsAsync(date, locked, ct);
        public Task<IReadOnlyList<AccountSnapshot>> AccountsAsync(IEnumerable<Guid> ids, bool locked, CancellationToken ct) => inner.AccountsAsync(ids, locked, ct);
        public Task<JournalReversal?> ReversalAsync(Guid id, CancellationToken ct) => inner.ReversalAsync(id, ct);
        public Task<JournalCorrection?> CorrectionAsync(Guid id, CancellationToken ct) => inner.CorrectionAsync(id, ct);
        public void AddCorrection(JournalCorrection c) => inner.AddCorrection(c);
        public void AddReversal(JournalReversal r) => inner.AddReversal(r);
        public void Add(JournalEntry j) => inner.Add(j);
        public Task<long> AllocateNumberAsync(int year, CancellationToken ct) => inner.AllocateNumberAsync(year, ct);
        public async Task FlushAsync(CancellationToken ct) { await inner.FlushAsync(ct); if (++calls == stage) throw new InvalidOperationException("Injected failure"); }
    }
}
