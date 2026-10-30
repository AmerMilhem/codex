using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;

namespace Erp.Finance.Application;

public interface IJournalSession
{
    Task<JournalEntry> LoadAsync(Guid id, bool lockRoot, CancellationToken token);
    Task<BookPolicy> BookAsync(bool lockPolicy, CancellationToken token);
    Task<IReadOnlyList<PeriodSnapshot>> PeriodsAsync(DateOnly? date, bool lockPeriods, CancellationToken token);
    Task<IReadOnlyList<AccountSnapshot>> AccountsAsync(IEnumerable<Guid> ids, bool lockAccounts, CancellationToken token);
    Task<JournalReversal?> ReversalAsync(Guid id, CancellationToken token);
    Task<JournalCorrection?> CorrectionAsync(Guid id, CancellationToken token) => Task.FromResult<JournalCorrection?>(null);
    void AddCorrection(JournalCorrection correction) => throw new NotSupportedException();
    void AddReversal(JournalReversal reversal);
    void Add(JournalEntry journal);
    Task<long> AllocateNumberAsync(int fiscalYear, CancellationToken token);
    Task FlushAsync(CancellationToken token);
}
public interface IJournalSessions { IJournalSession Open(IApplicationTransaction transaction); }
public interface IJournalQueries
{
    Task<JournalView> GetAsync(VerifiedContext context, Guid id, CancellationToken token = default);
    Task<JournalPage> ListAsync(VerifiedContext context, int offset, int limit, CancellationToken token = default);
    Task<JournalSetup> SetupAsync(VerifiedContext context, CancellationToken token = default);
    Task<IReadOnlyList<JournalAuditView>> AuditAsync(VerifiedContext context, Guid id, CancellationToken token = default);
    Task<JournalReconciliation> ReconcileAsync(VerifiedContext context, CancellationToken token = default);
}
public static class FinanceAuthorization
{
    public static void Require(VerifiedContext context, string permission)
    { if (context.Action != permission) throw new FoundationException(Errors.AUTH_SCOPE_DENIED); }
}
public sealed class JournalCommands(IUnitOfWorkExecutor uow, IIdempotentCommands idempotency, IJournalSessions sessions,
    IAuditWriter audit, IOutboxWriter outbox, TimeProvider clock)
{
    private static LineInput[] Inputs(SaveDraftRequest request)
    {
        if (request is null || request.Lines is null || request.Lines.Length > 200 || request.Reference is null || request.Description is null || request.Currency is null
            || request.Lines.Any(x => x is null || x.Description is null)) throw new FoundationException(Errors.VALIDATION_FAILED);
        return request.Lines.Select(x => new LineInput(x.Id, x.AccountId, JournalMoney.Parse(x.Debit), JournalMoney.Parse(x.Credit), x.Description)).ToArray();
    }
    public Task<CommandReceipt> CreateAsync(VerifiedContext context, SaveDraftRequest request, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, JournalPermissions.Create); var inputs = Inputs(request);
        return uow.ExecuteAsync(context, async (tx, ct) =>
        {
            var session = sessions.Open(tx); var book = await session.BookAsync(true, ct);
            if (request.Currency != book.Currency) throw new FoundationException(FinanceErrors.CurrencyInvalid);
            await RequireDraftAccounts(session, inputs, ct);
            var journal = JournalEntry.Create(context.TenantId, context.CompanyId, request.PostingDate, request.Currency, request.Reference, request.Description, inputs, clock.GetUtcNow());
            session.Add(journal); audit.Append(tx, new("JournalCreated", "ManualJournal", journal.Id.ToString()));
            return new CommandReceipt(journal.Id, journal.Revision);
        }, token);
    }
    public Task<CommandReceipt> UpdateAsync(VerifiedContext context, Guid id, long expected, SaveDraftRequest request, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, JournalPermissions.Edit); var inputs = Inputs(request);
        return uow.ExecuteAsync(context, async (tx, ct) =>
        {
            var session = sessions.Open(tx); var book = await session.BookAsync(true, ct);
            var journal = await session.LoadAsync(id, true, ct); journal.RequireDraft(); journal.ExpectRevision(expected);
            if (request.Currency != book.Currency) throw new FoundationException(FinanceErrors.CurrencyInvalid);
            await RequireDraftAccounts(session, inputs, ct);
            journal.Edit(expected, request.PostingDate, request.Currency, request.Reference, request.Description, inputs);
            audit.Append(tx, new("JournalUpdated", "ManualJournal", id.ToString()));
            return new CommandReceipt(id, journal.Revision);
        }, token);
    }
    public Task<JournalValidation> ValidateAsync(VerifiedContext context, Guid id, long expected, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, JournalPermissions.Edit);
        return uow.ExecuteAsync(context, async (tx, ct) =>
        {
            var session = sessions.Open(tx); var book = await session.BookAsync(true, ct);
            var journal = await session.LoadAsync(id, true, ct); journal.RequireDraft(); journal.ExpectRevision(expected);
            var periods = await session.PeriodsAsync(journal.PostingDate, true, ct);
            var accounts = await session.AccountsAsync(journal.Lines.Select(x => x.AccountId), true, ct);
            var issues = journal.Validate(book, periods, accounts);
            audit.Append(tx, new("JournalValidated", "ManualJournal", id.ToString()));
            return new JournalValidation(journal.Revision, issues.Count == 0, issues.Select(x => new ValidationIssue(x.Error.Id, x.Error.Code, x.Field, x.LineId)).ToArray());
        }, token);
    }
    public Task<CommandReceipt> PostAsync(VerifiedContext context, Guid id, long expected, CommandIdentity identity, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, JournalPermissions.Post);
        if (identity.Command != "Finance.PostManualJournal" || identity.Target != id.ToString() || expected < 1) throw new FoundationException(Errors.VALIDATION_FAILED);
        return idempotency.ExecuteAsync(context, identity, async (tx, ct) =>
        {
            var session = sessions.Open(tx); var book = await session.BookAsync(true, ct);
            // The root serializes edit/post. Shared reference locks protect period closure and account changes through COMMIT.
            var journal = await session.LoadAsync(id, true, ct); journal.RequireDraft(); journal.ExpectRevision(expected);
            var periods = await session.PeriodsAsync(journal.PostingDate, true, ct);
            var accounts = await session.AccountsAsync(journal.Lines.Select(x => x.AccountId), true, ct);
            var issues = journal.Validate(book, periods, accounts);
            if (issues.Count > 0) throw new FoundationException(issues[0].Error);
            var number = await session.AllocateNumberAsync(periods[0].FiscalYear, ct);
            var now = clock.GetUtcNow(); journal.Post(expected, book, periods, accounts, number, context.Identity.Issuer, context.Identity.Subject, now);
            audit.Append(tx, new("JournalPosted", "ManualJournal", id.ToString()));
            var message = new JournalEntryPostedV1(id, journal.JournalNumber!, journal.PostingDate!.Value, journal.Currency);
            outbox.Append(tx, new(Guid.CreateVersion7(), JournalEntryPostedV1.EventType, id.ToString(), JsonSerializer.Serialize(message, JsonSerializerOptions.Web), now));
            // Force financial changes inside the shared transaction; failures after this point must still roll back everything.
            await session.FlushAsync(ct);
            return new CommandReceipt(id, journal.Revision);
        }, token);
    }
    private static async Task RequireDraftAccounts(IJournalSession session, IReadOnlyList<LineInput> lines, CancellationToken token)
    {
        var accounts = await session.AccountsAsync(lines.Select(x => x.AccountId), false, token);
        if (lines.Any(x => !accounts.Any(a => a.Id == x.AccountId))) throw new FoundationException(FinanceErrors.AccountMissing);
    }
}
