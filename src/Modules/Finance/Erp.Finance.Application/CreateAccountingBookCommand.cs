using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;

namespace Erp.Finance.Application;

public interface IAccountingBookSetupStore
{
    Task<Guid> CreateAsync(IApplicationTransaction transaction, string code, string name, string currency,
        int minorUnits, DateTimeOffset now, PrincipalIdentity actor, CancellationToken token);
    Task<IReadOnlyList<AccountingBookView>> ListAsync(IApplicationTransaction transaction, CancellationToken token);
    Task<AccountingBookView> LoadAsync(IApplicationTransaction transaction, Guid id, CancellationToken token);
}

public sealed class AccountingBookSetupQueries(IUnitOfWorkExecutor uow, IAccountingBookSetupStore books)
{
    public Task<IReadOnlyList<AccountingBookView>> ListAsync(VerifiedContext context, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, BookPermissions.View);
        return uow.ExecuteAsync(context, (tx, ct) => books.ListAsync(tx, ct), token);
    }
}

public sealed class CreateAccountingBookCommand(IIdempotentCommands idempotency, IAccountingBookSetupStore books,
    IAuditWriter audit, IOutboxWriter outbox, TimeProvider clock)
{
    public Task<CommandReceipt> ExecuteAsync(VerifiedContext context, CreateAccountingBookRequest request,
        CommandIdentity identity, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, BookPermissions.Create);
        if (request is null || identity.Command != "Finance.CreateAccountingBook" || identity.Target != "books")
            throw new FoundationException(Errors.VALIDATION_FAILED);
        var (code, name, currency, minorUnits) = AccountingBookSetup.Validate(request.Code, request.Name, request.FunctionalCurrency);
        return idempotency.ExecuteAsync(context, identity, async (tx, ct) =>
        {
            var instant = clock.GetUtcNow();
            var now = instant.AddTicks(-(instant.Ticks % 10));
            var id = await books.CreateAsync(tx, code, name, currency, minorUnits, now, context.Identity, ct);
            var evidence = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["name"] = name,
                ["functionalCurrency"] = currency,
                ["minorUnits"] = minorUnits
            };
            audit.Append(tx, new("AccountingBookCreateRequested", "AccountingBook", id.ToString(), After: evidence));
            audit.Append(tx, new("AccountingBookCreated", "AccountingBook", id.ToString(), After: evidence));
            var payload = new AccountingBookCreatedV1(context.TenantId, context.CompanyId, id, code, currency);
            outbox.Append(tx, new(Guid.CreateVersion7(), AccountingBookCreatedV1.EventType, id.ToString(),
                JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), now));
            return new CommandReceipt(id, 1);
        }, token);
    }
}
