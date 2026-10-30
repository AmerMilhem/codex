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
public sealed class AccountSetupTests(DatabaseFixture db)
{
    private CreateAccountCommand Command => new(db.Commands, new AccountSetupStore(), db.Audit, db.Outbox, TimeProvider.System);
    private static CommandIdentity Identity(CreateAccountRequest request, string key) => new(
        "Finance.CreateAccount", "accounts", key,
        RequestFingerprint.Create("POST", "accounts", "0", JsonSerializer.Serialize(request)));
    private static async Task<VerifiedContext> Context(Guid tenant, Guid company, string permission) =>
        await new ContextResolver(new Membership(tenant, company)).ResolveAsync(new("tests", "account-creator"),
            tenant, company, permission, "account-setup-tests", Guid.NewGuid().ToString("N"), ExecutionSource.Test);
    private sealed class Membership(Guid tenant, Guid company) : IMembershipStore
    {
        public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken token) =>
            ValueTask.FromResult<PrincipalMembership?>(new(true, [new(tenant, company,
                new HashSet<string> { AccountPermissions.Create, AccountPermissions.View, JournalPermissions.Post,
                    PeriodPermissions.Create, PeriodPermissions.Close })]));
    }
    private async Task<CommandReceipt> Create(Guid tenant, Guid company, string code, string name,
        bool postable = true, string? key = null, IIdempotentCommands? executor = null)
    {
        var request = new CreateAccountRequest(code, name, postable);
        var command = new CreateAccountCommand(executor ?? db.Commands, new AccountSetupStore(), db.Audit, db.Outbox, TimeProvider.System);
        return await command.ExecuteAsync(await Context(tenant, company, AccountPermissions.Create), request,
            Identity(request, key ?? Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public async Task Created_accounts_are_active_and_postable_choices_are_persisted_with_audit_and_outbox()
    {
        var s = await TrialBalanceScenario.Create(db);
        var expense = await Create(s.Tenant, s.Company, " 6a00 ", " Office Expense ");
        var header = await Create(s.Tenant, s.Company, "1-000", "Assets Header", false);
        var rows = await new AccountSetupQueries(db.Uow, new AccountSetupStore())
            .ListAsync(await Context(s.Tenant, s.Company, AccountPermissions.View));
        Assert.Contains(rows, x => x.Id == expense.Reference && x.Code == "6A00" && x.Name == "Office Expense"
            && x.Status == "Active" && x.PostingAllowed && x.CreatedAt is not null && x.CreatedBySubject == "account-creator");
        Assert.Contains(rows, x => x.Id == header.Reference && !x.PostingAllowed);
        foreach (var id in new[] { expense.Reference, header.Reference })
        {
            Assert.Equal(2L, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND entity_type='Account' AND action IN ('AccountCreateRequested','AccountCreated')", ("id", id.ToString())));
            Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.account-created.v1'", ("id", id.ToString())));
        }
    }

    [Fact]
    public async Task Invalid_code_name_and_case_insensitive_duplicate_are_rejected()
    {
        var s = await TrialBalanceScenario.Create(db);
        foreach (var code in new[] { "", "  ", "A B", "A/1", new string('A', 33) })
            Assert.Equal(AccountSetupErrors.InvalidCode, (await Assert.ThrowsAsync<FoundationException>(() =>
                Create(s.Tenant, s.Company, code, "Valid"))).Error);
        foreach (var name in new[] { "", "   ", new string('N', 201) })
            Assert.Equal(AccountSetupErrors.InvalidName, (await Assert.ThrowsAsync<FoundationException>(() =>
                Create(s.Tenant, s.Company, "6200", name))).Error);
        await Create(s.Tenant, s.Company, "abc.1", "First");
        Assert.Equal(AccountSetupErrors.Duplicate, (await Assert.ThrowsAsync<FoundationException>(() =>
            Create(s.Tenant, s.Company, "  ABC.1  ", "Second"))).Error);
        await db.Execute("INSERT INTO finance.accounts(id,tenant_id,company_id,code,name,status,posting_allowed) VALUES(gen_random_uuid(),@t,@c,'legacy-x','Legacy','Active',true)",
            ("t", s.Tenant), ("c", s.Company));
        Assert.Equal(AccountSetupErrors.Duplicate, (await Assert.ThrowsAsync<FoundationException>(() =>
            Create(s.Tenant, s.Company, "LEGACY-X", "Second"))).Error);
    }

    [Fact]
    public async Task Concurrent_duplicate_and_identical_retry_create_one_account()
    {
        var s = await TrialBalanceScenario.Create(db);
        var same = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Create(s.Tenant, s.Company, "6100", "Expense", key: "same")));
        Assert.Single(same.Select(x => x.Reference).Distinct());
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            try { await Create(s.Tenant, s.Company, "1100", "Cash", key: "race-" + i); return true; }
            catch (FoundationException error) { Assert.Equal(AccountSetupErrors.Duplicate, error.Error); return false; }
        }));
        Assert.Single(results, x => x);
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM finance.accounts WHERE tenant_id=@t AND company_id=@c AND code='1100'", ("t", s.Tenant), ("c", s.Company)));
    }

    [Fact]
    public async Task Lost_response_and_changed_payload_are_idempotent()
    {
        var s = await TrialBalanceScenario.Create(db);
        async Task Lose() { await Create(s.Tenant, s.Company, "6300", "Travel", key: "lost"); throw new TimeoutException(); }
        await Assert.ThrowsAsync<TimeoutException>(Lose);
        var before = await s.StateDigest();
        var retried = await Create(s.Tenant, s.Company, "6300", "Travel", key: "lost");
        Assert.Equal(before, await s.StateDigest());
        foreach (var (code, name) in new[] { ("6301", "Travel"), ("6300", "Other") })
            Assert.Equal(Errors.API_IDEMPOTENCY_CONFLICT, (await Assert.ThrowsAsync<FoundationException>(() =>
                Create(s.Tenant, s.Company, code, name, key: "lost"))).Error);
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE source_id=@id AND type='finance.account-created.v1'", ("id", retried.Reference.ToString())));
    }

    [Fact]
    public async Task Permission_tenant_company_and_pooled_RLS_are_isolated()
    {
        var a = await TrialBalanceScenario.Create(db);
        var b = await TrialBalanceScenario.Create(db, tenant: a.Tenant);
        var c = await TrialBalanceScenario.Create(db);
        var request = new CreateAccountRequest("6100", "Expense", true);
        foreach (var permission in new[] { AccountPermissions.View, JournalPermissions.Create,
            JournalPermissions.Post, PeriodPermissions.Create, PeriodPermissions.Close })
            Assert.Equal(Errors.AUTH_SCOPE_DENIED, (await Assert.ThrowsAsync<FoundationException>(() =>
                Command.ExecuteAsync(Context(a.Tenant, a.Company, permission).GetAwaiter().GetResult(), request,
                    Identity(request, permission)))).Error);
        var first = await Create(a.Tenant, a.Company, "6100", "Expense");
        foreach (var other in new[] { b, c })
        {
            var own = await Create(other.Tenant, other.Company, "6100", "Expense");
            var visible = await new AccountSetupQueries(db.Uow, new AccountSetupStore())
                .ListAsync(await Context(other.Tenant, other.Company, AccountPermissions.View));
            Assert.Contains(visible, x => x.Id == own.Reference);
            Assert.DoesNotContain(visible, x => x.Id == first.Reference);
        }
        Assert.True(await db.Scalar<bool>("SELECT relforcerowsecurity FROM pg_class WHERE oid='finance.accounts'::regclass"));
    }

    [Fact]
    public async Task Failed_transaction_rolls_back_account_audit_outbox_and_receipt()
    {
        var s = await TrialBalanceScenario.Create(db);
        var failed = new FailAfterWork(db.Commands);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(s.Tenant, s.Company, "6400", "Supplies", key: "rollback", executor: failed));
        Assert.Equal(0L, await db.Scalar<long>("SELECT count(*) FROM finance.accounts WHERE tenant_id=@t AND company_id=@c AND code='6400'", ("t", s.Tenant), ("c", s.Company)));
        Assert.Equal(0L, await db.Scalar<long>("SELECT count(*) FROM integration.outbox WHERE tenant_id=@t AND company_id=@c AND type='finance.account-created.v1'", ("t", s.Tenant), ("c", s.Company)));
        await Create(s.Tenant, s.Company, "6400", "Supplies", key: "rollback");
    }
    private sealed class FailAfterWork(IIdempotentCommands inner) : IIdempotentCommands
    {
        public Task<CommandReceipt> ExecuteAsync(VerifiedContext context, CommandIdentity identity,
            Func<IApplicationTransaction, CancellationToken, Task<CommandReceipt>> work, CancellationToken token = default) =>
            inner.ExecuteAsync(context, identity, async (tx, ct) => { await work(tx, ct); throw new InvalidOperationException("injected"); }, token);
    }

    [Fact]
    public async Task Newly_created_accounts_post_and_match_trial_balance_and_GL_nonpostable_rejected()
    {
        var s = await TrialBalanceScenario.Create(db);
        await db.CreatePeriod(s.Tenant, s.Company, new(2027, 1, 1), new(2027, 1, 31), Guid.NewGuid().ToString());
        var expense = await Create(s.Tenant, s.Company, "6100", "Office Expense");
        var cash = await Create(s.Tenant, s.Company, "1100", "Cash");
        var header = await Create(s.Tenant, s.Company, "1000H", "Assets Header", false);
        var date = new DateOnly(2027, 1, 2);
        var posted = await s.Post(expense.Reference, cash.Reference, "100", date);
        var report = await s.Report(date, date);
        Assert.True(report.Reconciliation.Passed);
        Assert.Equal("100", s.Account(report, expense.Reference).PeriodDebit);
        Assert.Equal("100", s.Account(report, cash.Reference).PeriodCredit);
        var reader = new GeneralLedgerReader(db.Source, NullLogger<GeneralLedgerReader>.Instance);
        var query = new GeneralLedgerQuery(reader);
        foreach (var account in new[] { expense.Reference, cash.Reference })
        {
            var gl = await query.RunAsync(await s.Context(), account, date, date);
            Assert.True(gl.Reconciliation.Passed);
            Assert.Single(gl.Movements);
            Assert.Equal(posted.Reference, gl.Movements[0].JournalId);
        }
        var journal = await new JournalQueries(db.Uow, new AuditReader()).GetAsync(await s.Context(), posted.Reference);
        Assert.Equal(new[] { "6100", "1100" }, journal.Lines.Select(x => x.AccountCode));
        var draft = await s.Draft(header.Reference, cash.Reference, "10", date);
        var journals = new JournalCommands(db.Uow, db.Commands, new JournalSessions(), db.Audit, db.Outbox, TimeProvider.System);
        Assert.Equal(FinanceErrors.NonPostableAccount, (await Assert.ThrowsAsync<FoundationException>(() =>
            journals.PostAsync(s.Context(JournalPermissions.Post).GetAwaiter().GetResult(), draft.Reference, 1,
                new("Finance.PostManualJournal", draft.Reference.ToString(), "nonpostable",
                    RequestFingerprint.Create("POST", draft.Reference.ToString(), "1", "{}"))))).Error);
    }

    [Fact]
    public async Task Runtime_direct_insert_without_command_effects_is_rejected()
    {
        var s = await TrialBalanceScenario.Create(db);
        await using var connection = new NpgsqlConnection(db.Runtime); await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await Sql.ExecuteAsync(connection, "SELECT set_config('erp.tenant_id',@t,true),set_config('erp.company_id',@c,true)", default,
            ("t", s.Tenant.ToString()), ("c", s.Company.ToString()));
        await Sql.ExecuteAsync(connection, "INSERT INTO finance.accounts(id,tenant_id,company_id,code,name,status,posting_allowed,created_at,created_by_issuer,created_by_subject) VALUES(gen_random_uuid(),@t,@c,'6500','Direct','Active',true,transaction_timestamp(),'tests','direct')", default,
            ("t", s.Tenant), ("c", s.Company));
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync())).SqlState);
    }

    [Fact]
    public async Task Created_event_is_inbox_deduplicated_without_financial_mutation()
    {
        var s = await TrialBalanceScenario.Create(db);
        var created = await Create(s.Tenant, s.Company, "6600", "Service");
        var payload = await db.Scalar<string>("SELECT payload::text FROM integration.outbox WHERE source_id=@id AND type='finance.account-created.v1'", ("id", created.Reference.ToString()));
        var eventId = await db.Scalar<Guid>("SELECT id FROM integration.outbox WHERE source_id=@id AND type='finance.account-created.v1'", ("id", created.Reference.ToString()));
        var message = new IntegrationEvent(eventId, AccountCreatedV1.EventType, created.Reference.ToString(), payload, DateTimeOffset.UtcNow);
        var context = await new ContextResolver(new WorkerMembership(s.Tenant, s.Company)).ResolveAsync(
            new("tests", "account-worker"), s.Tenant, s.Company, "outbox.dispatch", "account-delivery",
            "account-worker-request", ExecutionSource.Worker);
        var handler = new AccountCreatedTraceHandler(new AccountSetupStore(), db.Audit);
        var before = await db.Scalar<string>("SELECT to_jsonb(a)::text FROM finance.accounts a WHERE id=@id", ("id", created.Reference));
        for (var attempt = 0; attempt < 2; attempt++)
            await db.Inbox.ProcessAsync(context, handler.Consumer, message, (tx, ct) => handler.HandleAsync(tx, message, ct));
        Assert.Equal(1L, await db.Scalar<long>("SELECT count(*) FROM audit.entries WHERE entity_id=@id AND action='AccountCreatedEventDelivered'", ("id", created.Reference.ToString())));
        Assert.Equal(before, await db.Scalar<string>("SELECT to_jsonb(a)::text FROM finance.accounts a WHERE id=@id", ("id", created.Reference)));
    }
    private sealed class WorkerMembership(Guid tenant, Guid company) : IMembershipStore
    {
        public ValueTask<PrincipalMembership?> FindAsync(PrincipalIdentity identity, CancellationToken token) =>
            ValueTask.FromResult<PrincipalMembership?>(new(true,
                [new(tenant, company, new HashSet<string> { "outbox.dispatch" })]));
    }

    [Fact]
    public async Task HTTP_contract_requires_permission_scope_and_idempotency()
    {
        var s = await FinanceScenario.Create(db);
        await using var factory = new ApiFactory(db);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token);
        var path = $"/api/v1/tenants/{s.Tenant}/companies/{s.Company}/finance/accounts";
        async Task<HttpResponseMessage> Send(string key, string code, string name)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            { Content = JsonContent.Create(new CreateAccountRequest(code, name, true)) };
            if (key.Length > 0) request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }
        foreach (var permission in new[] { AccountPermissions.View, JournalPermissions.Post, PeriodPermissions.Create })
        {
            factory.Members.Permissions.Clear(); factory.Members.Permissions.Add(permission);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send("http", "AC-100", "Cash")).StatusCode);
        }
        factory.Members.Permissions.Clear(); factory.Members.Permissions.Add(AccountPermissions.Create);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send("", "AC-100", "Cash")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send("invalid", "A B", "Bad")).StatusCode);
        var first = await Send("http", "AC-100", "Cash"); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var retry = await Send("http", "AC-100", "Cash"); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(await first.Content.ReadFromJsonAsync<CommandReceipt>(), await retry.Content.ReadFromJsonAsync<CommandReceipt>());
        Assert.Equal(HttpStatusCode.Conflict, (await Send("http", "AC-101", "Cash")).StatusCode);
        using var foreign = new HttpRequestMessage(HttpMethod.Post,
            path.Replace(s.Company.ToString(), DatabaseFixture.CompanyA2.ToString(), StringComparison.Ordinal))
        { Content = JsonContent.Create(new CreateAccountRequest("AC-102", "Cash", true)) };
        foreign.Headers.Add("Idempotency-Key", "foreign");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreign)).StatusCode);
    }
}
