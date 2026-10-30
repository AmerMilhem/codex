using Erp.Application.Abstractions;
using Erp.Finance.Application;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Finance.Infrastructure;

public sealed class AccountingBookSetupStore : IAccountingBookSetupStore
{
    private static FinanceDbContext Open(IApplicationTransaction transaction) =>
        PostgresTransaction.Require(transaction).Enlist(c => new FinanceDbContext(FinanceDbContext.Options(c)));
    private static AccountingBookView View(AccountingBookRow row) => new(row.Id, row.TenantId, row.CompanyId,
        row.Code, row.Name, row.Currency, row.MinorUnits, row.Status.ToString(), row.CreatedAt,
        row.CreatedByIssuer, row.CreatedBySubject);

    public async Task<Guid> CreateAsync(IApplicationTransaction transaction, string code, string name, string currency,
        int minorUnits, DateTimeOffset now, PrincipalIdentity actor, CancellationToken token)
    {
        var tx = PostgresTransaction.Require(transaction);
        // There is no row to lock yet. A transaction-scoped advisory lock serializes this
        // company; the existing (tenant,company) PK is the independent database backstop.
        await using (var command = Sql.Command(tx.Connection, "SELECT finance.lock_company_for_book_create()"))
            await command.ExecuteNonQueryAsync(token);
        if (await Open(transaction).Books.AsNoTracking().AnyAsync(token))
            throw new FoundationException(BookSetupErrors.Duplicate);
        var id = Guid.CreateVersion7();
        var db = Open(transaction);
        db.Books.Add(new AccountingBookRow
        {
            Id = id,
            TenantId = tx.Context.TenantId,
            CompanyId = tx.Context.CompanyId,
            Code = code,
            Name = name,
            Currency = currency,
            MinorUnits = minorUnits,
            Status = AccountingBookStatus.Active,
            CreatedAt = now,
            CreatedByIssuer = actor.Issuer,
            CreatedBySubject = actor.Subject
        });
        await db.SaveChangesAsync(token);
        return id;
    }

    public async Task<IReadOnlyList<AccountingBookView>> ListAsync(IApplicationTransaction transaction, CancellationToken token)
    {
        var rows = await Open(transaction).Books.AsNoTracking().OrderBy(x => x.Code).ToArrayAsync(token);
        return rows.Select(View).ToArray();
    }

    public async Task<AccountingBookView> LoadAsync(IApplicationTransaction transaction, Guid id, CancellationToken token)
    {
        var row = await Open(transaction).Books.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        return row is null ? throw new FoundationException(Errors.RESOURCE_NOT_FOUND) : View(row);
    }
}
