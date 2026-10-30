using Erp.Application.Abstractions;
using Erp.Finance.Application;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Finance.Infrastructure;

public sealed class JournalQueries(IUnitOfWorkExecutor uow, IAuditReader audit) : IJournalQueries
{
    private static FinanceDbContext Open(IApplicationTransaction tx) => PostgresTransaction.Require(tx).Enlist(c => new FinanceDbContext(FinanceDbContext.Options(c)));
    private static JournalView View(JournalEntry x, IReadOnlyDictionary<Guid, AccountRow> accounts) => new(x.Id, x.Status.ToString(), x.Revision, x.PostingDate,
        x.Currency, x.Reference, x.Description, x.JournalNumber, x.PostedByIssuer, x.PostedBySubject, x.PostedAt, JournalMoney.Format(x.TotalDebit),
        JournalMoney.Format(x.TotalCredit), JournalMoney.Format(x.TotalDebit - x.TotalCredit), x.RoundingPolicy,
        x.Lines.OrderBy(l => l.Position).Select(l => new JournalLineView(l.Id, l.Position, l.AccountId, accounts[l.AccountId].Code, accounts[l.AccountId].Name,
            JournalMoney.Format(l.Debit), JournalMoney.Format(l.Credit), l.Description)).ToArray());
    private static async Task<JournalReversalView?> ReversalView(FinanceDbContext db, Guid id, CancellationToken ct)
    {
        return await (from r in db.Reversals.AsNoTracking()
                      join o in db.Journals on r.OriginalJournalId equals o.Id
                      join v in db.Journals on r.ReversalJournalId equals v.Id
                      where r.OriginalJournalId == id || r.ReversalJournalId == id
                      select new JournalReversalView(r.OriginalJournalId, o.JournalNumber!, r.ReversalJournalId, v.JournalNumber!, r.PostingDate, r.Reason, r.ActorIssuer, r.ActorSubject, r.Timestamp, r.RequestId, r.CorrelationId)).SingleOrDefaultAsync(ct);
    }
    public Task<JournalView> GetAsync(VerifiedContext context, Guid id, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, JournalPermissions.View);
        return uow.ExecuteAsync(context, async (tx, ct) =>
        {
            var db = Open(tx); var journal = await db.Journals.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new FoundationException(Errors.RESOURCE_NOT_FOUND);
            var ids = journal.Lines.Select(x => x.AccountId).ToArray();
            var accounts = await db.Accounts.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var correction = await db.Corrections.AsNoTracking().Where(x => x.OriginalJournalId == id || x.ReversalJournalId == id || x.ReplacementJournalId == id)
                .Select(x => new JournalCorrectionView(x.Id, x.OriginalJournalId, x.ReversalJournalId, x.ReplacementJournalId, x.PostingDate, x.Reason, x.ActorIssuer, x.ActorSubject, x.Timestamp, x.RequestId, x.CorrelationId)).SingleOrDefaultAsync(ct);
            return View(journal, accounts) with { Reversal = await ReversalView(db, id, ct), Correction = correction };
        }, token);
    }
    public Task<JournalPage> ListAsync(VerifiedContext context, int offset, int limit, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, JournalPermissions.View);
        if (offset is < 0 or > 100000 || limit is < 1 or > 100) throw new FoundationException(Errors.VALIDATION_FAILED);
        return uow.ExecuteAsync(context, async (tx, ct) =>
        {
            var db = Open(tx); var journals = await db.Journals.AsNoTracking().Include(x => x.Lines).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Skip(offset).Take(limit + 1).ToArrayAsync(ct);
            var ids = journals.SelectMany(x => x.Lines).Select(x => x.AccountId).Distinct().ToArray();
            var accounts = await db.Accounts.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            return new JournalPage(journals.Take(limit).Select(x => View(x, accounts)).ToArray(), offset, limit, journals.Length > limit);
        }, token);
    }
    public Task<JournalSetup> SetupAsync(VerifiedContext context, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, JournalPermissions.View);
        return uow.ExecuteAsync(context, async (tx, ct) =>
        {
            var db = Open(tx); var book = await db.Books.AsNoTracking().SingleOrDefaultAsync(ct) ?? throw new FoundationException(FinanceErrors.BookMissing);
            var accounts = await db.Accounts.AsNoTracking().OrderBy(x => x.Code).Select(x => new AccountView(x.Id, x.Code, x.Name, x.Status.ToString(), x.PostingAllowed)).Take(1000).ToArrayAsync(ct);
            return new JournalSetup(book.Currency, book.MinorUnits, accounts);
        }, token);
    }
    public Task<IReadOnlyList<JournalAuditView>> AuditAsync(VerifiedContext context, Guid id, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, JournalPermissions.View);
        return uow.ExecuteAsync<IReadOnlyList<JournalAuditView>>(context, async (tx, ct) =>
        {
            var db = Open(tx);
            if (!await db.Journals.AnyAsync(x => x.Id == id, ct)) throw new FoundationException(Errors.RESOURCE_NOT_FOUND);
            var records = await audit.ReadAsync(tx, "ManualJournal", id.ToString(), ct);
            var reversal = await ReversalView(db, id, ct);
            return records.Select(x => new JournalAuditView(x.Action, x.ActorIssuer, x.ActorSubject, x.Timestamp, x.TenantId, x.CompanyId, x.RequestId, x.CorrelationId, x.Action is "JournalReversalRequested" or "JournalReversed" ? reversal : null)).ToArray();
        }, token);
    }
    public Task<JournalReconciliation> ReconcileAsync(VerifiedContext context, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, JournalPermissions.View);
        return uow.ExecuteAsync(context, async (tx, ct) =>
        {
            // One SQL statement gives one PostgreSQL statement snapshot across all posted journals in scope.
            await using var command = Sql.Command(PostgresTransaction.Require(tx).Connection, """
                SELECT j.id,coalesce(sum(l.debit),0),coalesce(sum(l.credit),0),count(l.id)
                FROM finance.journal_entries j LEFT JOIN finance.journal_lines l
                  ON (l.tenant_id,l.company_id,l.journal_id)=(j.tenant_id,j.company_id,j.id)
                WHERE j.status='Posted' GROUP BY j.id ORDER BY j.id
                """);
            await using var reader = await command.ExecuteReaderAsync(ct);
            var failures = new List<ReconciliationFailure>(); decimal debit = 0, credit = 0; long count = 0;
            while (await reader.ReadAsync(ct))
            {
                var d = reader.GetDecimal(1); var c = reader.GetDecimal(2); var lines = checked((int)reader.GetInt64(3));
                debit += d; credit += c; count++;
                if (d != c || lines < 2) failures.Add(new(reader.GetGuid(0), JournalMoney.Format(d), JournalMoney.Format(c), lines));
            }
            return new JournalReconciliation(failures.Count == 0 && debit == credit, count, JournalMoney.Format(debit), JournalMoney.Format(credit), failures);
        }, token);
    }
}
