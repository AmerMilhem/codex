using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Erp.Finance.Application;
using Erp.Finance.Contracts;
using Erp.Finance.Infrastructure;
using Erp.Kernel;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class GeneralLedgerTests(DatabaseFixture db)
{
    private GeneralLedgerQuery Query => new(new GeneralLedgerReader(db.Source, NullLogger<GeneralLedgerReader>.Instance));
    private async Task<GeneralLedgerReport> Read(TrialBalanceScenario s, int offset = 0, int limit = 50, Guid? account = null) =>
        await Query.RunAsync(await s.Context(), account ?? s.Cash, new(2026, 1, 1), new(2026, 1, 31), offset, limit);
    [Fact]
    public async Task Golden_opening_movements_running_closing_match_trial_balance_and_trace_posted_journal()
    {
        var s = await TrialBalanceScenario.Create(db);
        await s.Post(s.Cash, s.Revenue, "100", new(2025, 12, 31));
        var first = await s.Post(s.Cash, s.Revenue, "40", new(2026, 1, 1));
        var second = await s.Post(s.Expense, s.Cash, "25", new(2026, 1, 31));
        await s.Draft(s.Cash, s.Revenue, "999", new(2026, 1, 15));
        await s.Post(s.Cash, s.Revenue, "800", new(2026, 2, 1));
        var r = await Read(s); Assert.Equal("100", r.OpeningBalance); Assert.Equal("40", r.PeriodDebit); Assert.Equal("25", r.PeriodCredit); Assert.Equal("115", r.ClosingBalance);
        Assert.Equal(["140", "115"], r.Movements.Select(x => x.RunningBalance)); Assert.Equal([first.Reference, second.Reference], r.Movements.Select(x => x.JournalId));
        Assert.Equal(2, r.TotalCount); Assert.True(r.Reconciliation.Passed); Assert.Equal("company-book-lifetime", r.OpeningScope);
        var tb = await s.Report(r.StartDate, r.EndDate); Assert.Equal("115", s.Account(tb, s.Cash).ClosingDebit);
        var journals = new JournalQueries(db.Uow, new Erp.Platform.Infrastructure.AuditReader());
        var j = await journals.GetAsync(await s.Context(), first.Reference, default);
        Assert.Equal("Posted", j.Status); Assert.Equal(j.JournalNumber, r.Movements[0].JournalNumber);
        Assert.Contains(await journals.AuditAsync(await s.Context(), first.Reference, default), x => x.Action == "JournalPosted" && x.CorrelationId.Length > 0);
    }
    [Fact]
    public async Task Same_date_order_is_deterministic_and_every_page_size_preserves_running_balances()
    {
        var s = await TrialBalanceScenario.Create(db); await s.Post(s.Cash, s.Revenue, "100", new(2025, 12, 31));
        for (var n = 1; n <= 13; n++) await s.Post(n % 2 == 0 ? s.Expense : s.Cash, n % 2 == 0 ? s.Cash : s.Revenue, n.ToString(System.Globalization.CultureInfo.InvariantCulture), new(2026, 1, 1));
        var full = await Read(s); Assert.Equal(13, full.TotalCount);
        Assert.Equal(full.Movements.Select(x => x.JournalId).OrderBy(x => x.ToString(), StringComparer.Ordinal), full.Movements.Select(x => x.JournalId));
        foreach (var size in new[] { 1, 3, 5, 13, 200 })
        {
            var all = new List<GeneralLedgerMovement>(); var prior = full.OpeningBalance;
            for (var offset = 0; offset < full.TotalCount; offset += size)
            {
                var page = await Read(s, offset, size); Assert.Equal(prior, page.PageOpeningBalance);
                Assert.Equal(full.PeriodDebit, page.PeriodDebit); Assert.Equal(full.PeriodCredit, page.PeriodCredit); Assert.Equal(full.ClosingBalance, page.ClosingBalance);
                Assert.True(page.Reconciliation.Passed); all.AddRange(page.Movements); prior = page.Movements[^1].RunningBalance;
            }
            Assert.Equal(full.Movements, all); Assert.Equal(full.ClosingBalance, prior);
        }
        var beyond = await Read(s, 200, 5); Assert.Empty(beyond.Movements); Assert.Equal(full.ClosingBalance, beyond.PageOpeningBalance);
    }
    [Fact]
    public async Task Same_journal_multiple_account_lines_use_position_tie_breaker()
    {
        var s = await TrialBalanceScenario.Create(db);
        var commands = new JournalCommands(db.Uow, db.Commands, new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
        var draft = await commands.CreateAsync(await s.Context(JournalPermissions.Create), new(new(2026, 1, 1), "JOD", "ORDER", "Multiple cash lines",
            [new(null, s.Cash, "40", "0", "First"), new(null, s.Cash, "0", "25", "Second"), new(null, s.Revenue, "0", "15", "Offset")]));
        await commands.PostAsync(await s.Context(JournalPermissions.Post), draft.Reference, draft.AppliedRevision, new("Finance.PostManualJournal", draft.Reference.ToString(), "post", Erp.Platform.Infrastructure.RequestFingerprint.Create("POST", draft.Reference.ToString(), "1", "{}")));
        var r = await Read(s); Assert.Equal(["40", "15"], r.Movements.Select(x => x.RunningBalance)); Assert.Equal(["First", "Second"], r.Movements.Select(x => x.Description));
        Assert.True(r.Movements[0].Position < r.Movements[1].Position); Assert.True(r.Reconciliation.Passed);
    }
    [Fact]
    public async Task Empty_and_opening_only_accounts_keep_exact_balances_and_closed_periods_readable()
    {
        var s = await TrialBalanceScenario.Create(db); var empty = await Read(s); Assert.Empty(empty.Movements); Assert.Equal("0", empty.ClosingBalance); Assert.True(empty.Reconciliation.Passed);
        await s.Post(s.Expense, s.Cash, "10", new(2025, 12, 31));
        await db.ClosePeriod(s.Period2026, s.Tenant, s.Company);
        await db.Execute("UPDATE finance.accounts SET status='Inactive',posting_allowed=false WHERE id=@cash", ("cash", s.Cash));
        var r = await Read(s); Assert.Empty(r.Movements); Assert.Equal("-10", r.OpeningBalance); Assert.Equal("-10", r.ClosingBalance); Assert.True(r.Reconciliation.Passed);
    }
    [Fact]
    public async Task Many_small_and_large_movements_accumulate_without_precision_loss()
    {
        var s = await TrialBalanceScenario.Create(db);
        for (var i = 0; i < 20; i++) await s.Post(s.Cash, s.Revenue, "0.001", new(2026, 1, 1));
        for (var i = 0; i < 2; i++) await s.Post(s.Cash, s.Revenue, "999999999999999999.999", new(2026, 1, 2));
        var r = await Read(s); Assert.Equal("2000000000000000000.018", r.ClosingBalance); Assert.Equal(r.ClosingBalance, r.Movements[^1].RunningBalance); Assert.True(r.Reconciliation.Passed);
        var last = await Read(s, 21, 1); Assert.Equal(r.ClosingBalance, last.Movements[0].RunningBalance);
    }
    [Fact]
    public async Task Other_accounts_tenants_companies_and_modified_identifiers_cannot_leak()
    {
        var a = await TrialBalanceScenario.Create(db); var b = await TrialBalanceScenario.Create(db, tenant: a.Tenant); var c = await TrialBalanceScenario.Create(db);
        await a.Post(a.Cash, a.Revenue, "10", new(2026, 1, 1)); await b.Post(b.Cash, b.Revenue, "200", new(2026, 1, 1)); await c.Post(c.Cash, c.Revenue, "300", new(2026, 1, 1));
        Assert.Equal("10", (await Read(a)).PeriodDebit); Assert.Empty((await Read(a, account: a.Expense)).Movements);
        foreach (var foreign in new[] { b.Cash, c.Cash, Guid.NewGuid() })
            Assert.Equal(Errors.RESOURCE_NOT_FOUND, (await Assert.ThrowsAsync<FoundationException>(() => Read(a, account: foreign))).Error);
        Assert.Equal(Errors.AUTH_SCOPE_DENIED, (await Assert.ThrowsAsync<FoundationException>(async () => await Query.RunAsync(await a.Context(JournalPermissions.Create), a.Cash, new(2026, 1, 1), new(2026, 1, 31)))).Error);
    }
    [Fact]
    public async Task Read_and_paginate_have_no_financial_audit_outbox_or_idempotency_mutation()
    {
        var s = await TrialBalanceScenario.Create(db); await s.Post(s.Cash, s.Revenue, "40", new(2026, 1, 1)); await s.Post(s.Expense, s.Cash, "25", new(2026, 1, 2));
        var before = await s.StateDigest(); await Read(s); await Read(s, 1, 1); await Read(s, 0, 1); Assert.Equal(before, await s.StateDigest());
    }
    [Fact]
    public async Task Pool_reuse_resets_RLS_and_read_only_context()
    {
        var a = await TrialBalanceScenario.Create(db); var b = await TrialBalanceScenario.Create(db);
        await a.Post(a.Cash, a.Revenue, "10", new(2026, 1, 1)); await b.Post(b.Cash, b.Revenue, "20", new(2026, 1, 1));
        await using var pool = new NpgsqlDataSourceBuilder(new NpgsqlConnectionStringBuilder(db.Runtime) { MaxPoolSize = 1 }.ToString()).Build();
        var q = new GeneralLedgerQuery(new GeneralLedgerReader(pool, NullLogger<GeneralLedgerReader>.Instance));
        Assert.Equal("10", (await q.RunAsync(await a.Context(), a.Cash, new(2026, 1, 1), new(2026, 1, 31))).ClosingBalance);
        Assert.Equal("20", (await q.RunAsync(await b.Context(), b.Cash, new(2026, 1, 1), new(2026, 1, 31))).ClosingBalance);
        await using var connection = await pool.OpenConnectionAsync();
        await using var cmd = Erp.Platform.Infrastructure.Persistence.Sql.Command(connection, "SELECT count(*) FROM finance.journal_lines"); Assert.Equal(0L, await cmd.ExecuteScalarAsync());
        await using var setting = Erp.Platform.Infrastructure.Persistence.Sql.Command(connection, "SHOW transaction_read_only"); Assert.Equal("off", await setting.ExecuteScalarAsync());
    }
    [Fact]
    public async Task Http_contract_enforces_context_permission_account_dates_and_pagination()
    {
        // Reuse A's book/periods if another regression test already populated them; this case needs only a scoped account.
        var id = Guid.NewGuid(); await db.Execute("INSERT INTO finance.accounting_books(tenant_id,company_id,currency,minor_units) VALUES(@t,@c,'JOD',3) ON CONFLICT DO NOTHING; INSERT INTO finance.accounts(id,tenant_id,company_id,code,name,status,posting_allowed) VALUES(@id,@t,@c,@code,'HTTP GL','Active',true)", ("t", DatabaseFixture.TenantA), ("c", DatabaseFixture.CompanyA), ("id", id), ("code", id.ToString("N")));
        await using var factory = new ApiFactory(db); factory.Members.Permissions.Add(JournalPermissions.View); using var client = factory.CreateClient();
        var path = $"/api/v1/tenants/{DatabaseFixture.TenantA}/companies/{DatabaseFixture.CompanyA}/finance/general-ledger";
        var qs = $"?accountId={id}&startDate=2026-01-01&endDate=2026-01-31";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path + qs)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token);
        var r = await client.GetFromJsonAsync<GeneralLedgerReport>(path + qs); Assert.NotNull(r); Assert.True(r.Reconciliation.Passed);
        foreach (var suffix in new[] { "&offset=-1", "&limit=0", "&limit=201" }) Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + qs + suffix)).StatusCode);
        foreach (var invalid in new[] { "", $"?accountId={id}", $"?accountId={id}&startDate=2026-02-01&endDate=2026-01-01", "?accountId=bad&startDate=2026-01-01&endDate=2026-01-31" }) Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path + qs.Replace(id.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal))).StatusCode);
        foreach (var old in new[] { DatabaseFixture.TenantA, DatabaseFixture.CompanyA }) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path.Replace(old.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal) + qs)).StatusCode);
        factory.Members.Permissions.Remove(JournalPermissions.View); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path + qs)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/finance/general-ledger" + qs)).StatusCode);
    }
}
