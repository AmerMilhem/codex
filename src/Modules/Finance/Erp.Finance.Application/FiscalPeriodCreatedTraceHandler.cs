using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Kernel;

namespace Erp.Finance.Application;

// The worker records a durable delivery trace, with no financial side effect.
public sealed class FiscalPeriodCreatedTraceHandler(IPeriodStore periods, IAuditWriter audit) : IIntegrationEventHandler
{
    public string EventType => FiscalPeriodCreatedV1.EventType;
    public string Consumer => "finance.period-created-trace.v1";

    public async Task HandleAsync(IApplicationTransaction transaction, IntegrationEvent message, CancellationToken token)
    {
        var context = transaction.Context;
        if (context.Source != ExecutionSource.Worker || context.Action != "outbox.dispatch")
            throw new FoundationException(Errors.AUTH_SCOPE_DENIED);
        var payload = JsonSerializer.Deserialize<FiscalPeriodCreatedV1>(message.Payload, JsonSerializerOptions.Web)
            ?? throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        if (message.Type != EventType || message.SourceId != payload.FiscalPeriodId.ToString()
            || payload.TenantId != context.TenantId || payload.CompanyId != context.CompanyId)
            throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        var period = await periods.LoadAsync(transaction, payload.FiscalPeriodId, false, token);
        if (period.StartDate != payload.StartDate || period.EndDate != payload.EndDate || period.CreatedAt != payload.CreatedAt)
            throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        audit.Append(transaction, new("FiscalPeriodCreatedEventDelivered", "FiscalPeriod", period.Id.ToString()));
    }
}
