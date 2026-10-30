using Erp.Application.Abstractions;
using Erp.Finance.Application;
using Erp.Finance.Domain;
using Erp.Kernel;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Finance.Infrastructure;

public sealed class JournalSessions : IJournalSessions
{
    public IJournalSession Open(IApplicationTransaction transaction) => new JournalSession(PostgresTransaction.Require(transaction));
}
internal sealed class JournalSession : IJournalSession
{
    private readonly PostgresTransaction tx;
    private readonly FinanceDbContext db;
    public JournalSession(PostgresTransaction tx)
    { this.tx = tx; db = tx.Enlist(c => new FinanceDbContext(FinanceDbContext.Options(c))); }
    public async Task<JournalEntry> LoadAsync(Guid id, bool lockRoot, CancellationToken token)
    {
        var query = lockRoot ? db.Journals.FromSqlInterpolated($"SELECT * FROM finance.journal_entries WHERE id={id} FOR UPDATE") : db.Journals.Where(x => x.Id == id);
        var journal = await query.SingleOrDefaultAsync(token) ?? throw new FoundationException(Errors.RESOURCE_NOT_FOUND);
        await db.Entry(journal).Collection(x => x.Lines).LoadAsync(token);
        return journal;
    }
    public async Task<BookPolicy> BookAsync(bool lockPolicy, CancellationToken token)
    {
        var books = lockPolicy ? db.Books.FromSqlRaw("SELECT * FROM finance.lock_book()") : db.Books.AsQueryable();
        var book = await books.AsNoTracking().SingleOrDefaultAsync(token) ?? throw new FoundationException(FinanceErrors.BookMissing);
        if (book.Status != AccountingBookStatus.Active) throw new FoundationException(FinanceErrors.BookMissing);
        return new(book.Currency, book.MinorUnits);
    }
    public async Task<IReadOnlyList<PeriodSnapshot>> PeriodsAsync(DateOnly? date, bool lockPeriods, CancellationToken token)
    {
        if (date is null) return [];
        var query = lockPeriods ? db.Periods.FromSqlInterpolated($"SELECT * FROM finance.lock_periods({date.Value})")
            : db.Periods.Where(x => x.StartsOn <= date && x.EndsOn >= date);
        var rows = await query.AsNoTracking().ToArrayAsync(token);
        return rows.Select(x => new PeriodSnapshot(x.Id, x.FiscalYear, x.Status, x.StartsOn, x.EndsOn)).ToArray();
    }
    public async Task<IReadOnlyList<AccountSnapshot>> AccountsAsync(IEnumerable<Guid> ids, bool lockAccounts, CancellationToken token)
    {
        var keys = ids.Distinct().Order().ToArray();
        var query = lockAccounts ? db.Accounts.FromSqlInterpolated($"SELECT * FROM finance.lock_accounts({keys})") : db.Accounts.Where(x => keys.Contains(x.Id));
        var rows = await query.AsNoTracking().ToArrayAsync(token);
        return rows.Select(x => new AccountSnapshot(x.Id, x.Status, x.PostingAllowed)).ToArray();
    }
    public Task<JournalReversal?> ReversalAsync(Guid id, CancellationToken token) => db.Reversals.AsNoTracking().SingleOrDefaultAsync(x => x.OriginalJournalId == id || x.ReversalJournalId == id, token);
    public Task<JournalCorrection?> CorrectionAsync(Guid id, CancellationToken token) => db.Corrections.AsNoTracking().SingleOrDefaultAsync(x => x.OriginalJournalId == id || x.ReversalJournalId == id || x.ReplacementJournalId == id, token);
    public void AddCorrection(JournalCorrection correction) => db.Corrections.Add(correction);
    public void AddReversal(JournalReversal reversal) => db.Reversals.Add(reversal);
    public void Add(JournalEntry journal) => db.Journals.Add(journal);
    public async Task<long> AllocateNumberAsync(int fiscalYear, CancellationToken token)
    {
        await using var command = Sql.Command(tx.Connection, """
            INSERT INTO finance.journal_numbers(tenant_id,company_id,fiscal_year,last_number) VALUES(@tenant,@company,@year,1)
            ON CONFLICT(tenant_id,company_id,fiscal_year) DO UPDATE SET last_number=finance.journal_numbers.last_number+1 RETURNING last_number
            """, ("tenant", tx.Context.TenantId), ("company", tx.Context.CompanyId), ("year", fiscalYear));
        return (long)(await command.ExecuteScalarAsync(token))!;
    }
    public Task FlushAsync(CancellationToken token) => db.SaveChangesAsync(token);
}
