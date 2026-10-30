using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;

namespace Erp.Finance.Application;

// Initial consumer records delivery evidence, never a second financial effect.
// The durable Foundation inbox commits and deduplicates this trace with its receipt.
public sealed class JournalPostedTraceHandler(IJournalSessions sessions, IAuditWriter audit) : IIntegrationEventHandler
{
    public string EventType => JournalEntryPostedV1.EventType;
    public string Consumer => "finance.posted-trace.v1";
    public async Task HandleAsync(IApplicationTransaction transaction, IntegrationEvent message, CancellationToken cancellationToken)
    {
        if (transaction.Context.Source != ExecutionSource.Worker || transaction.Context.Action != "outbox.dispatch")
            throw new FoundationException(Errors.AUTH_SCOPE_DENIED);
        var payload = JsonSerializer.Deserialize<JournalEntryPostedV1>(message.Payload, JsonSerializerOptions.Web) ?? throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        if (message.Type != EventType || message.SourceId != payload.JournalId.ToString()) throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        var journal = await sessions.Open(transaction).LoadAsync(payload.JournalId, false, cancellationToken);
        if (journal.Status != JournalStatus.Posted || journal.JournalNumber != payload.JournalNumber || journal.Currency != payload.Currency || journal.PostingDate != payload.PostingDate)
            throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        audit.Append(transaction, new("JournalPostedEventDelivered", "ManualJournal", journal.Id.ToString()));
    }
}
