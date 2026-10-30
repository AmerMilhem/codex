using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Application;
using Erp.Finance.Contracts;
using Erp.Finance.Infrastructure;
using Erp.Kernel;
using Erp.Platform.Infrastructure;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class TrialBalanceTests(DatabaseFixture db)
{
    [Fact]
    public async Task Golden_account_aggregation_uses_only_posted_entries_and_grand_totals_balance()
    {
        var s = await TrialBalanceScenario.Create(db); await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 10)); await s.Post(s.Cash, s.Revenue, "40", new(2026, 1, 15));
        await s.Draft(s.Expense, s.Cash, "999", new(2026, 1, 20));
        var r = await s.Report(new(2026, 1, 1), new(2026, 1, 31));
        Assert.Equal(3, r.Accounts.Count); Assert.Equal(new("0", "0", "140", "140", "100", "100"), r.Totals);
        Assert.Equal(new("0", "0", "100", "0", "100", "0"), s.Account(r, s.Expense));
        Assert.Equal(new("0", "0", "40", "100", "0", "60"), s.Account(r, s.Cash));
        Assert.Equal(new("0", "0", "0", "40", "0", "40"), s.Account(r, s.Revenue));
        Assert.Equal(2, r.LedgerControl.PostedJournalCount); Assert.Equal(4, r.LedgerControl.PostedLineCount); Assert.True(r.Reconciliation.Passed);
        Assert.Equal("company-book-lifetime", r.OpeningScope);
    }
    [Fact]
    public async Task Opening_period_closing_cross_fiscal_year_and_inclusive_date_boundaries_use_posting_date()
    {
        var s = await TrialBalanceScenario.Create(db);
        await s.Post(s.Expense, s.Cash, "10", new(2025, 12, 31));
        await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        await s.Post(s.Cash, s.Revenue, "40", new(2026, 1, 31));
        await s.Post(s.Expense, s.Cash, "900", new(2026, 2, 1));
        var r = await s.Report(new(2026, 1, 1), new(2026, 1, 31));
        Assert.Equal(new("10", "10", "140", "140", "110", "110"), r.Totals);
        Assert.Equal(new("10", "0", "100", "0", "110", "0"), s.Account(r, s.Expense));
        Assert.Equal(new("0", "10", "40", "100", "0", "70"), s.Account(r, s.Cash));
        var february = await s.Report(new(2026, 2, 1), new(2026, 2, 1));
        Assert.Equal(new("110", "110", "900", "900", "1010", "1010"), february.Totals);
        var jan31 = await s.Report(new(2026, 1, 31), new(2026, 1, 31)); Assert.Equal("40", jan31.Totals.PeriodDebit);
        // All these journals were created now. Historical PostingDate, rather than CreatedAt, selects the boundaries.
        Assert.Equal(3, r.LedgerControl.PostedJournalCount); Assert.True(r.Reconciliation.Passed);
    }
    [Fact]
    public async Task Closed_periods_and_now_inactive_accounts_remain_reportable()
    {
        var s = await TrialBalanceScenario.Create(db); await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1));
        await db.ClosePeriod(s.Period2026, s.Tenant, s.Company);
        await db.Execute("UPDATE finance.accounts SET status='Inactive',posting_allowed=false WHERE id=@account", ("account", s.Expense));
        var r = await s.Report(new(2026, 1, 1), new(2026, 12, 31)); Assert.True(r.Reconciliation.Passed); Assert.Equal("100", s.Account(r, s.Expense).PeriodDebit);
    }
    [Fact]
    public async Task Opening_only_account_is_retained_and_empty_ledger_returns_balanced_zero_totals()
    {
        var s = await TrialBalanceScenario.Create(db); var empty = await s.Report(new(2026, 1, 1), new(2026, 1, 31));
        Assert.Empty(empty.Accounts); Assert.Equal(new("0", "0", "0", "0", "0", "0"), empty.Totals); Assert.True(empty.Reconciliation.Passed);
        await s.Draft(s.Expense, s.Cash, "500", new(2026, 1, 1)); Assert.Empty((await s.Report(new(2026, 1, 1), new(2026, 1, 31))).Accounts);
        await s.Post(s.Expense, s.Cash, "10", new(2025, 12, 31));
        var openingOnly = await s.Report(new(2026, 1, 1), new(2026, 1, 31)); Assert.Equal(new("10", "10", "0", "0", "10", "10"), openingOnly.Totals); Assert.Equal(2, openingOnly.Accounts.Count);
    }
    [Fact]
    public async Task Multiple_journals_net_each_account_correctly_without_netting_period_movements()
    {
        var s = await TrialBalanceScenario.Create(db); await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1)); await s.Post(s.Cash, s.Expense, "100", new(2026, 1, 2));
        var r = await s.Report(new(2026, 1, 1), new(2026, 1, 31)); Assert.Equal(new("0", "0", "200", "200", "0", "0"), r.Totals); Assert.Equal(2, r.Accounts.Count);
        var later = await s.Report(new(2026, 2, 1), new(2026, 2, 28)); Assert.Empty(later.Accounts); Assert.True(later.Reconciliation.Passed);
    }
    [Fact]
    public async Task Many_small_and_large_amounts_preserve_exact_aggregate_beyond_single_journal_bound()
    {
        var s = await TrialBalanceScenario.Create(db);
        for (var i = 0; i < 20; i++) await s.Post(s.Expense, s.Cash, "0.001", new(2026, 1, 1));
        for (var i = 0; i < 2; i++) await s.Post(s.Expense, s.Cash, "999999999999999999.999", new(2026, 1, 1));
        var r = await s.Report(new(2026, 1, 1), new(2026, 1, 31)); Assert.Equal("2000000000000000000.018", r.Totals.PeriodDebit); Assert.Equal(r.Totals.PeriodDebit, r.Totals.PeriodCredit); Assert.True(r.Reconciliation.Passed);
    }
    [Fact]
    public async Task Tenant_company_scope_and_permission_are_enforced_independently_of_URL()
    {
        var s = await TrialBalanceScenario.Create(db); var otherCompany = await TrialBalanceScenario.Create(db, tenant: s.Tenant); var otherTenant = await TrialBalanceScenario.Create(db);
        await s.Post(s.Expense, s.Cash, "10", new(2026, 1, 1)); await otherCompany.Post(otherCompany.Expense, otherCompany.Cash, "200", new(2026, 1, 1)); await otherTenant.Post(otherTenant.Expense, otherTenant.Cash, "300", new(2026, 1, 1));
        Assert.Equal("10", (await s.Report(new(2026, 1, 1), new(2026, 1, 31))).Totals.PeriodDebit);
        var resolver = new ContextResolver(s.Membership);
        foreach (var foreign in new[] { otherCompany, otherTenant }) Assert.Equal(Errors.AUTH_SCOPE_DENIED,
            (await Assert.ThrowsAsync<FoundationException>(async () => await resolver.ResolveAsync(new("tests", "reader"), foreign.Tenant, foreign.Company, JournalPermissions.View, "test", "test", ExecutionSource.Test))).Error);
        var wrongAction = await s.Context(JournalPermissions.Create);
        Assert.Equal(Errors.AUTH_SCOPE_DENIED, (await Assert.ThrowsAsync<FoundationException>(() => s.Query.RunAsync(wrongAction, new(2026, 1, 1), new(2026, 1, 31)))).Error);
    }
    [Fact]
    public async Task Running_and_refreshing_report_changes_no_financial_or_integration_state()
    {
        var s = await TrialBalanceScenario.Create(db); await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1)); await s.Draft(s.Expense, s.Cash, "900", new(2026, 1, 1));
        var before = await s.StateDigest(); await s.Report(new(2026, 1, 1), new(2026, 1, 31)); await s.Report(new(2026, 1, 1), new(2026, 1, 31)); Assert.Equal(before, await s.StateDigest());
    }
    [Fact]
    public async Task Read_only_report_pool_reuse_does_not_leak_scope_or_transaction_settings()
    {
        var a = await TrialBalanceScenario.Create(db); var b = await TrialBalanceScenario.Create(db);
        await a.Post(a.Expense, a.Cash, "100", new(2026, 1, 1)); await b.Post(b.Expense, b.Cash, "200", new(2026, 1, 1));
        await using var pool = new NpgsqlDataSourceBuilder(new NpgsqlConnectionStringBuilder(db.Runtime) { MaxPoolSize = 1 }.ToString()).Build();
        var query = new TrialBalanceQuery(new TrialBalanceReader(pool, NullLogger<TrialBalanceReader>.Instance));
        Assert.Equal("100", (await query.RunAsync(await a.Context(), new(2026, 1, 1), new(2026, 1, 31))).Totals.PeriodDebit);
        Assert.Equal("200", (await query.RunAsync(await b.Context(), new(2026, 1, 1), new(2026, 1, 31))).Totals.PeriodDebit);
        await using var connection = await pool.OpenConnectionAsync();
        await using var count = Sql.Command(connection, "SELECT count(*) FROM finance.journal_entries"); Assert.Equal(0L, await count.ExecuteScalarAsync());
        await using var setting = Sql.Command(connection, "SHOW transaction_read_only"); Assert.Equal("off", await setting.ExecuteScalarAsync());
    }
    [Fact]
    public async Task Corrupt_ledger_is_visible_and_opposite_journal_errors_cannot_cancel_the_failure()
    {
        var s = await TrialBalanceScenario.Create(db); var one = await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1)); var two = await s.Post(s.Cash, s.Revenue, "40", new(2026, 1, 2));
        // Deliberate corruption only on this disposable test database, with guards restored before executing the report.
        // Runtime permissions cannot perform this mutation. No production migration or repair path is introduced.
        await db.Execute("ALTER TABLE finance.journal_lines DISABLE TRIGGER USER");
        try { await db.Execute("UPDATE finance.journal_lines SET debit=debit+1 WHERE journal_id=@one AND debit>0; UPDATE finance.journal_lines SET credit=credit+1 WHERE journal_id=@two AND credit>0", ("one", one.Reference), ("two", two.Reference)); }
        finally { await db.Execute("ALTER TABLE finance.journal_lines ENABLE TRIGGER USER"); }
        var before = await s.StateDigest(); var r = await s.Report(new(2026, 1, 1), new(2026, 1, 31));
        Assert.Equal(r.Totals.PeriodDebit, r.Totals.PeriodCredit); Assert.False(r.Reconciliation.Passed); Assert.Equal(2, r.LedgerControl.InvalidJournalCount);
        Assert.Contains(r.Reconciliation.Checks, x => x.Code == "TB_LEDGER_JOURNAL_INTEGRITY" && !x.Passed); Assert.Equal(before, await s.StateDigest());
        Assert.Contains(s.Log.Messages, x => x.Contains("TB_LEDGER_JOURNAL_INTEGRITY", StringComparison.Ordinal));
    }
    [Fact]
    public async Task Http_read_contract_validates_range_authentication_scope_and_current_permission()
    {
        var s = await TrialBalanceScenario.Create(db, DatabaseFixture.TenantA, DatabaseFixture.CompanyA);
        await s.Post(s.Expense, s.Cash, "100", new(2026, 1, 1)); await using var factory = new ApiFactory(db); factory.Members.Permissions.Add(JournalPermissions.View);
        using var client = factory.CreateClient(); var path = $"/api/v1/tenants/{s.Tenant}/companies/{s.Company}/finance/trial-balance";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path + "?startDate=2026-01-01&endDate=2026-01-31")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token);
        foreach (var query in new[] { "", "?startDate=2026-02-01&endDate=2026-01-01", "?startDate=invalid&endDate=2026-01-31" }) Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + query)).StatusCode);
        var report = await client.GetFromJsonAsync<TrialBalanceReport>(path + "?startDate=2026-01-01&endDate=2026-01-31"); Assert.NotNull(report); Assert.True(report.Reconciliation.Passed);
        factory.Members.Permissions.Remove(JournalPermissions.View); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path + "?startDate=2026-01-01&endDate=2026-01-31")).StatusCode);
        factory.Members.Permissions.Add(JournalPermissions.View);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path.Replace(s.Company.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal) + "?startDate=2026-01-01&endDate=2026-01-31")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path.Replace(s.Tenant.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal) + "?startDate=2026-01-01&endDate=2026-01-31")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/finance/trial-balance?startDate=2026-01-01&endDate=2026-01-31")).StatusCode);
    }
}

