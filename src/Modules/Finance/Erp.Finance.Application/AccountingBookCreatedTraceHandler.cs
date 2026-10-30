using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Kernel;

namespace Erp.Finance.Application;

public sealed class AccountingBookCreatedTraceHandler(IAccountingBookSetupStore books, IAuditWriter audit) : IIntegrationEventHandler
{
    public string EventType => AccountingBookCreatedV1.EventType;
    public string Consumer => "finance.accounting-book-created-trace.v1";
    public async Task HandleAsync(IApplicationTransaction transaction, IntegrationEvent message, CancellationToken token)
    {
        var context = transaction.Context;
        if (context.Source != ExecutionSource.Worker || context.Action != "outbox.dispatch")
            throw new FoundationException(Errors.AUTH_SCOPE_DENIED);
        var payload = JsonSerializer.Deserialize<AccountingBookCreatedV1>(message.Payload, JsonSerializerOptions.Web)
            ?? throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        if (message.Type != EventType || message.SourceId != payload.AccountingBookId.ToString()
            || payload.TenantId != context.TenantId || payload.CompanyId != context.CompanyId)
            throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        var book = await books.LoadAsync(transaction, payload.AccountingBookId, token);
        if (book.Code != payload.Code || book.FunctionalCurrency != payload.FunctionalCurrency)
            throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        audit.Append(transaction, new("AccountingBookCreatedEventDelivered", "AccountingBook", book.Id.ToString()));
    }
}
