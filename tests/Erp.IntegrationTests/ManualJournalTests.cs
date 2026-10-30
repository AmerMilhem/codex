using Erp.Application.Abstractions;
using Erp.Finance.Application;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Finance.Infrastructure;
using Erp.Kernel;
using Erp.Platform.Infrastructure;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class ManualJournalTests(DatabaseFixture db)
{
    [Fact]
    public async Task Golden_post_is_balanced_immutable_audited_and_reconciles()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft();
        var validation = await s.Commands.ValidateAsync(await s.Context(JournalPermissions.Edit), draft.Reference, 1);
        Assert.True(validation.Valid); Assert.Empty(validation.Issues);
        var receipt = await s.Post(draft.Reference); Assert.Equal(2, receipt.AppliedRevision);
        var journal = await s.Queries.GetAsync(await s.Context(JournalPermissions.View), receipt.Reference);
        Assert.Equal("Posted", journal.Status); Assert.Equal("100", journal.TotalDebit); Assert.Equal("100", journal.TotalCredit);
        Assert.NotNull(journal.JournalNumber); Assert.Equal("runner", journal.PostedBySubject); Assert.NotNull(journal.PostedAt);
        var audit = await s.Queries.AuditAsync(await s.Context(JournalPermissions.View), receipt.Reference);
        Assert.Equal(new[] { "JournalCreated", "JournalValidated", "JournalPosted" }, audit.Select(x => x.Action));
        Assert.All(audit, x => { Assert.Equal(s.Tenant, x.TenantId); Assert.Equal(s.Company, x.CompanyId); Assert.NotEmpty(x.RequestId); Assert.NotEmpty(x.CorrelationId); });
        await s.AssertOnePosting(receipt.Reference);
        var reconciliation = await s.Queries.ReconcileAsync(await s.Context(JournalPermissions.View));
        Assert.True(reconciliation.Passed); Assert.Empty(reconciliation.Failures); Assert.Equal(reconciliation.TotalDebit, reconciliation.TotalCredit);
    }
    [Fact]
    public async Task Unbalanced_validate_reports_errors_without_posting_and_post_rechecks()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft(credit: "99");
        var result = await s.Commands.ValidateAsync(await s.Context(JournalPermissions.Edit), draft.Reference, 1);
        Assert.False(result.Valid); Assert.Contains(result.Issues, x => x.ErrorId == "FIN_JOURNAL_UNBALANCED");
        await s.RejectPost(draft.Reference, FinanceErrors.Unbalanced); await s.AssertNoPosting(draft.Reference);
    }
    [Fact]
    public async Task Period_closed_after_successful_validate_is_rejected_at_post()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft();
        Assert.True((await s.Commands.ValidateAsync(await s.Context(JournalPermissions.Edit), draft.Reference, 1)).Valid);
        await db.ClosePeriod(s.Period, s.Tenant, s.Company);
        await s.RejectPost(draft.Reference, FinanceErrors.PeriodClosed); await s.AssertNoPosting(draft.Reference);
    }
    [Theory]
    [InlineData("Inactive", true, "FIN_ACCOUNT_INACTIVE")]
    [InlineData("Active", false, "FIN_ACCOUNT_NOT_POSTABLE")]
    public async Task Invalid_account_is_rechecked_inside_post(string status, bool allowed, string error)
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft();
        await db.Execute("UPDATE finance.accounts SET status=@status,posting_allowed=@allowed WHERE id=@id", ("status", status), ("allowed", allowed), ("id", s.Expense));
        var failure = await Assert.ThrowsAsync<FoundationException>(() => s.Post(draft.Reference)); Assert.Equal(error, failure.Error.Id);
        await s.AssertNoPosting(draft.Reference);
    }
    [Theory]
    [InlineData("0", "0")]
    [InlineData("10", "10")]
    [InlineData("0.0001", "0")]
    public async Task Invalid_line_side_or_currency_precision_cannot_post(string debit, string credit)
    {
        var s = await FinanceScenario.Create(db);
        var request = s.Request() with { Lines = [new(null, s.Expense, debit, credit, ""), new(null, s.Cash, "0", "100", "")] };
        var draft = await s.Commands.CreateAsync(await s.Context(JournalPermissions.Create), request);
        await s.RejectPost(draft.Reference, FinanceErrors.InvalidLine); await s.AssertNoPosting(draft.Reference);
    }
    [Fact]
    public async Task Missing_date_period_or_second_line_is_reported()
    {
        var s = await FinanceScenario.Create(db);
        var draft = await s.Commands.CreateAsync(await s.Context(JournalPermissions.Create), s.Request() with { PostingDate = null, Lines = [] });
        var result = await s.Commands.ValidateAsync(await s.Context(JournalPermissions.Edit), draft.Reference, 1);
        Assert.Contains(result.Issues, x => x.ErrorId == "FIN_POSTING_DATE_REQUIRED"); Assert.Contains(result.Issues, x => x.ErrorId == "FIN_JOURNAL_INVALID_LINE");
        await s.Commands.UpdateAsync(await s.Context(JournalPermissions.Edit), draft.Reference, 1, s.Request() with { PostingDate = new DateOnly(1999, 1, 1) });
        await s.RejectPost(draft.Reference, FinanceErrors.PeriodMissing, 2);
    }
    [Fact]
    public async Task Duplicate_post_with_new_key_rejected_but_same_key_replays_original_receipt()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft();
        var first = await s.Post(draft.Reference, key: "original"); Assert.Equal(first, await s.Post(draft.Reference, key: "original"));
        await s.RejectPost(draft.Reference, FinanceErrors.AlreadyPosted, key: "different"); await s.AssertOnePosting(draft.Reference);
    }
    [Fact]
    public async Task Concurrent_identical_posts_commit_once_and_all_replay()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft();
        var receipts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => s.Post(draft.Reference, key: "concurrent")));
        Assert.Single(receipts.Distinct()); await s.AssertOnePosting(draft.Reference);
    }
    [Fact]
    public async Task Concurrent_distinct_posts_create_one_result()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft();
        async Task<bool> Attempt(int index) { try { await s.Post(draft.Reference, key: "distinct-" + index); return true; } catch (FoundationException e) { Assert.Equal(FinanceErrors.AlreadyPosted, e.Error); return false; } }
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(Attempt)); Assert.Single(results, x => x); await s.AssertOnePosting(draft.Reference);
    }
    [Fact]
    public async Task Concurrent_number_allocation_has_unique_company_fiscal_year_numbers()
    {
        var s = await FinanceScenario.Create(db); var drafts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => s.CreateDraft()));
        await Task.WhenAll(drafts.Select(x => s.Post(x.Reference)));
        Assert.Equal(6, await db.Scalar<long>("SELECT count(DISTINCT number_sequence) FROM finance.journal_entries WHERE fiscal_period_id=@period", ("period", s.Period)));
    }
    [Fact]
    public async Task Response_timeout_after_commit_replays_without_second_effect()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft();
        async Task LostResponse() { await s.Post(draft.Reference, key: "timeout-after-commit"); throw new TimeoutException("Committed response lost in transport."); }
        await Assert.ThrowsAsync<TimeoutException>(LostResponse);
        var receipt = await s.Post(draft.Reference, key: "timeout-after-commit"); Assert.Equal(draft.Reference, receipt.Reference); await s.AssertOnePosting(draft.Reference);
    }
    [Fact]
    public async Task Same_key_with_different_precondition_conflicts_before_current_state_checks()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft(); await s.Post(draft.Reference, key: "fingerprint");
        var error = await Assert.ThrowsAsync<FoundationException>(() => s.Post(draft.Reference, 2, "fingerprint")); Assert.Equal(Errors.API_IDEMPOTENCY_CONFLICT, error.Error);
    }
    [Fact]
    public async Task Rollback_after_finance_audit_outbox_flush_leaves_no_partial_posting()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft();
        var failing = new JournalCommands(db.Uow, new FailAfterFlush(db.Commands), new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Post(draft.Reference, commands: failing));
        await s.AssertNoPosting(draft.Reference); Assert.Equal(0, await db.Scalar<long>("SELECT count(*) FROM finance.journal_numbers WHERE tenant_id=@tenant AND company_id=@company AND fiscal_year=@year", ("tenant", s.Tenant), ("company", s.Company), ("year", s.Date.Year)));
        Assert.Equal(0, await db.Scalar<long>("SELECT count(*) FROM integration.idempotency WHERE target=@id", ("id", draft.Reference.ToString())));
        await s.Post(draft.Reference); await s.AssertOnePosting(draft.Reference);
    }
    [Fact]
    public async Task Draft_revision_conflict_and_line_add_edit_remove_work_without_lost_update()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft(); var context = await s.Context(JournalPermissions.Edit);
        var current = await s.Queries.GetAsync(await s.Context(JournalPermissions.View), draft.Reference);
        var request = s.Request() with { Reference = "A", Lines = [new(current.Lines[0].Id, s.Expense, "100", "0", "edited"), new(null, s.Cash, "0", "100", "new credit")] };
        await s.Commands.UpdateAsync(context, draft.Reference, 1, request);
        var conflict = await Assert.ThrowsAsync<FoundationException>(() => s.Commands.UpdateAsync(context, draft.Reference, 1, s.Request() with { Reference = "B" })); Assert.Equal(Errors.DOC_CONCURRENCY_CONFLICT, conflict.Error);
        var saved = await s.Queries.GetAsync(await s.Context(JournalPermissions.View), draft.Reference);
        Assert.Equal("A", saved.Reference); Assert.Equal(2, saved.Revision); Assert.Equal("edited", saved.Lines[0].Description); Assert.DoesNotContain(saved.Lines, x => x.Id == current.Lines[1].Id);
    }
    [Fact]
    public async Task Simultaneous_draft_edit_and_post_cannot_post_a_different_revision()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft();
        async Task<string> Edit() { try { await s.Commands.UpdateAsync(await s.Context(JournalPermissions.Edit), draft.Reference, 1, s.Request() with { Reference = "changed" }); return "edited"; } catch (FoundationException e) { return e.Error.Id; } }
        async Task<string> Post() { try { await s.Post(draft.Reference); return "posted"; } catch (FoundationException e) { return e.Error.Id; } }
        var outcomes = await Task.WhenAll(Edit(), Post());
        Assert.True(outcomes.Contains("edited") ? outcomes.Contains("DOC_CONCURRENCY_CONFLICT") : outcomes.Contains("posted") && outcomes.Contains("FIN_JOURNAL_ALREADY_POSTED"));
    }
    [Fact]
    public async Task Posted_header_and_lines_are_immutable_in_application_and_database()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft(); await s.Post(draft.Reference);
        Assert.Equal(FinanceErrors.AlreadyPosted, (await Assert.ThrowsAsync<FoundationException>(async () => await s.Commands.UpdateAsync(await s.Context(JournalPermissions.Edit), draft.Reference, 2, s.Request()))).Error);
        var context = await s.Context(JournalPermissions.Edit);
        foreach (var sql in new[] { "UPDATE finance.journal_entries SET posting_date=posting_date+1 WHERE id=@id", "UPDATE finance.journal_lines SET debit=debit+1 WHERE journal_id=@id", "DELETE FROM finance.journal_lines WHERE journal_id=@id", "DELETE FROM finance.journal_entries WHERE id=@id", "INSERT INTO finance.journal_lines(id,tenant_id,company_id,journal_id,position,account_id,debit,credit,description) SELECT gen_random_uuid(),tenant_id,company_id,id,3,@account,1,0,'' FROM finance.journal_entries WHERE id=@id" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Uow.ExecuteAsync(context, (tx, ct) => Sql.ExecuteAsync(PostgresTransaction.Require(tx).Connection, sql, ct, ("id", draft.Reference), ("account", s.Expense))));
            Assert.Contains(error.SqlState, new[] { "23514", "42501" });
        }
        await s.AssertOnePosting(draft.Reference);
    }
    [Fact]
    public async Task Decimal_precision_and_three_minor_units_survive_post_db_and_wire()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft("999999999999999999.999", "999999999999999999.999"); await s.Post(draft.Reference);
        var journal = await s.Queries.GetAsync(await s.Context(JournalPermissions.View), draft.Reference);
        Assert.Equal("999999999999999999.999", journal.TotalDebit); Assert.Equal(journal.TotalDebit, journal.TotalCredit);
        Assert.Equal(999999999999999999.999m, await db.Scalar<decimal>("SELECT debit FROM finance.journal_lines WHERE journal_id=@id AND debit>0", ("id", draft.Reference)));
    }
    [Fact]
    public async Task Cross_tenant_and_company_reads_commands_and_account_injection_fail_closed()
    {
        var a = await FinanceScenario.Create(db); var foreignTenant = await FinanceScenario.Create(db, DatabaseFixture.TenantB, DatabaseFixture.CompanyB); var foreignCompany = await FinanceScenario.Create(db, DatabaseFixture.TenantA, DatabaseFixture.CompanyA2);
        foreach (var other in new[] { foreignTenant, foreignCompany })
        {
            var journal = await other.CreateDraft();
            Assert.Equal(Errors.RESOURCE_NOT_FOUND, (await Assert.ThrowsAsync<FoundationException>(async () => await a.Queries.GetAsync(await a.Context(JournalPermissions.View), journal.Reference))).Error);
            Assert.Equal(Errors.RESOURCE_NOT_FOUND, (await Assert.ThrowsAsync<FoundationException>(() => a.Post(journal.Reference))).Error);
            var request = a.Request() with { Lines = [new(null, other.Cash, "100", "0", ""), new(null, a.Cash, "0", "100", "")] };
            Assert.Equal(FinanceErrors.AccountMissing, (await Assert.ThrowsAsync<FoundationException>(async () => await a.Commands.CreateAsync(await a.Context(JournalPermissions.Create), request))).Error);
        }
        await using var c = await db.Source.OpenConnectionAsync();
        foreach (var table in new[] { "accounting_books", "accounts", "fiscal_periods", "journal_entries", "journal_lines", "journal_numbers" })
        { await using var cmd = Sql.Command(c, "SELECT count(*) FROM finance." + table); Assert.Equal(0L, await cmd.ExecuteScalarAsync()); }
        var context = await a.Context(JournalPermissions.Edit);
        var denial = await Assert.ThrowsAsync<PostgresException>(() => db.Uow.ExecuteAsync(context, (tx, ct) => Sql.ExecuteAsync(PostgresTransaction.Require(tx).Connection,
            "INSERT INTO finance.journal_numbers VALUES(@tenant,@company,2099,1)", ct, ("tenant", foreignTenant.Tenant), ("company", foreignTenant.Company)))); Assert.Equal("42501", denial.SqlState);
    }
    [Fact]
    public async Task Permission_to_create_or_edit_is_not_permission_to_post_or_view()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft();
        foreach (var permission in new[] { JournalPermissions.Create, JournalPermissions.Edit, JournalPermissions.View })
            Assert.Equal(Errors.AUTH_SCOPE_DENIED, (await Assert.ThrowsAsync<FoundationException>(async () => await s.Commands.PostAsync(await s.Context(permission), draft.Reference, 1, s.Identity(draft.Reference, 1, "denied")))).Error);
        Assert.Equal(Errors.AUTH_SCOPE_DENIED, (await Assert.ThrowsAsync<FoundationException>(async () => await s.Queries.GetAsync(await s.Context(JournalPermissions.Post), draft.Reference))).Error);
        await s.AssertNoPosting(draft.Reference);
    }
    [Fact]
    public async Task Database_rejects_unbalanced_direct_post_even_with_forged_metadata()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft(credit: "99"); var context = await s.Context(JournalPermissions.Post);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Uow.ExecuteAsync(context, (tx, ct) => Sql.ExecuteAsync(PostgresTransaction.Require(tx).Connection,
            "UPDATE finance.journal_entries SET status='Posted',revision=revision+1,fiscal_period_id=@period,fiscal_year=@year,number_sequence=1,journal_number=@number,posted_by_issuer='tests',posted_by_subject='runner',posted_at=clock_timestamp() WHERE id=@id", ct,
            ("period", s.Period), ("year", s.Date.Year), ("number", $"MJ-{s.Date.Year}-000001"), ("id", draft.Reference)))); Assert.Equal("23514", error.SqlState); await s.AssertNoPosting(draft.Reference);
    }
    [Fact]
    public async Task Posting_reference_locks_serialize_period_close_and_account_disable()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = new JournalCommands(db.Uow, new PauseAfterFlush(db.Commands, entered, release), new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
        var post = s.Post(draft.Reference, commands: commands); await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var close = db.ClosePeriod(s.Period, s.Tenant, s.Company);
        var disable = db.Execute("UPDATE finance.accounts SET status='Inactive' WHERE id=@id", ("id", s.Cash));
        await Task.Delay(100); Assert.False(close.IsCompleted); Assert.False(disable.IsCompleted);
        release.SetResult(); await post; await Task.WhenAll(close, disable); await s.AssertOnePosting(draft.Reference);
    }
    [Fact]
    public async Task Posted_event_delivery_and_duplicate_inbox_preserve_one_financial_effect_and_trace()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft(); await s.Post(draft.Reference);
        var payload = await db.Scalar<string>("SELECT payload::text FROM integration.outbox WHERE source_id=@id", ("id", draft.Reference.ToString()));
        var id = await db.Scalar<Guid>("SELECT id FROM integration.outbox WHERE source_id=@id", ("id", draft.Reference.ToString()));
        using var document = System.Text.Json.JsonDocument.Parse(payload);
        Assert.Equal(new[] { "currency", "journalId", "journalNumber", "postingDate" }, document.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        var message = new IntegrationEvent(id, JournalEntryPostedV1.EventType, draft.Reference.ToString(), payload, DateTimeOffset.UtcNow);
        var handler = new JournalPostedTraceHandler(new JournalSessions(), db.Audit); var context = await db.Context(source: ExecutionSource.Worker, action: "outbox.dispatch");
        for (var i = 0; i < 2; i++) await db.Inbox.ProcessAsync(context, handler.Consumer, message, (tx, ct) => handler.HandleAsync(tx, message, ct));
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND action='JournalPostedEventDelivered'", ("id", draft.Reference.ToString()))); await s.AssertOnePosting(draft.Reference);
    }
    [Fact]
    public async Task Finance_migration_clean_install_and_upgrade_from_foundation_preserve_previous_data()
    {
        foreach (var upgrade in new[] { false, true })
        {
            var name = "journal_" + Guid.NewGuid().ToString("N"); await db.Execute($"CREATE DATABASE {name} OWNER erp_migrator");
            var migration = new NpgsqlConnectionStringBuilder(db.Migration) { Database = name }.ToString();
            await MigrationRunner.ApplyAsync(migration);
            if (upgrade)
            {
                await using var previous = new NpgsqlConnection(migration); await previous.OpenAsync();
                await Sql.ExecuteAsync(previous, "CREATE TABLE public.upgrade_marker(value text NOT NULL); INSERT INTO public.upgrade_marker VALUES('preserved')");
            }
            await FinanceMigrations.ApplyAsync(migration); await FinanceMigrations.ApplyAsync(migration);
            await using var c = new NpgsqlConnection(migration); await c.OpenAsync(); await using var schema = new FinanceDbContext(FinanceDbContext.Options(c));
            Assert.False(schema.Database.HasPendingModelChanges()); Assert.Equal(7, (await schema.Database.GetAppliedMigrationsAsync()).Count());
            if (upgrade) { await using var marker = Sql.Command(c, "SELECT value FROM public.upgrade_marker"); Assert.Equal("preserved", await marker.ExecuteScalarAsync()); }
            var runtime = new NpgsqlConnectionStringBuilder(db.Runtime) { Database = name }.ToString(); await using var source = new NpgsqlDataSourceBuilder(runtime).Build();
            Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, (await new FinanceDatabaseReadiness(source).CheckHealthAsync(new())).Status);
        }
    }
    [Fact]
    public async Task Concurrent_draft_edits_have_one_winner_and_one_explicit_conflict()
    {
        var s = await FinanceScenario.Create(db); var draft = await s.CreateDraft();
        async Task<bool> Edit(string reference)
        {
            try { await s.Commands.UpdateAsync(await s.Context(JournalPermissions.Edit), draft.Reference, 1, s.Request() with { Reference = reference }); return true; }
            catch (FoundationException e) { Assert.Equal(Errors.DOC_CONCURRENCY_CONFLICT, e.Error); return false; }
        }
        var outcomes = await Task.WhenAll(Edit("A"), Edit("B")); Assert.Single(outcomes, x => x);
        Assert.Equal(2, (await s.Queries.GetAsync(await s.Context(JournalPermissions.View), draft.Reference)).Revision);
    }
    [Fact]
    public async Task Database_composite_foreign_keys_and_unique_account_codes_enforce_business_scope()
    {
        var s = await FinanceScenario.Create(db); var other = await FinanceScenario.Create(db, DatabaseFixture.TenantA, DatabaseFixture.CompanyA2); var draft = await s.CreateDraft();
        var context = await s.Context(JournalPermissions.Edit);
        var fk = await Assert.ThrowsAsync<PostgresException>(() => db.Uow.ExecuteAsync(context, (tx, ct) => Sql.ExecuteAsync(PostgresTransaction.Require(tx).Connection,
            "INSERT INTO finance.journal_lines(id,tenant_id,company_id,journal_id,position,account_id,debit,credit,description) VALUES(@id,@tenant,@company,@journal,3,@account,1,0,'')", ct,
            ("id", Guid.NewGuid()), ("tenant", s.Tenant), ("company", s.Company), ("journal", draft.Reference), ("account", other.Cash)))); Assert.Equal("23503", fk.SqlState);
        var unique = await Assert.ThrowsAsync<PostgresException>(() => db.Execute(
            "INSERT INTO finance.accounts(id,tenant_id,company_id,code,name,status,posting_allowed) SELECT gen_random_uuid(),tenant_id,company_id,code,'Duplicate','Active',true FROM finance.accounts WHERE id=@id", ("id", s.Cash))); Assert.Equal("23505", unique.SqlState);
    }
    [Fact]
    public async Task Reference_lock_functions_cannot_escape_RLS_or_allow_runtime_configuration_writes()
    {
        var s = await FinanceScenario.Create(db); var other = await FinanceScenario.Create(db, DatabaseFixture.TenantB, DatabaseFixture.CompanyB);
        var context = await s.Context(JournalPermissions.Post);
        await db.Uow.ExecuteAsync(context, async (tx, ct) =>
        {
            var c = PostgresTransaction.Require(tx).Connection;
            await using var cmd = Sql.Command(c, "SELECT count(*) FROM finance.lock_accounts(@ids)", ("ids", new[] { other.Cash })); Assert.Equal(0L, await cmd.ExecuteScalarAsync(ct));
            await using var books = Sql.Command(c, "SELECT company_id FROM finance.lock_book()"); Assert.Equal(s.Company, await books.ExecuteScalarAsync(ct)); return 0;
        });
        await using var unscoped = await db.Source.OpenConnectionAsync(); await using var missing = Sql.Command(unscoped, "SELECT count(*) FROM finance.lock_book()"); Assert.Equal(0L, await missing.ExecuteScalarAsync());
        var denied = await Assert.ThrowsAsync<PostgresException>(() => db.Uow.ExecuteAsync(context, (tx, ct) => Sql.ExecuteAsync(PostgresTransaction.Require(tx).Connection, "UPDATE finance.accounts SET status='Inactive' WHERE id=@id", ct, ("id", s.Cash)))); Assert.Equal("42501", denied.SqlState);
    }
    private sealed class FailAfterFlush(IIdempotentCommands inner) : IIdempotentCommands
    {
        public Task<CommandReceipt> ExecuteAsync(VerifiedContext c, CommandIdentity i, Func<IApplicationTransaction, CancellationToken, Task<CommandReceipt>> work, CancellationToken token = default) =>
            inner.ExecuteAsync(c, i, async (tx, ct) => { await work(tx, ct); await PostgresTransaction.Require(tx).Platform.SaveChangesAsync(ct); throw new InvalidOperationException("Injected after all local writes, before commit."); }, token);
    }
    private sealed class PauseAfterFlush(IIdempotentCommands inner, TaskCompletionSource entered, TaskCompletionSource release) : IIdempotentCommands
    {
        public Task<CommandReceipt> ExecuteAsync(VerifiedContext c, CommandIdentity i, Func<IApplicationTransaction, CancellationToken, Task<CommandReceipt>> work, CancellationToken token = default) =>
            inner.ExecuteAsync(c, i, async (tx, ct) => { var result = await work(tx, ct); entered.SetResult(); await release.Task.WaitAsync(ct); return result; }, token);
    }
}

