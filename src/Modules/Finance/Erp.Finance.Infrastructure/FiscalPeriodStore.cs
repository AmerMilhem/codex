using Erp.Application.Abstractions;
using Erp.Finance.Application;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Finance.Infrastructure;

public sealed class FiscalPeriodStore : IPeriodStore
{
    private static FinanceDbContext Open(IApplicationTransaction transaction) =>
        PostgresTransaction.Require(transaction).Enlist(c => new FinanceDbContext(FinanceDbContext.Options(c)));

    private static FiscalPeriodView View(FiscalPeriodRow row) => new(row.Id, row.TenantId, row.CompanyId,
        row.FiscalYear, row.StartsOn, row.EndsOn, row.Status.ToString(), row.Revision, row.ClosedAt,
        row.ClosedByIssuer, row.ClosedBySubject, row.CloseComment, row.CloseReconciliation,
        row.CreatedAt, row.CreatedByIssuer, row.CreatedBySubject);

    public async Task<Guid> CreateAsync(IApplicationTransaction transaction, DateOnly start, DateOnly end,
        int fiscalYear, DateTimeOffset now, PrincipalIdentity actor, CancellationToken token)
    {
        var tx = PostgresTransaction.Require(transaction);
        var context = tx.Context;
        // Exclusive book lock serializes every company-period insert, including the overlap trigger.
        await using (var lockCommand = Sql.Command(tx.Connection,
            "SELECT count(*) FROM finance.lock_book_for_period_create()"))
            if ((long)(await lockCommand.ExecuteScalarAsync(token))! != 1)
                throw new FoundationException(FinanceErrors.BookMissing);
        await using (var overlap = Sql.Command(tx.Connection, """
            SELECT starts_on,ends_on FROM finance.fiscal_periods
            WHERE tenant_id=@tenant AND company_id=@company AND starts_on<=@end AND ends_on>=@start
            ORDER BY starts_on,id LIMIT 1
            """, ("tenant", context.TenantId), ("company", context.CompanyId), ("start", start), ("end", end)))
        await using (var reader = await overlap.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token))
                throw new FoundationException(reader.GetFieldValue<DateOnly>(0) == start &&
                    reader.GetFieldValue<DateOnly>(1) == end ? PeriodSetupErrors.Duplicate : PeriodSetupErrors.Overlap);
        }
        var id = Guid.CreateVersion7();
        var db = Open(transaction);
        db.Periods.Add(new FiscalPeriodRow
        {
            Id = id,
            TenantId = context.TenantId,
            CompanyId = context.CompanyId,
            FiscalYear = fiscalYear,
            StartsOn = start,
            EndsOn = end,
            Status = FiscalPeriodStatus.Open,
            Revision = 1,
            CreatedAt = now,
            CreatedByIssuer = actor.Issuer,
            CreatedBySubject = actor.Subject
        });
        await db.SaveChangesAsync(token);
        return id;
    }

    public async Task<IReadOnlyList<FiscalPeriodView>> ListAsync(IApplicationTransaction transaction, CancellationToken token)
    {
        var rows = await Open(transaction).Periods.AsNoTracking().OrderByDescending(x => x.StartsOn)
            .ThenBy(x => x.Id).Take(500).ToArrayAsync(token);
        return rows.Select(View).ToArray();
    }

    public async Task<FiscalPeriodView> LoadAsync(IApplicationTransaction transaction, Guid id, bool lockRoot, CancellationToken token)
    {
        var db = Open(transaction);
        var row = lockRoot
            ? await db.Periods.FromSqlInterpolated($"SELECT * FROM finance.fiscal_periods WHERE id={id} FOR UPDATE").SingleOrDefaultAsync(token)
            : await db.Periods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        return row is null ? throw new FoundationException(Errors.RESOURCE_NOT_FOUND) : View(row);
    }

    public async Task<PeriodClosePreview> ValidateAsync(IApplicationTransaction transaction, FiscalPeriodView period, CancellationToken token)
    {
        var tx = PostgresTransaction.Require(transaction);
        var context = tx.Context;
        var accounts = new List<TrialBalanceAccount>();
        await using (var command = TrialBalanceReader.Command(tx.Connection, TrialBalanceSql.Accounts, context,
            new(period.StartDate, period.EndDate)))
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) accounts.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                new(reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                    reader.GetString(7), reader.GetString(8))));

        TrialBalanceLedgerControl control;
        await using (var command = TrialBalanceReader.Command(tx.Connection, TrialBalanceSql.Control, context,
            new(period.StartDate, period.EndDate)))
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            await reader.ReadAsync(token);
            control = new(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5)), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9), reader.GetInt64(10), reader.GetInt64(11), reader.GetInt64(12));
        }
        var totals = TrialBalanceArithmetic.Total(accounts);
        var reconciliation = TrialBalanceArithmetic.Reconcile(accounts, totals, control);
        var checks = reconciliation.Checks.Select(x => new PeriodCloseCheck(x.Code, x.Passed, x.Expected, x.Actual)).ToList();

        // Independent account movement scan follows the GL's posted-line source and reconciles each account,
        // including credit/debit gross amounts, with the actual TB projection. Both are read under this transaction.
        var gl = new Dictionary<Guid, (string Debit, string Credit)>();
        await using (var command = Sql.Command(tx.Connection, """
            SELECT l.account_id,trim_scale(sum(l.debit))::text,trim_scale(sum(l.credit))::text
            FROM finance.journal_lines l JOIN finance.journal_entries j
              ON (j.tenant_id,j.company_id,j.id)=(l.tenant_id,l.company_id,l.journal_id)
            WHERE j.tenant_id=@tenant AND j.company_id=@company AND j.status='Posted'
              AND j.posting_date BETWEEN @start AND @end GROUP BY l.account_id
            """, ("tenant", context.TenantId), ("company", context.CompanyId),
            ("start", period.StartDate), ("end", period.EndDate)))
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) gl.Add(reader.GetGuid(0), (reader.GetString(1), reader.GetString(2)));
        var glMismatch = accounts.Count(x =>
        {
            var actual = gl.GetValueOrDefault(x.AccountId, ("0", "0"));
            return TrialBalanceArithmetic.Units(x.Amounts.PeriodDebit) != TrialBalanceArithmetic.Units(actual.Item1)
                || TrialBalanceArithmetic.Units(x.Amounts.PeriodCredit) != TrialBalanceArithmetic.Units(actual.Item2);
        });
        glMismatch += gl.Keys.Count(id => accounts.All(x => x.AccountId != id));
        checks.Add(new("GL_TB_PERIOD_ACCOUNTS", glMismatch == 0, "0", glMismatch.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        await using (var command = Sql.Command(tx.Connection, """
            SELECT count(*) FROM finance.journal_entries j
            WHERE j.tenant_id=@tenant AND j.company_id=@company AND j.status='Posted'
              AND j.posting_date BETWEEN @start AND @end
              AND (j.fiscal_period_id<>@period OR j.posting_date NOT BETWEEN @start AND @end)
            """, ("tenant", context.TenantId), ("company", context.CompanyId),
            ("start", period.StartDate), ("end", period.EndDate), ("period", period.Id)))
        {
            var invalid = (long)(await command.ExecuteScalarAsync(token))!;
            checks.Add(new("POSTED_PERIOD_SCOPE", invalid == 0, "0", invalid.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return new(period, checks.All(x => x.Passed), totals.PeriodDebit, totals.PeriodCredit, checks);
    }

    public async Task CloseAsync(IApplicationTransaction transaction, FiscalPeriodView period, string? comment,
        PeriodClosePreview preview,
        DateTimeOffset now, PrincipalIdentity actor, CancellationToken token)
    {
        var db = Open(transaction);
        var row = await db.Periods.SingleAsync(x => x.Id == period.Id, token);
        if (row.Revision != period.Revision) throw new FoundationException(PeriodCloseErrors.ConcurrencyConflict);
        row.Status = FiscalPeriodStatus.Closed;
        row.Revision++;
        row.ClosedAt = now;
        row.ClosedByIssuer = actor.Issuer;
        row.ClosedBySubject = actor.Subject;
        row.CloseComment = comment;
        row.CloseReconciliation = System.Text.Json.JsonSerializer.Serialize(new
        {
            version = 1,
            passed = preview.Passed,
            periodDebit = preview.PeriodDebit,
            periodCredit = preview.PeriodCredit,
            checks = preview.Checks
        }, System.Text.Json.JsonSerializerOptions.Web);
        await db.SaveChangesAsync(token);
    }
}
