using Erp.Application.Abstractions;
using Erp.Finance.Application;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;
using Erp.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Finance.Infrastructure;

public sealed class AccountSetupStore : IAccountSetupStore
{
    private static FinanceDbContext Open(IApplicationTransaction transaction) =>
        PostgresTransaction.Require(transaction).Enlist(c => new FinanceDbContext(FinanceDbContext.Options(c)));

    private static AccountSetupView View(AccountRow row) => new(row.Id, row.Code, row.Name,
        row.Status.ToString(), row.PostingAllowed, row.CreatedAt, row.CreatedByIssuer, row.CreatedBySubject);

    public async Task<Guid> CreateAsync(IApplicationTransaction transaction, string code, string name, bool postingAllowed,
        DateTimeOffset now, PrincipalIdentity actor, CancellationToken token)
    {
        var tx = PostgresTransaction.Require(transaction);
        await using (var command = Sql.Command(tx.Connection,
            "SELECT count(*) FROM finance.lock_book_for_account_create()"))
            if ((long)(await command.ExecuteScalarAsync(token))! != 1)
                throw new FoundationException(FinanceErrors.BookMissing);
        await using (var duplicate = Sql.Command(tx.Connection, """
            SELECT count(*) FROM finance.accounts WHERE tenant_id=@tenant AND company_id=@company
              AND upper(btrim(code))=@code
            """, ("tenant", tx.Context.TenantId), ("company", tx.Context.CompanyId), ("code", code)))
            if ((long)(await duplicate.ExecuteScalarAsync(token))! != 0)
                throw new FoundationException(AccountSetupErrors.Duplicate);
        var id = Guid.CreateVersion7();
        var db = Open(transaction);
        db.Accounts.Add(new AccountRow
        {
            Id = id,
            TenantId = tx.Context.TenantId,
            CompanyId = tx.Context.CompanyId,
            Code = code,
            Name = name,
            Status = AccountStatus.Active,
            PostingAllowed = postingAllowed,
            CreatedAt = now,
            CreatedByIssuer = actor.Issuer,
            CreatedBySubject = actor.Subject
        });
        await db.SaveChangesAsync(token);
        return id;
    }

    public async Task<IReadOnlyList<AccountSetupView>> ListAsync(IApplicationTransaction transaction, CancellationToken token)
    {
        var rows = await Open(transaction).Accounts.AsNoTracking().OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Take(1000).ToArrayAsync(token);
        return rows.Select(View).ToArray();
    }

    public async Task<AccountSetupView> LoadAsync(IApplicationTransaction transaction, Guid id, CancellationToken token)
    {
        var row = await Open(transaction).Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        return row is null ? throw new FoundationException(Errors.RESOURCE_NOT_FOUND) : View(row);
    }
}
