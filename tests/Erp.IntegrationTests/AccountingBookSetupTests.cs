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
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Erp.IntegrationTests;

[Collection("Postgres")]
public sealed class AccountingBookSetupTests(DatabaseFixture db)
{
    private CreateAccountingBookCommand Command => new(db.Commands, new AccountingBookSetupStore(), db.Audit, db.Outbox, TimeProvider.System);
    private static CommandIdentity Identity(CreateAccountingBookRequest request, string key) => new(
        "Finance.CreateAccountingBook", "books", key,
        RequestFingerprint.Create("POST", "books", "0", JsonSerializer.Serialize(request)));
    private static async Task<VerifiedContext> Context(Guid tenant, Guid company, string permission) =>
        await new ContextResolver(new Membership(tenant, company)).ResolveAsync(new("tests", "book-creator"),
            tenant, company, permission, "book-setup-tests", Guid.NewGuid().ToString("N"), ExecutionSource.Test);
    private sealed class Membership(Guid tenant, Guid company) : IMembershipStore
    {
        public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken token) =>
            ValueTask.FromResult<PrincipalMembership?>(new(true, [new(tenant, company,
                new HashSet<string> { BookPermissions.Create, BookPermissions.View, AccountPermissions.Create,
                    AccountPermissions.View, PeriodPermissions.Create, PeriodPermissions.Close, JournalPermissions.Create,
                    JournalPermissions.Edit, JournalPermissions.Post, JournalPermissions.Reverse, JournalPermissions.View })]));
    }
    private async Task<CommandReceipt> Create(Guid tenant, Guid company, string code = "PRIMARY", string name = "Primary Accounting Book",
        string currency = "JOD", string? key = null, IIdempotentCommands? executor = null)
    {
        var request = new CreateAccountingBookRequest(code, name, currency);
        var command = new CreateAccountingBookCommand(executor ?? db.Commands, new AccountingBookSetupStore(), db.Audit, db.Outbox, TimeProvider.System);
        return await command.ExecuteAsync(await Context(tenant, company, BookPermissions.Create), request,
            Identity(request, key ?? Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public async Task Book_starts_Active_with_currency_precision_creator_audit_and_outbox()
    {
        var tenant = Guid.NewGuid(); var company = Guid.NewGuid();
        var result = await Create(tenant, company, " primary ", " Main Book ");
        var rows = await new AccountingBookSetupQueries(db.Uow, new AccountingBookSetupStore())
            .ListAsync(await Context(tenant, company, BookPermissions.View));
        var book = Assert.Single(rows);
        Assert.Equal(result.Reference, book.Id); Assert.Equal("PRIMARY", book.Code);
        Assert.Equal("Main Book", book.Name); Assert.Equal("JOD", book.FunctionalCurrency);
        Assert.Equal(3, book.MinorUnits); Assert.Equal("Active", book.Status);
        Assert.NotNull(book.CreatedAt); Assert.Equal("book-creator", book.CreatedBySubject);
        Assert.Equal(2L, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_type='AccountingBook' AND entity_id=@id AND action IN ('AccountingBookCreateRequested','AccountingBookCreated')", ("id", book.Id.ToString())));
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.accounting-book-created.v1'", ("id", book.Id.ToString())));
    }

    [Fact]
    public async Task Invalid_code_name_currency_and_existing_company_book_are_rejected()
    {
        var tenant = Guid.NewGuid(); var company = Guid.NewGuid();
        foreach (var code in new[] { "", "A B", "A/B", new string('Z', 33) })
            Assert.Equal(BookSetupErrors.InvalidCode, (await Assert.ThrowsAsync<FoundationException>(() =>
                Create(tenant, company, code))).Error);
        Assert.Equal(BookSetupErrors.InvalidName, (await Assert.ThrowsAsync<FoundationException>(() =>
            Create(tenant, company, name: "  "))).Error);
        Assert.Equal(BookSetupErrors.InvalidCurrency, (await Assert.ThrowsAsync<FoundationException>(() =>
            Create(tenant, company, currency: "ZZZ"))).Error);
        await Create(tenant, company);
        foreach (var code in new[] { "primary", "SECONDARY" })
            Assert.Equal(BookSetupErrors.Duplicate, (await Assert.ThrowsAsync<FoundationException>(() =>
                Create(tenant, company, code))).Error);
    }

    [Fact]
    public async Task Concurrent_duplicates_and_matching_retries_have_one_book_and_event()
    {
        var tenant = Guid.NewGuid(); var company = Guid.NewGuid();
        var same = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Create(tenant, company, key: "same")));
        Assert.Single(same.Select(x => x.Reference).Distinct());
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM finance.accounting_books WHERE tenant_id=@t AND company_id=@c", ("t", tenant), ("c", company)));
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.accounting-book-created.v1'", ("id", same[0].Reference.ToString())));
        var otherTenant = Guid.NewGuid(); var otherCompany = Guid.NewGuid();
        var competing = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            try { await Create(otherTenant, otherCompany, i == 0 ? "PRIMARY" : "OTHER", key: "race-" + i); return true; }
            catch (FoundationException error) { Assert.Equal(BookSetupErrors.Duplicate, error.Error); return false; }
        }));
        Assert.Single(competing, x => x);
    }

    [Fact]
    public async Task Lost_response_and_different_payload_conflict_without_duplicate_effect()
    {
        var tenant = Guid.NewGuid(); var company = Guid.NewGuid();
        async Task Lose() { await Create(tenant, company, key: "lost"); throw new TimeoutException(); }
        await Assert.ThrowsAsync<TimeoutException>(Lose);
        var retry = await Create(tenant, company, key: "lost");
        foreach (var (code, currency) in new[] { ("OTHER", "JOD"), ("PRIMARY", "USD") })
            Assert.Equal(Errors.API_IDEMPOTENCY_CONFLICT, (await Assert.ThrowsAsync<FoundationException>(() =>
                Create(tenant, company, code: code, currency: currency, key: "lost"))).Error);
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM finance.accounting_books WHERE tenant_id=@t AND company_id=@c", ("t", tenant), ("c", company)));
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.accounting-book-created.v1'", ("id", retry.Reference.ToString())));
    }

    [Fact]
    public async Task Independent_permission_and_tenant_company_RLS_isolate_books()
    {
        var tenant = Guid.NewGuid(); var company = Guid.NewGuid();
        var request = new CreateAccountingBookRequest("PRIMARY", "Book", "JOD");
        foreach (var permission in new[] { BookPermissions.View, AccountPermissions.Create,
            PeriodPermissions.Create, JournalPermissions.Post })
            Assert.Equal(Errors.AUTH_SCOPE_DENIED, (await Assert.ThrowsAsync<FoundationException>(async () =>
                await Command.ExecuteAsync(await Context(tenant, company, permission), request,
                    Identity(request, permission)))).Error);
        var first = await Create(tenant, company);
        var sameTenantOtherCompany = await Create(tenant, Guid.NewGuid());
        var otherTenant = await Create(Guid.NewGuid(), Guid.NewGuid());
        var queries = new AccountingBookSetupQueries(db.Uow, new AccountingBookSetupStore());
        for (var pass = 0; pass < 3; pass++)
        {
            var visible = await queries.ListAsync(await Context(tenant, company, BookPermissions.View));
            Assert.Equal(first.Reference, Assert.Single(visible).Id);
        }
        Assert.NotEqual(first.Reference, sameTenantOtherCompany.Reference);
        Assert.NotEqual(first.Reference, otherTenant.Reference);
        Assert.True(await db.Scalar<bool>("SELECT relforcerowsecurity FROM pg_class WHERE oid='finance.accounting_books'::regclass"));
    }

    [Fact]
    public async Task Failure_after_insert_rolls_back_book_audit_outbox_and_receipt()
    {
        var tenant = Guid.NewGuid(); var company = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(tenant, company, key: "rollback",
            executor: new FailAfterWork(db.Commands)));
        Assert.Equal(0L, await db.Scalar<long>("SELECT count(*) FROM finance.accounting_books WHERE tenant_id=@t AND company_id=@c", ("t", tenant), ("c", company)));
        Assert.Equal(0L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE tenant_id=@t AND company_id=@c AND type='finance.accounting-book-created.v1'", ("t", tenant), ("c", company)));
        await Create(tenant, company, key: "rollback");
    }
    private sealed class FailAfterWork(IIdempotentCommands inner) : IIdempotentCommands
    {
        public Task<CommandReceipt> ExecuteAsync(VerifiedContext context, CommandIdentity identity,
            Func<IApplicationTransaction, CancellationToken, Task<CommandReceipt>> work, CancellationToken token = default) =>
            inner.ExecuteAsync(context, identity, async (tx, ct) => { await work(tx, ct); throw new InvalidOperationException("injected"); }, token);
    }

    [Fact]
    public async Task Book_period_accounts_journal_reports_reversal_and_close_share_one_scope()
    {
        var tenant = Guid.NewGuid(); var company = Guid.NewGuid();
        var book = await Create(tenant, company);
        var period = await db.CreatePeriod(tenant, company, new(2027, 1, 1), new(2027, 1, 31), Guid.NewGuid().ToString());
        async Task<Guid> Account(string code, string name)
        {
            var request = new CreateAccountRequest(code, name, true);
            var command = new CreateAccountCommand(db.Commands, new AccountSetupStore(), db.Audit, db.Outbox, TimeProvider.System);
            var identity = new CommandIdentity("Finance.CreateAccount", "accounts", Guid.NewGuid().ToString("N"),
                RequestFingerprint.Create("POST", "accounts", "0", JsonSerializer.Serialize(request)));
            return (await command.ExecuteAsync(await Context(tenant, company, AccountPermissions.Create), request, identity)).Reference;
        }
        var expense = await Account("6100", "Office Expense"); var cash = await Account("1100", "Cash");
        var journal = new JournalCommands(db.Uow, db.Commands, new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
        var date = new DateOnly(2027, 1, 1);
        var request = new SaveDraftRequest(date, "JOD", "Book golden", "Book setup journal",
            [new(null, expense, "100", "0", ""), new(null, cash, "0", "100", "")]);
        var draft = await journal.CreateAsync(await Context(tenant, company, JournalPermissions.Create), request);
        Assert.True((await journal.ValidateAsync(await Context(tenant, company, JournalPermissions.Edit), draft.Reference, 1)).Valid);
        var posted = await journal.PostAsync(await Context(tenant, company, JournalPermissions.Post), draft.Reference, 1,
            new("Finance.PostManualJournal", draft.Reference.ToString(), "book-post",
                RequestFingerprint.Create("POST", draft.Reference.ToString(), "1", "{}")));
        var report = await new TrialBalanceQuery(new TrialBalanceReader(db.Source, NullLogger<TrialBalanceReader>.Instance))
            .RunAsync(await Context(tenant, company, JournalPermissions.View), date, new DateOnly(2027, 1, 31));
        Assert.True(report.Reconciliation.Passed); Assert.Equal("100", report.Totals.PeriodDebit);
        Assert.Contains(report.Accounts, x => x.AccountId == expense && x.Amounts.PeriodDebit == "100");
        var gl = await new GeneralLedgerQuery(new GeneralLedgerReader(db.Source, NullLogger<GeneralLedgerReader>.Instance))
            .RunAsync(await Context(tenant, company, JournalPermissions.View), expense, date, new DateOnly(2027, 1, 31));
        Assert.True(gl.Reconciliation.Passed); Assert.Single(gl.Movements);
        var reverseRequest = new ReverseJournalRequest(new DateOnly(2027, 1, 2), "Correction");
        var reverse = await new ReverseJournalCommand(db.Commands, new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System)
            .ExecuteAsync(await Context(tenant, company, JournalPermissions.Reverse), posted.Reference, 2, reverseRequest,
                new("Finance.ReverseManualJournal", posted.Reference.ToString(), "book-reverse",
                    RequestFingerprint.Create("POST", posted.Reference.ToString(), "2", JsonSerializer.Serialize(reverseRequest))));
        Assert.NotEqual(posted.Reference, reverse.Reference);
        await db.ClosePeriod(period.Reference, tenant, company, Guid.NewGuid().ToString());
        var later = await journal.CreateAsync(await Context(tenant, company, JournalPermissions.Create), request);
        Assert.Equal(FinanceErrors.PeriodClosed, (await Assert.ThrowsAsync<FoundationException>(() =>
            journal.PostAsync(Context(tenant, company, JournalPermissions.Post).GetAwaiter().GetResult(), later.Reference, 1,
                new("Finance.PostManualJournal", later.Reference.ToString(), "after-close",
                    RequestFingerprint.Create("POST", later.Reference.ToString(), "1", "{}"))))).Error);
        Assert.Equal(book.Reference, await db.Scalar<Guid>("SELECT id FROM finance.accounting_books WHERE tenant_id=@t AND company_id=@c", ("t", tenant), ("c", company)));
    }

    [Fact]
    public async Task Runtime_direct_insert_without_effects_is_rejected()
    {
        var tenant = Guid.NewGuid(); var company = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(db.Runtime); await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await Sql.ExecuteAsync(connection, "SELECT set_config('erp.tenant_id',@t,true),set_config('erp.company_id',@c,true)", default,
            ("t", tenant.ToString()), ("c", company.ToString()));
        await Sql.ExecuteAsync(connection, """
            INSERT INTO finance.accounting_books(tenant_id,company_id,id,code,name,currency,minor_units,status,
              created_at,created_by_issuer,created_by_subject)
            VALUES(@t,@c,gen_random_uuid(),'PRIMARY','Direct','JOD',3,'Active',transaction_timestamp(),'tests','direct')
            """, default, ("t", tenant), ("c", company));
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync())).SqlState);
    }

    [Fact]
    public async Task HTTP_create_requires_independent_permission_and_scope()
    {
        await FinanceScenario.Create(db, company: DatabaseFixture.CompanyA);
        await using var factory = new ApiFactory(db);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token);
        var path = $"/api/v1/tenants/{DatabaseFixture.TenantA}/companies/{DatabaseFixture.CompanyA}/finance/books";
        async Task<HttpResponseMessage> Send(string key, string code, string currency = "JOD")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            { Content = JsonContent.Create(new CreateAccountingBookRequest(code, "Book", currency)) };
            if (key.Length > 0) request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }
        foreach (var permission in new[] { BookPermissions.View, AccountPermissions.Create, JournalPermissions.Post })
        {
            factory.Members.Permissions.Clear(); factory.Members.Permissions.Add(permission);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send("http", "PRIMARY")).StatusCode);
        }
        factory.Members.Permissions.Clear(); factory.Members.Permissions.Add(BookPermissions.Create);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send("", "PRIMARY")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send("invalid", "PRIMARY", "ZZZ")).StatusCode);
        // The shared test company may already have a legacy book; duplicate is the stable response.
        Assert.Equal(HttpStatusCode.Conflict, (await Send("http", "PRIMARY")).StatusCode);
        using var foreign = new HttpRequestMessage(HttpMethod.Post,
            path.Replace(DatabaseFixture.CompanyA.ToString(), DatabaseFixture.CompanyA2.ToString(), StringComparison.Ordinal))
        { Content = JsonContent.Create(new CreateAccountingBookRequest("PRIMARY", "Book", "JOD")) };
        foreign.Headers.Add("Idempotency-Key", "foreign");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreign)).StatusCode);
    }
}
