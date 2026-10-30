using System.Text.Json;
using Erp.Application.Abstractions;
using Erp.Finance.Contracts;
using Erp.Finance.Domain;
using Erp.Kernel;
namespace Erp.Finance.Application;
// Operational trace only. Financial effects have already committed in the correction command.
public sealed class JournalCorrectedTraceHandler(IJournalSessions sessions, IAuditWriter audit) : IIntegrationEventHandler
{
    public string EventType => JournalCorrectedV1.EventType;
    public string Consumer => "finance.corrected-trace.v1";
    public async Task HandleAsync(IApplicationTransaction tx, IntegrationEvent message, CancellationToken ct)
    {
        if (tx.Context.Source != ExecutionSource.Worker || tx.Context.Action != "outbox.dispatch")
            throw new FoundationException(Errors.AUTH_SCOPE_DENIED);
        var payload = JsonSerializer.Deserialize<JournalCorrectedV1>(message.Payload, JsonSerializerOptions.Web)
            ?? throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        var relation = await sessions.Open(tx).CorrectionAsync(payload.OriginalJournalId, ct);
        if (message.Type != EventType || message.SourceId != payload.OriginalJournalId.ToString()
            || relation?.Id != payload.CorrectionId || relation.OriginalJournalId != payload.OriginalJournalId
            || relation.ReversalJournalId != payload.ReversalJournalId || relation.ReplacementJournalId != payload.ReplacementJournalId)
            throw new FoundationException(Errors.EVENT_PAYLOAD_CONFLICT);
        audit.Append(tx, new("JournalCorrectedEventDelivered", "ManualJournal", payload.OriginalJournalId.ToString()));
    }
}
