using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Kernel;
namespace Erp.Finance.Application;
// Acknowledges the versioned event through the durable inbox without creating any accounting effect.
public sealed class JournalReversedTraceHandler(IJournalSessions sessions, IAuditWriter audit) : IIntegrationEventHandler
{
    public string EventType => JournalEntryReversedV1.EventType;
    public string Consumer => "finance.reversed-trace.v1";
    public async Task HandleAsync(IApplicationTransaction tx, IntegrationEvent message, CancellationToken ct)
    {
        if (tx.Context.Source != ExecutionSource.Worker || tx.Context.Action != "outbox.dispatch") throw new FoundationException(Errors.AUTH_SCOPE_DENIED);
        var payload = JsonSerializer.Deserialize<JournalEntryReversedV1>(message.Payload, JsonSerializerOptions.Web) ?? throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        var session = sessions.Open(tx); var relation = await session.ReversalAsync(payload.OriginalJournalId, ct);
        if (message.Type != EventType || message.SourceId != payload.OriginalJournalId.ToString() || relation?.OriginalJournalId != payload.OriginalJournalId || relation.ReversalJournalId != payload.ReversalJournalId || relation.PostingDate != payload.PostingDate)
            throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        var reversal = await session.LoadAsync(payload.ReversalJournalId, false, ct);
        if (reversal.JournalNumber != payload.ReversalJournalNumber || reversal.Currency != payload.Currency) throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        audit.Append(tx, new("JournalReversedEventDelivered", "ManualJournal", payload.OriginalJournalId.ToString()));
    }
}
