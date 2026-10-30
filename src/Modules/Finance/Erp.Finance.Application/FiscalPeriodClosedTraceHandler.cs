using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Kernel;

namespace Erp.Finance.Application;

// Durable Inbox deduplicates this operational trace. It never changes period or ledger state.
public sealed class FiscalPeriodClosedTraceHandler(IPeriodStore periods, IAuditWriter audit) : IIntegrationEventHandler
{
    public string EventType => FiscalPeriodClosedV1.EventType;
    public string Consumer => "finance.period-closed-trace.v1";

    public async Task HandleAsync(IApplicationTransaction transaction, IntegrationEvent message, CancellationToken token)
    {
        var context = transaction.Context;
        if (context.Source != ExecutionSource.Worker || context.Action != "outbox.dispatch")
            throw new FoundationException(Errors.AUTH_SCOPE_DENIED);
        var payload = JsonSerializer.Deserialize<FiscalPeriodClosedV1>(message.Payload, JsonSerializerOptions.Web)
            ?? throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        if (message.Type != EventType || message.SourceId != payload.FiscalPeriodId.ToString()
            || payload.TenantId != context.TenantId || payload.CompanyId != context.CompanyId)
            throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        var period = await periods.LoadAsync(transaction, payload.FiscalPeriodId, false, token);
        if (period.Status != "Closed" || period.StartDate != payload.StartDate || period.EndDate != payload.EndDate
            || period.ClosedAt != payload.ClosedAt)
            throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        audit.Append(transaction, new("FiscalPeriodClosedEventDelivered", "FiscalPeriod", period.Id.ToString()));
    }
}
