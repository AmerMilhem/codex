using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Kernel;

namespace Erp.Finance.Application;

public sealed class AccountCreatedTraceHandler(IAccountSetupStore accounts, IAuditWriter audit) : IIntegrationEventHandler
{
    public string EventType => AccountCreatedV1.EventType;
    public string Consumer => "finance.account-created-trace.v1";

    public async Task HandleAsync(IApplicationTransaction transaction, IntegrationEvent message, CancellationToken token)
    {
        var context = transaction.Context;
        if (context.Source != ExecutionSource.Worker || context.Action != "outbox.dispatch")
            throw new FoundationException(Errors.AUTH_SCOPE_DENIED);
        var payload = JsonSerializer.Deserialize<AccountCreatedV1>(message.Payload, JsonSerializerOptions.Web)
            ?? throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        if (message.Type != EventType || message.SourceId != payload.AccountId.ToString()
            || payload.TenantId != context.TenantId || payload.CompanyId != context.CompanyId)
            throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        var account = await accounts.LoadAsync(transaction, payload.AccountId, token);
        if (account.Code != payload.Code || account.PostingAllowed != payload.PostingAllowed)
            throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        audit.Append(transaction, new("AccountCreatedEventDelivered", "Account", account.Id.ToString()));
    }
}