internal sealed class FinanceScenario(DatabaseFixture db, Guid tenant, Guid company, DateOnly date, Guid period, Guid expense, Guid cash)
{
    private static int year = 2100;
    public Guid Tenant => tenant; public Guid Company => company; public DateOnly Date => date; public Guid Period => period; public Guid Expense => expense; public Guid Cash => cash;
    public JournalCommands Commands { get; } = new(db.Uow, db.Commands, new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
    public IJournalQueries Queries { get; } = new JournalQueries(db.Uow, new AuditReader());
    public static async Task<FinanceScenario> Create(DatabaseFixture db, Guid? tenant = null, Guid? company = null)
    {
        var t = tenant ?? DatabaseFixture.TenantA; var c = company ?? DatabaseFixture.CompanyA; var y = Interlocked.Increment(ref year);
        var period = Guid.NewGuid(); var expense = Guid.NewGuid(); var cash = Guid.NewGuid();
        await db.Execute("INSERT INTO finance.accounting_books(tenant_id,company_id,currency,minor_units) VALUES(@tenant,@company,'JOD',3) ON CONFLICT DO NOTHING", ("tenant", t), ("company", c));
        await db.Execute("INSERT INTO finance.fiscal_periods(id,tenant_id,company_id,fiscal_year,starts_on,ends_on,status) VALUES(@id,@tenant,@company,@year,@start,@end,'Open')", ("id", period), ("tenant", t), ("company", c), ("year", y), ("start", new DateOnly(y, 1, 1)), ("end", new DateOnly(y, 12, 31)));
        foreach (var (id, name) in new[] { (expense, "Expense"), (cash, "Cash") })
            await db.Execute("INSERT INTO finance.accounts(id,tenant_id,company_id,code,name,status,posting_allowed) VALUES(@id,@tenant,@company,@code,@name,'Active',true)", ("id", id), ("tenant", t), ("company", c), ("code", id.ToString("N")), ("name", name));
        return new(db, t, c, new DateOnly(y, 6, 15), period, expense, cash);
    }
    public Task<VerifiedContext> Context(string permission) => db.Context(Tenant, Company, action: permission);
    public SaveDraftRequest Request(string debit = "100", string credit = "100") => new(Date, "JOD", "MANUAL", "Manual journal", [new(null, Expense, debit, "0", "Expense"), new(null, Cash, "0", credit, "Cash")]);
    public async Task<CommandReceipt> CreateDraft(string debit = "100", string credit = "100") => await Commands.CreateAsync(await Context(JournalPermissions.Create), Request(debit, credit));
    public CommandIdentity Identity(Guid id, long revision, string key) => new("Finance.PostManualJournal", id.ToString(), key, RequestFingerprint.Create("POST", id.ToString(), revision.ToString(System.Globalization.CultureInfo.InvariantCulture), "{}"));
    public async Task<CommandReceipt> Post(Guid id, long revision = 1, string key = "post", JournalCommands? commands = null) => await (commands ?? Commands).PostAsync(await Context(JournalPermissions.Post), id, revision, Identity(id, revision, key));
    public async Task RejectPost(Guid id, ErrorDefinition error, long revision = 1, string key = "post") => Assert.Equal(error, (await Assert.ThrowsAsync<FoundationException>(() => Post(id, revision, key))).Error);
    public async Task AssertOnePosting(Guid id)
    {
        Assert.Equal("Posted", await db.Scalar<string>("SELECT status FROM finance.journal_entries WHERE id=@id", ("id", id)));
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.journal-entry-posted.v1'", ("id", id.ToString())));
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND action='JournalPosted'", ("id", id.ToString())));
        Assert.Equal(1, await db.Scalar<long>("SELECT count(*) FROM integration.idempotency WHERE target=@id AND state='Completed'", ("id", id.ToString())));
    }
    public async Task AssertNoPosting(Guid id)
    {
        Assert.Equal("Draft", await db.Scalar<string>("SELECT status FROM finance.journal_entries WHERE id=@id", ("id", id)));
        Assert.Equal(0, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id", ("id", id.ToString())));
        Assert.Equal(0, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND action='JournalPosted'", ("id", id.ToString())));
    }
}