internal sealed class TrialBalanceScenario(DatabaseFixture db, Guid tenant, Guid company, Guid expense, Guid cash, Guid revenue, Guid period2026)
{
    public Guid Tenant => tenant; public Guid Company => company; public Guid Expense => expense; public Guid Cash => cash; public Guid Revenue => revenue; public Guid Period2026 => period2026;
    public Memberships Membership { get; } = new(tenant, company);
    public TestReportLogger Log { get; } = new();
    public ITrialBalanceQuery Query => new TrialBalanceQuery(new TrialBalanceReader(db.Source, Log));
    public async Task<VerifiedContext> Context(string action = JournalPermissions.View) => await new ContextResolver(Membership).ResolveAsync(new("tests", "reader"), Tenant, Company, action, "trial-balance-tests", Guid.NewGuid().ToString("N"), ExecutionSource.Test);
    public async Task<TrialBalanceReport> Report(DateOnly start, DateOnly end) => await Query.RunAsync(await Context(), start, end);
    public static async Task<TrialBalanceScenario> Create(DatabaseFixture db, Guid? tenant = null, Guid? company = null)
    {
        var t = tenant ?? Guid.NewGuid(); var c = company ?? Guid.NewGuid(); var expense = Guid.NewGuid(); var cash = Guid.NewGuid(); var revenue = Guid.NewGuid(); var period = Guid.NewGuid();
        await db.Execute("INSERT INTO finance.accounting_books(tenant_id,company_id,currency,minor_units) VALUES(@tenant,@company,'JOD',3) ON CONFLICT DO NOTHING", ("tenant", t), ("company", c));
        // Default A is shared with earlier regression cases; avoid conflicting account codes/date periods there.
        var shared = company == DatabaseFixture.CompanyA;
        foreach (var (id, code, name) in new[] { (expense, "5000", "Expense"), (cash, "1000", "Cash"), (revenue, "4000", "Revenue") })
            await db.Execute("INSERT INTO finance.accounts(id,tenant_id,company_id,code,name,status,posting_allowed) VALUES(@id,@tenant,@company,@code,@name,'Active',true)", ("id", id), ("tenant", t), ("company", c), ("code", shared ? id.ToString("N") : code), ("name", name));
        foreach (var year in new[] { 2025, 2026 })
            await db.Execute("INSERT INTO finance.fiscal_periods(id,tenant_id,company_id,fiscal_year,starts_on,ends_on,status) VALUES(@id,@tenant,@company,@year,@start,@end,'Open')", ("id", year == 2026 ? period : Guid.NewGuid()), ("tenant", t), ("company", c), ("year", year), ("start", new DateOnly(year, 1, 1)), ("end", new DateOnly(year, 12, 31)));
        return new(db, t, c, expense, cash, revenue, period);
    }
    public TrialBalanceAmounts Account(TrialBalanceReport report, Guid account) => report.Accounts.Single(x => x.AccountId == account).Amounts;
    public async Task<CommandReceipt> Draft(Guid debit, Guid credit, string amount, DateOnly date)
    {
        var commands = new JournalCommands(db.Uow, db.Commands, new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
        return await commands.CreateAsync(await Context(JournalPermissions.Create), new(date, "JOD", "TB", "Trial balance test", [new(null, debit, amount, "0", ""), new(null, credit, "0", amount, "")]));
    }
    public async Task<CommandReceipt> Post(Guid debit, Guid credit, string amount, DateOnly date)
    {
        var draft = await Draft(debit, credit, amount, date); var commands = new JournalCommands(db.Uow, db.Commands, new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
        return await commands.PostAsync(await Context(JournalPermissions.Post), draft.Reference, draft.AppliedRevision, new("Finance.PostManualJournal", draft.Reference.ToString(), "post", RequestFingerprint.Create("POST", draft.Reference.ToString(), "1", "{}")));
    }
    public Task<string> StateDigest() => db.Scalar<string>("""
        SELECT md5(coalesce(string_agg(value,'|' ORDER BY value),'')) FROM (
          SELECT to_jsonb(j)::text AS value FROM finance.journal_entries j WHERE tenant_id=@tenant AND company_id=@company
          UNION ALL SELECT to_jsonb(l)::text FROM finance.journal_lines l WHERE tenant_id=@tenant AND company_id=@company
          UNION ALL SELECT to_jsonb(a)::text FROM finance.accounts a WHERE tenant_id=@tenant AND company_id=@company
          UNION ALL SELECT to_jsonb(b)::text FROM finance.accounting_books b WHERE tenant_id=@tenant AND company_id=@company
          UNION ALL SELECT to_jsonb(p)::text FROM finance.fiscal_periods p WHERE tenant_id=@tenant AND company_id=@company
          UNION ALL SELECT to_jsonb(r)::text FROM finance.journal_reversals r WHERE tenant_id=@tenant AND company_id=@company
          UNION ALL SELECT to_jsonb(n)::text FROM finance.journal_numbers n WHERE tenant_id=@tenant AND company_id=@company
          UNION ALL SELECT to_jsonb(e)::text FROM audit.entries e WHERE tenant_id=@tenant AND company_id=@company
          UNION ALL SELECT to_jsonb(o)::text FROM integration.outbox o WHERE tenant_id=@tenant AND company_id=@company
          UNION ALL SELECT to_jsonb(i)::text FROM integration.idempotency i WHERE tenant_id=@tenant AND company_id=@company
          UNION ALL SELECT to_jsonb(i)::text FROM integration.inbox i WHERE tenant_id=@tenant AND company_id=@company
        ) all_state
        """, ("tenant", Tenant), ("company", Company));
    internal sealed class Memberships(Guid tenant, Guid company) : IMembershipStore
    {
        public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken token) => ValueTask.FromResult<PrincipalMembership?>(new(true, [new(tenant, company, new HashSet<string> { JournalPermissions.View, JournalPermissions.Create, JournalPermissions.Edit, JournalPermissions.Post, JournalPermissions.Reverse, JournalPermissions.Correct })]));
    }
    internal sealed class TestReportLogger : ILogger<TrialBalanceReader>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
