using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;

namespace Erp.Finance.Application;

public interface IAccountSetupStore
{
    Task<Guid> CreateAsync(IApplicationTransaction transaction, string code, string name, bool postingAllowed,
        DateTimeOffset now, PrincipalIdentity actor, CancellationToken token);
    Task<IReadOnlyList<AccountSetupView>> ListAsync(IApplicationTransaction transaction, CancellationToken token);
    Task<AccountSetupView> LoadAsync(IApplicationTransaction transaction, Guid id, CancellationToken token);
}

public sealed class AccountSetupQueries(IUnitOfWorkExecutor uow, IAccountSetupStore accounts)
{
    public Task<IReadOnlyList<AccountSetupView>> ListAsync(VerifiedContext context, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, AccountPermissions.View);
        return uow.ExecuteAsync(context, (tx, ct) => accounts.ListAsync(tx, ct), token);
    }
}

public sealed class CreateAccountCommand(IIdempotentCommands idempotency, IAccountSetupStore accounts,
    IAuditWriter audit, IOutboxWriter outbox, TimeProvider clock)
{
    public Task<CommandReceipt> ExecuteAsync(VerifiedContext context, CreateAccountRequest request,
        CommandIdentity identity, CancellationToken token = default)
    {
        FinanceAuthorization.Require(context, AccountPermissions.Create);
        if (request is null || identity.Command != "Finance.CreateAccount" || identity.Target != "accounts")
            throw new FoundationException(Errors.VALIDATION_FAILED);
        var (code, name) = AccountSetup.Validate(request.Code, request.Name);
        return idempotency.ExecuteAsync(context, identity, async (tx, ct) =>
        {
            var instant = clock.GetUtcNow();
            var now = instant.AddTicks(-(instant.Ticks % 10));
            var id = await accounts.CreateAsync(tx, code, name, request.PostingAllowed, now, context.Identity, ct);
            var evidence = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["name"] = name,
                ["postingAllowed"] = request.PostingAllowed
            };
            audit.Append(tx, new("AccountCreateRequested", "Account", id.ToString(), After: evidence));
            audit.Append(tx, new("AccountCreated", "Account", id.ToString(), After: evidence));
            var payload = new AccountCreatedV1(context.TenantId, context.CompanyId, id, code, request.PostingAllowed);
            outbox.Append(tx, new(Guid.CreateVersion7(), AccountCreatedV1.EventType, id.ToString(),
                JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), now));
            return new CommandReceipt(id, 1);
        }, token);
    }
}
